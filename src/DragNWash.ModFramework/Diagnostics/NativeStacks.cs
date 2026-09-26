using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace DragNWash.ModFramework.Diagnostics
{
    /// <summary>
    /// Native stacks on 64-bit Windows: every thread of the game, Unity's own
    /// included, with native frames named from the PDBs the game ships
    /// (module!function) and the runtime's compiled methods named by Mono
    /// (Type:Method). Experimental (core 1.7).
    /// </summary>
    /// <remarks>
    /// A thread is stopped only to read its registers and walk its stack, and
    /// is always let go again within 100 ms: the walk runs on a thread made
    /// for it, and nothing in between allocates, because the stopped thread
    /// may hold the heap or the runtime's locks. Names are looked up after.
    /// </remarks>
    public static class NativeStacks
    {
        private const int ContextSize = 1232;
        private const int MaxFrames = 128;
        private const int Patience = 100;

        private static readonly object Gate = new object();
        private static readonly Dictionary<long, string> Names = new Dictionary<long, string>();
        private static readonly long[] Ips = new long[MaxFrames];
        private static int _ipCount;
        private static volatile bool _abandon;
        private static IntPtr _contextMemory, _context, _start, _done;
        private static Thread _walker;
        private static uint _walkerId;
        private static bool _broken, _symbols;
        private static List<(long start, long end, string name)> _modules;

        /// <summary>The main thread's Windows thread id; 0 elsewhere.</summary>
        public static uint MainThreadId { get; internal set; }

        /// <summary>64-bit Windows, and the walker has not given up.</summary>
        public static bool Available => Environment.OSVersion.Platform == PlatformID.Win32NT && IntPtr.Size == 8 && !_broken;

        /// <summary>Whether a frame text came from native code (module!function) rather than a managed method.</summary>
        public static bool IsNative(string frame) => frame != null && frame.IndexOf('!') > 0;

        // Called on the main thread at start (Windows).
        internal static void NoteMainThread()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT) MainThreadId = GetCurrentThreadId();
        }

        /// <summary>Every thread of the game with its frames, the main thread first. Not from the main thread's own walk: it is read like any other.</summary>
        public static List<ThreadStack> CaptureAll(out string reason)
        {
            reason = null;
            if (!Available)
            {
                reason = "native stacks need 64-bit Windows";
                return null;
            }
            var ids = new List<uint>();
            using (Process me = Process.GetCurrentProcess())
            {
                foreach (ProcessThread t in me.Threads) ids.Add((uint)t.Id);
            }
            var list = new List<ThreadStack>();
            foreach (uint id in ids)
            {
                if (id == GetCurrentThreadId() || id == _walkerId) continue;
                long[] ips = CaptureIps(id, out string why);
                var frames = new List<string>();
                if (ips != null) foreach (long ip in ips) frames.Add(Resolve(ip));
                else if (why != null) frames.Add("(" + why + ")");
                list.Add(new ThreadStack { Id = (int)id, Name = ThreadName(id), IsMain = id == MainThreadId, Frames = frames, Native = true });
            }
            list.Sort((a, b) => a.IsMain != b.IsMain ? (a.IsMain ? -1 : 1) : a.Id.CompareTo(b.Id));
            return list;
        }

        /// <summary>The main thread with native frames; null with a reason when it could not be read.</summary>
        public static ThreadStack CaptureMain(out string reason)
        {
            reason = null;
            if (!Available || MainThreadId == 0)
            {
                reason = "native stacks need 64-bit Windows";
                return null;
            }
            if (GetCurrentThreadId() == MainThreadId)
            {
                // A thread cannot stop itself: the walk runs from the pool.
                ThreadStack result = null;
                string why = null;
                var worker = new Thread(() => { result = CaptureMain(out why); }) { IsBackground = true };
                worker.Start();
                worker.Join(2000);
                reason = why;
                return result;
            }
            long[] ips = CaptureIps(MainThreadId, out reason);
            if (ips == null) return null;
            var frames = new List<string>();
            foreach (long ip in ips) frames.Add(Resolve(ip));
            return new ThreadStack { Id = (int)MainThreadId, Name = ThreadName(MainThreadId), IsMain = true, Frames = frames, Native = true };
        }

        // The instruction addresses of one thread, innermost first; null with a
        // reason when the thread could not be read. Never from that thread.
        internal static long[] CaptureIps(uint threadId, out string reason)
        {
            reason = null;
            lock (Gate)
            {
                if (!Available)
                {
                    reason = "native stacks are off";
                    return null;
                }
                EnsureWalker();
                IntPtr thread = OpenThread(0x0002 | 0x0008 | 0x0040, false, threadId);
                if (thread == IntPtr.Zero)
                {
                    reason = "could not open the thread";
                    return null;
                }
                bool walked = false, late = false;
                try
                {
                    for (int i = 0; i < ContextSize; i += 8) Marshal.WriteInt64(_context, i, 0);
                    Marshal.WriteInt32(_context, 0x30, 0x10000B); // CONTEXT_FULL
                    if (SuspendThread(thread) == uint.MaxValue)
                    {
                        reason = "could not stop the thread";
                        return null;
                    }
                    try
                    {
                        if (GetThreadContext(thread, _context))
                        {
                            _abandon = false;
                            SetEvent(_start);
                            walked = WaitForSingleObject(_done, Patience) == 0;
                            if (!walked)
                            {
                                _abandon = true;
                                late = true;
                            }
                        }
                    }
                    finally
                    {
                        ResumeThread(thread);
                    }
                    if (late)
                    {
                        // Let the walk finish before its buffers are used again.
                        if (WaitForSingleObject(_done, 2000) != 0)
                        {
                            _broken = true;
                            ModFramework.Log.LogWarning("[stacks] a native stack walk did not come back; native stacks are off for this session.");
                        }
                        reason = "the walk took too long; the thread was let go";
                        return null;
                    }
                    if (!walked)
                    {
                        reason = "could not read the thread's registers";
                        return null;
                    }
                    var ips = new long[_ipCount];
                    Array.Copy(Ips, ips, _ipCount);
                    return ips;
                }
                finally
                {
                    CloseHandle(thread);
                }
            }
        }

        /// <summary>A name for an instruction address: module!function +0x1a, Type:Method + 0x1a for compiled managed code, or module+0x1234.</summary>
        public static string Resolve(long ip)
        {
            lock (Names)
            {
                if (Names.TryGetValue(ip, out string known)) return known;
            }
            string name = null;
            (long start, long end, string module) = ModuleOf(ip);
            if (module != null)
            {
                name = Symbol(ip, module) ?? $"{module}+0x{ip - start:x}";
            }
            else
            {
                if (MonoNative.Load() && MonoNative.Pmip != null)
                {
                    string text = MonoNative.Text(MonoNative.Pmip(new IntPtr(ip)), true);
                    if (!string.IsNullOrEmpty(text)) name = Managed(text);
                }
                if (name == null) name = $"0x{ip:x}";
            }
            lock (Names)
            {
                if (Names.Count > 50000) Names.Clear();
                Names[ip] = name;
            }
            return name;
        }

        // " Game.Type:Method (int) [{0x1234}] + 0x1a (0x...) [0x... - Unity Root Domain]"
        // becomes "Game.Type:Method (int) + 0x1a".
        private static readonly Regex PmipShape = new Regex(@"^\s*(.*?)\s*\[\{0x[0-9a-fA-F]+\}\]\s*(\+ 0x[0-9a-fA-F]+)?", RegexOptions.Compiled);

        private static string Managed(string pmip)
        {
            Match m = PmipShape.Match(pmip);
            return m.Success ? (m.Groups[1].Value + (m.Groups[2].Success ? " " + m.Groups[2].Value : "")).Trim() : pmip.Trim();
        }

        private static (long, long, string) ModuleOf(long ip)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                if (_modules == null || pass == 1)
                {
                    var modules = new List<(long, long, string)>();
                    using (Process me = Process.GetCurrentProcess())
                    {
                        foreach (ProcessModule m in me.Modules)
                        {
                            long b = m.BaseAddress.ToInt64();
                            modules.Add((b, b + m.ModuleMemorySize, Path.GetFileNameWithoutExtension(m.ModuleName)));
                        }
                    }
                    _modules = modules;
                }
                foreach ((long start, long end, string name) in _modules)
                {
                    if (ip >= start && ip < end) return (start, end, name);
                }
            }
            return (0, 0, null);
        }

        private static string Symbol(long ip, string module)
        {
            lock (CrashReports.DbgHelpGate)
            {
                try
                {
                    if (!_symbols)
                    {
                        _symbols = true;
                        SymSetOptions(0x2 | 0x4 | 0x200 | 0x80000); // undecorated, deferred loads, no error boxes, no prompts
                        string game = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                        if (!SymInitializeW(new IntPtr(-1), game, true))
                        {
                            ModFramework.Log.LogWarning("[stacks] DbgHelp could not start; native frames show as module+offset.");
                        }
                    }
                    IntPtr info = Marshal.AllocHGlobal(88 + 2 * 1024);
                    try
                    {
                        for (int i = 0; i < 88; i += 4) Marshal.WriteInt32(info, i, 0);
                        Marshal.WriteInt32(info, 0, 88);    // SizeOfStruct
                        Marshal.WriteInt32(info, 80, 1024); // MaxNameLen
                        if (!SymFromAddrW(new IntPtr(-1), (ulong)ip, out ulong displacement, info)) return null;
                        string name = Marshal.PtrToStringUni(new IntPtr(info.ToInt64() + 84));
                        return $"{module}!{name} +0x{displacement:x}";
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(info);
                    }
                }
                catch
                {
                    return null;
                }
            }
        }

        private static string ThreadName(uint id)
        {
            IntPtr thread = OpenThread(0x0040 | 0x0400, false, id); // query information, limited
            if (thread == IntPtr.Zero) return "";
            try
            {
                if (GetThreadDescription(thread, out IntPtr text) >= 0 && text != IntPtr.Zero)
                {
                    string name = Marshal.PtrToStringUni(text);
                    LocalFree(text);
                    return name ?? "";
                }
            }
            catch (EntryPointNotFoundException)
            {
                // Windows before 10 1607.
            }
            finally
            {
                CloseHandle(thread);
            }
            return "";
        }

        private static void EnsureWalker()
        {
            if (_walker != null) return;
            _contextMemory = Marshal.AllocHGlobal(ContextSize + 16);
            _context = new IntPtr((_contextMemory.ToInt64() + 15) & ~15L);
            _start = CreateEventW(IntPtr.Zero, false, false, null);
            _done = CreateEventW(IntPtr.Zero, false, false, null);
            using (var ready = new ManualResetEvent(false))
            {
                _walker = new Thread(() =>
                {
                    _walkerId = GetCurrentThreadId();
                    // Once on its own registers, so every call below is ready
                    // before a thread is ever stopped for it.
                    RtlCaptureContext(_context);
                    Walk();
                    ready.Set();
                    while (true)
                    {
                        WaitForSingleObject(_start, uint.MaxValue);
                        Walk();
                        SetEvent(_done);
                    }
                }) { IsBackground = true, Name = "NativeStacks walker" };
                _walker.Start();
                ready.WaitOne(2000);
            }
        }

        // x64 unwinding from the context in _context: each frame's unwind data
        // (the modules' and the runtime's registered tables), or a leaf frame's
        // return address. Reads stay inside the stack region the walk began in.
        private static void Walk()
        {
            _ipCount = 0;
            long rip = Marshal.ReadInt64(_context, 0xF8);
            long rsp = Marshal.ReadInt64(_context, 0x98);
            if (VirtualQuery(new IntPtr(rsp), out MemoryInfo region, new IntPtr(Marshal.SizeOf(typeof(MemoryInfo)))) == IntPtr.Zero) return;
            long high = region.BaseAddress.ToInt64() + region.RegionSize.ToInt64();
            while (_ipCount < MaxFrames && !_abandon && rip != 0 && rsp >= region.BaseAddress.ToInt64() && rsp + 8 <= high)
            {
                Ips[_ipCount++] = rip;
                IntPtr entry = RtlLookupFunctionEntry((ulong)rip, out ulong imageBase, IntPtr.Zero);
                long before = rsp;
                if (entry == IntPtr.Zero)
                {
                    rip = Marshal.ReadInt64(new IntPtr(rsp));
                    rsp += 8;
                    Marshal.WriteInt64(_context, 0xF8, rip);
                    Marshal.WriteInt64(_context, 0x98, rsp);
                }
                else
                {
                    RtlVirtualUnwind(0, imageBase, (ulong)rip, entry, _context, out IntPtr _, out ulong _, IntPtr.Zero);
                    rip = Marshal.ReadInt64(_context, 0xF8);
                    rsp = Marshal.ReadInt64(_context, 0x98);
                }
                if (rsp <= before) break;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryInfo
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenThread(uint access, bool inherit, uint id);
        [DllImport("kernel32.dll")] private static extern uint SuspendThread(IntPtr thread);
        [DllImport("kernel32.dll")] private static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll")] private static extern bool GetThreadContext(IntPtr thread, IntPtr context);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateEventW(IntPtr attributes, bool manualReset, bool initial, string name);
        [DllImport("kernel32.dll")] private static extern bool SetEvent(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll")] private static extern void RtlCaptureContext(IntPtr context);
        [DllImport("kernel32.dll")] private static extern IntPtr RtlLookupFunctionEntry(ulong controlPc, out ulong imageBase, IntPtr history);
        [DllImport("kernel32.dll")] private static extern IntPtr RtlVirtualUnwind(uint handlerType, ulong imageBase, ulong controlPc, IntPtr functionEntry, IntPtr context, out IntPtr handlerData, out ulong establisherFrame, IntPtr contextPointers);
        [DllImport("kernel32.dll")] private static extern IntPtr VirtualQuery(IntPtr address, out MemoryInfo info, IntPtr length);
        [DllImport("kernel32.dll")] private static extern int GetThreadDescription(IntPtr thread, out IntPtr description);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
        [DllImport("dbghelp.dll")] private static extern uint SymSetOptions(uint options);
        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode)] private static extern bool SymInitializeW(IntPtr process, string searchPath, bool invade);
        [DllImport("dbghelp.dll")] private static extern bool SymFromAddrW(IntPtr process, ulong address, out ulong displacement, IntPtr symbol);
    }
}

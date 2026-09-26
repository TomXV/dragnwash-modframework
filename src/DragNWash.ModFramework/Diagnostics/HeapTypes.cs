using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DragNWash.ModFramework.Diagnostics
{
    /// <summary>One managed type in a <see cref="HeapCount"/>.</summary>
    public sealed class TypeCount
    {
        /// <summary>Namespace.Name as Mono names it (generic types as Name`2, arrays as Name[]).</summary>
        public string Name { get; internal set; }

        /// <summary>The assembly the type comes from.</summary>
        public string Assembly { get; internal set; }

        /// <summary>How many objects of it were reachable.</summary>
        public long Count { get; internal set; }

        /// <summary>Their size together, bytes (each object's own size, not what it points to).</summary>
        public long Bytes { get; internal set; }

        /// <summary>Count minus the count at the count before; null when there was none or the type was not in it.</summary>
        public long? Change { get; internal set; }
    }

    /// <summary>What <see cref="HeapTypes.Count"/> found.</summary>
    public sealed class HeapCount
    {
        /// <summary>When it was counted.</summary>
        public DateTime Time { get; internal set; }

        /// <summary>Objects counted.</summary>
        public long Objects { get; internal set; }

        /// <summary>Their size together, bytes.</summary>
        public long Bytes { get; internal set; }

        /// <summary>How long the game was stopped, seconds.</summary>
        public double Seconds { get; internal set; }

        /// <summary>True when there were more objects than room to note them; the counts are then too low.</summary>
        public bool Truncated { get; internal set; }

        /// <summary>Every type, the most bytes first.</summary>
        public IReadOnlyList<TypeCount> Types { get; internal set; }
    }

    /// <summary>
    /// Counts the managed objects by type: everything reachable from static
    /// fields (not what only a running method's locals hold), walked with the
    /// runtime's own liveness code, the one Unity uses to find unused assets.
    /// The whole game stops while it walks, from a tenth of a second to a
    /// second. Experimental (core 1.7): main thread only, and only on a button.
    /// </summary>
    public static class HeapTypes
    {
        private const int Slots = 1 << 17;
        private const int BlockSize = 8 * 1024;

        private static readonly long[] Keys = new long[Slots];
        private static readonly long[] Counts = new long[Slots];
        private static readonly long[] Sizes = new long[Slots];

        // Filled from inside the walk, where nothing may allocate: the world is
        // stopped and a thread that was stopped may hold the heap lock. So all
        // the memory is taken before, and handed out here.
        private static IntPtr _objects, _pool;
        private static long _objectCount, _objectCapacity, _poolUsed, _poolBlocks;
        private static bool _overflow;

        private static readonly MonoNativeCallbacks.Register RegisterDelegate = OnObjects;
        private static readonly MonoNativeCallbacks.Reallocate ReallocateDelegate = OnReallocate;
        private static IntPtr _registerPtr, _reallocatePtr;

        /// <summary>Whether an assembly name is one of the mods' (a plugin in BepInEx/plugins).</summary>
        public static bool IsModAssembly(string assembly)
        {
            if (_modAssemblies == null)
            {
                _modAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
                {
                    if (info.Instance != null) _modAssemblies.Add(info.Instance.GetType().Assembly.GetName().Name);
                }
            }
            return assembly != null && _modAssemblies.Contains(assembly);
        }

        private static HashSet<string> _modAssemblies;

        /// <summary>The last count, or null.</summary>
        public static HeapCount Last { get; private set; }

        /// <summary>Whether this Mono has what counting needs.</summary>
        public static bool Available =>
            MonoNative.Load() && MonoNative.LivenessAllocate != null && MonoNative.LivenessFromStatics != null && MonoNative.LivenessFromRoot != null
            && MonoNative.LivenessFinalize != null && MonoNative.LivenessFreeStruct != null && MonoNative.StopWorld != null && MonoNative.StartWorld != null
            && MonoNative.ObjectGetClass != null && MonoNative.ObjectGetSize != null && MonoNative.ClassGetName != null;

        /// <summary>Counts now. Main thread only; stops the game while it walks.</summary>
        public static HeapCount Count()
        {
            if (!Available) throw new InvalidOperationException("this Mono does not export the liveness functions");
            if (_registerPtr == IntPtr.Zero)
            {
                _registerPtr = Marshal.GetFunctionPointerForDelegate(RegisterDelegate);
                _reallocatePtr = Marshal.GetFunctionPointerForDelegate(ReallocateDelegate);
            }
            long heap = Math.Max(GC.GetTotalMemory(false), 16L << 20);
            _objectCapacity = heap / 16 + 65536;
            _poolBlocks = _objectCapacity * 8 / (BlockSize - 64) + 256;
            _objects = Marshal.AllocHGlobal(new IntPtr(_objectCapacity * 8));
            _pool = Marshal.AllocHGlobal(new IntPtr(_poolBlocks * BlockSize));
            try
            {
                WarmUp();
                Reset();
                IntPtr state = MonoNative.LivenessAllocate(IntPtr.Zero, 0, _registerPtr, IntPtr.Zero, _reallocatePtr);
                var clock = Stopwatch.StartNew();
                MonoNative.StopWorld();
                try
                {
                    MonoNative.LivenessFromStatics(state);
                    MonoNative.LivenessFinalize(state);
                    // The marks are off again: each object's class and size can be read.
                    for (long i = 0; i < _objectCount; i++)
                    {
                        IntPtr o = ReadObject(i);
                        if (o == IntPtr.Zero) continue;
                        Add(MonoNative.ObjectGetClass(o).ToInt64(), MonoNative.ObjectGetSize(o));
                    }
                }
                finally
                {
                    MonoNative.StartWorld();
                }
                clock.Stop();
                MonoNative.LivenessFreeStruct(state);
                HeapCount result = Collect(clock.Elapsed.TotalSeconds);
                Last = result;
                ModFramework.Log.LogInfo($"[memory] counted {result.Objects} objects of {result.Types.Count} types ({MemoryWatch.Size(result.Bytes)}); the game was stopped {result.Seconds * 1000:0} ms.");
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(_objects);
                Marshal.FreeHGlobal(_pool);
                _objects = _pool = IntPtr.Zero;
            }
        }

        private static IntPtr ReadObject(long i) => Marshal.ReadIntPtr(new IntPtr(_objects.ToInt64() + i * 8));

        // Runs the same walk once over one small array of our own, with the
        // world running: the first native call into each callback, and the
        // first call of each function, compile their wrappers here, not while
        // the world is stopped.
        private static void WarmUp()
        {
            Reset();
            var probe = new int[1];
            GCHandle pin = GCHandle.Alloc(probe, GCHandleType.Pinned);
            try
            {
                // An array's elements start 32 bytes into it on 64-bit Mono
                // (vtable, sync, bounds, length).
                IntPtr root = new IntPtr(pin.AddrOfPinnedObject().ToInt64() - 4 * IntPtr.Size);
                IntPtr state = MonoNative.LivenessAllocate(IntPtr.Zero, 0, _registerPtr, IntPtr.Zero, _reallocatePtr);
                MonoNative.LivenessFromRoot(root, state);
                MonoNative.LivenessFinalize(state);
                for (long i = 0; i < _objectCount; i++)
                {
                    IntPtr o = ReadObject(i);
                    Add(MonoNative.ObjectGetClass(o).ToInt64(), MonoNative.ObjectGetSize(o));
                }
                MonoNative.LivenessFreeStruct(state);
            }
            finally
            {
                pin.Free();
            }
            GC.KeepAlive(probe);
        }

        private static void Reset()
        {
            Array.Clear(Keys, 0, Slots);
            Array.Clear(Counts, 0, Slots);
            Array.Clear(Sizes, 0, Slots);
            _objectCount = 0;
            _poolUsed = 0;
            _overflow = false;
        }

        // Batches of up to 64 objects, found by the walk (their class pointers
        // are marked while the walk runs, so only the addresses are kept).
        private static void OnObjects(IntPtr objects, int count, IntPtr userdata)
        {
            for (int i = 0; i < count; i++)
            {
                if (_objectCount >= _objectCapacity)
                {
                    _overflow = true;
                    return;
                }
                Marshal.WriteIntPtr(new IntPtr(_objects.ToInt64() + _objectCount * 8), Marshal.ReadIntPtr(new IntPtr(objects.ToInt64() + (long)i * IntPtr.Size)));
                _objectCount++;
            }
        }

        // The walk's own block lists: 8 KB blocks from the pool; freeing is a
        // no-op, the pool goes as a whole.
        private static IntPtr OnReallocate(IntPtr pointer, int size, IntPtr userdata)
        {
            if (size == 0) return IntPtr.Zero;
            if (pointer == IntPtr.Zero && size <= BlockSize && _poolUsed < _poolBlocks)
            {
                return new IntPtr(_pool.ToInt64() + BlockSize * _poolUsed++);
            }
            // Out of pool: the heap, which the world being stopped makes a risk,
            // but less of one than handing back nothing.
            _overflow = true;
            return pointer == IntPtr.Zero ? Marshal.AllocHGlobal(size) : Marshal.ReAllocHGlobal(pointer, new IntPtr(size));
        }

        private static void Add(long klass, uint size)
        {
            if (klass == 0) return;
            int i = (int)((ulong)(klass >> 3) * 0x9E3779B1UL % Slots);
            for (int probe = 0; probe < Slots; probe++)
            {
                if (Keys[i] == klass || Keys[i] == 0)
                {
                    Keys[i] = klass;
                    Counts[i]++;
                    Sizes[i] += size;
                    return;
                }
                i = (i + 1) & (Slots - 1);
            }
        }

        private static HeapCount Collect(double seconds)
        {
            var previous = new Dictionary<string, long>();
            if (Last != null)
            {
                foreach (TypeCount t in Last.Types) previous[t.Assembly + "|" + t.Name] = t.Count;
            }
            var types = new List<TypeCount>();
            long objects = 0, bytes = 0;
            for (int i = 0; i < Slots; i++)
            {
                if (Keys[i] == 0) continue;
                var klass = new IntPtr(Keys[i]);
                string ns = MonoNative.Text(MonoNative.ClassGetNamespace?.Invoke(klass) ?? IntPtr.Zero, false);
                string name = MonoNative.Text(MonoNative.ClassGetName(klass), false) ?? "?";
                string assembly = "";
                if (MonoNative.ClassGetImage != null && MonoNative.ImageGetName != null)
                {
                    assembly = MonoNative.Text(MonoNative.ImageGetName(MonoNative.ClassGetImage(klass)), false) ?? "";
                }
                var t = new TypeCount { Name = string.IsNullOrEmpty(ns) ? name : ns + "." + name, Assembly = assembly, Count = Counts[i], Bytes = Sizes[i] };
                if (previous.TryGetValue(t.Assembly + "|" + t.Name, out long before)) t.Change = t.Count - before;
                else if (Last != null) t.Change = t.Count;
                types.Add(t);
                objects += t.Count;
                bytes += t.Bytes;
            }
            types.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
            return new HeapCount { Time = DateTime.Now, Objects = objects, Bytes = bytes, Seconds = seconds, Truncated = _overflow, Types = types };
        }
    }

    // The two callbacks the liveness walk takes.
    internal static class MonoNativeCallbacks
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void Register(IntPtr objects, int count, IntPtr userdata);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate IntPtr Reallocate(IntPtr pointer, int size, IntPtr userdata);
    }
}

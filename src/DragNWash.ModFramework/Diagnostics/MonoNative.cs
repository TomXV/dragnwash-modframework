using System;
using System.Runtime.InteropServices;

namespace DragNWash.ModFramework.Diagnostics
{
    // Functions the game's Mono runtime exports, found at run time: the
    // library is mono-2.0-bdwgc.dll on Windows and libmonobdwgc-2.0.so on
    // Linux. Null when a function is missing (another Unity), and callers say
    // so instead of failing.
    internal static class MonoNative
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate IntPtr PtrToPtr(IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate uint PtrToUInt(IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void PtrToVoid(IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void NoArgs();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate IntPtr AllocateStruct(IntPtr filter, uint maxCount, IntPtr callback, IntPtr userdata, IntPtr reallocate);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void FromRoot(IntPtr root, IntPtr state);

        private static bool _looked;
        private static IntPtr _module;

        internal static PtrToPtr ObjectGetClass, ClassGetName, ClassGetNamespace, ClassGetImage, ImageGetName, Pmip;
        internal static PtrToUInt ObjectGetSize;
        internal static PtrToVoid LivenessFromStatics, LivenessFinalize, LivenessFreeStruct, GFree;
        internal static FromRoot LivenessFromRoot;
        internal static AllocateStruct LivenessAllocate;
        internal static NoArgs StopWorld, StartWorld;

        internal static bool Load()
        {
            if (_looked) return _module != IntPtr.Zero;
            _looked = true;
            try
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    _module = GetModuleHandle("mono-2.0-bdwgc.dll");
                }
                else
                {
                    // RTLD_NOW | RTLD_NOLOAD: the one Unity loaded, never a second copy.
                    _module = dlopen("libmonobdwgc-2.0.so", 2 | 4);
                }
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning("[memory] the Mono runtime's functions could not be looked up: " + ex.Message);
            }
            if (_module == IntPtr.Zero) return false;
            ObjectGetClass = Get<PtrToPtr>("mono_object_get_class");
            ObjectGetSize = Get<PtrToUInt>("mono_object_get_size");
            ClassGetName = Get<PtrToPtr>("mono_class_get_name");
            ClassGetNamespace = Get<PtrToPtr>("mono_class_get_namespace");
            ClassGetImage = Get<PtrToPtr>("mono_class_get_image");
            ImageGetName = Get<PtrToPtr>("mono_image_get_name");
            Pmip = Get<PtrToPtr>("mono_pmip");
            GFree = Get<PtrToVoid>("mono_unity_g_free") ?? Get<PtrToVoid>("mono_free");
            LivenessAllocate = Get<AllocateStruct>("mono_unity_liveness_allocate_struct");
            LivenessFromStatics = Get<PtrToVoid>("mono_unity_liveness_calculation_from_statics");
            LivenessFromRoot = Get<FromRoot>("mono_unity_liveness_calculation_from_root");
            LivenessFinalize = Get<PtrToVoid>("mono_unity_liveness_finalize");
            LivenessFreeStruct = Get<PtrToVoid>("mono_unity_liveness_free_struct");
            StopWorld = Get<NoArgs>("mono_unity_stop_gc_world");
            StartWorld = Get<NoArgs>("mono_unity_start_gc_world");
            return true;
        }

        // A C string Mono returned, as text; freed with Mono's own free when it was a copy.
        internal static string Text(IntPtr s, bool free)
        {
            if (s == IntPtr.Zero) return null;
            string text = Marshal.PtrToStringAnsi(s);
            if (free) GFree?.Invoke(s);
            return text;
        }

        private static T Get<T>(string name) where T : class
        {
            IntPtr p = Environment.OSVersion.Platform == PlatformID.Win32NT ? GetProcAddress(_module, name) : dlsym(_module, name);
            return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("libdl.so.2")]
        private static extern IntPtr dlopen(string file, int mode);

        [DllImport("libdl.so.2")]
        private static extern IntPtr dlsym(IntPtr handle, string name);
    }
}

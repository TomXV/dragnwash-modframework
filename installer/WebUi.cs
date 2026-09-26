using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace DragNWash.Installer
{
    // The window in WebView2, when it can be had. Install.exe is one file, so what
    // WebView2 needs comes out of its resources: the two managed DLLs are loaded from
    // memory, and WebView2Loader.dll for this process's processor is written once under
    // %LOCALAPPDATA% and checked against its SHA-256 before every use. Nothing here
    // names a WebView2 type, so this class runs before the DLLs are there; the window
    // itself (InstallerWindow, InstallerSession) is only reached through Show below.
    // When anything fails, Run says why and Program opens the WinForms window instead.
    internal static class WebUi
    {
        // Where the loader and WebView2's own data go: per user, never next to the exe,
        // which may sit in Downloads or on a read-only drive.
        internal static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DragNWash ModFramework", "Installer");

        private static readonly Dictionary<string, Assembly> Loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        private static bool _resolving;

        // DNW_INSTALLER_CLASSIC=1 opens the WinForms window, for testing it and for anyone
        // whose WebView2 misbehaves.
        internal static bool Wanted()
        {
            return Environment.GetEnvironmentVariable("DNW_INSTALLER_CLASSIC") != "1";
        }

        // Shows the window and runs it until it closes: null. Otherwise why it couldn't be
        // shown, in English for the WinForms window's log; nothing has been done then.
        // manifest: null when it couldn't be read (startError says why; the window then
        // shows only that failure).
        internal static string Run(ModManifest manifest, InstallerException startError, string payload)
        {
            string loader;
            try
            {
                Resolve();
                loader = Loader();
            }
            catch (Exception ex)
            {
                return "WebView2 parts could not be unpacked: " + ex.Message;
            }
            try
            {
                return Show(loader, manifest, startError, payload);
            }
            catch (Exception ex)
            {
                return "WebView2 could not start: " + ex.Message;
            }
        }

        // Kept out of line, so the WebView2 types in it are resolved only once the DLLs can be found.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string Show(string loader, ModManifest manifest, InstallerException startError, string payload)
        {
            return InstallerSession.Run(loader, manifest, startError, payload);
        }

        private static void Resolve()
        {
            if (_resolving)
            {
                return;
            }
            _resolving = true;
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                string name = new AssemblyName(e.Name).Name;
                if (name != "Microsoft.Web.WebView2.Core" && name != "Microsoft.Web.WebView2.WinForms")
                {
                    return null;
                }
                lock (Loaded)
                {
                    if (!Loaded.TryGetValue(name, out Assembly assembly))
                    {
                        byte[] bytes = Resource("webview2/" + name + ".dll");
                        assembly = bytes == null ? null : Assembly.Load(bytes);
                        Loaded[name] = assembly;
                    }
                    return assembly;
                }
            };
        }

        // The folder with this processor's WebView2Loader.dll, written there if it isn't yet.
        // The folder is named after the file's SHA-256, so another Install.exe with another
        // loader never uses this one, and a file there that doesn't match is written again.
        private static string Loader()
        {
            string arch;
            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.X64:
                    arch = "x64";
                    break;
                case Architecture.Arm64:
                    arch = "arm64";
                    break;
                case Architecture.X86:
                    arch = "x86";
                    break;
                default:
                    throw new PlatformNotSupportedException("no WebView2Loader.dll for " + RuntimeInformation.ProcessArchitecture);
            }
            byte[] bytes = Resource($"webview2/{arch}/WebView2Loader.dll") ?? throw new FileNotFoundException("WebView2Loader.dll for " + arch + " isn't in the exe");
            string hash = Sha256(bytes);
            string folder = Path.Combine(Folder, "WebView2Loader", arch + "-" + hash.Substring(0, 16));
            string file = Path.Combine(folder, "WebView2Loader.dll");
            if (File.Exists(file) && Sha256(File.ReadAllBytes(file)) == hash)
            {
                return folder;
            }
            Directory.CreateDirectory(folder);
            // Written beside it, then moved over it in one step: a second Install.exe
            // starting at the same moment never finds half a file.
            string part = file + "." + Guid.NewGuid().ToString("N") + ".part";
            File.WriteAllBytes(part, bytes);
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
                File.Move(part, file);
            }
            catch (IOException) when (File.Exists(file))
            {
                // The other Install.exe won; its file is checked below like any other.
            }
            finally
            {
                if (File.Exists(part))
                {
                    File.Delete(part);
                }
            }
            if (Sha256(File.ReadAllBytes(file)) != hash)
            {
                throw new IOException("the WebView2Loader.dll written to " + folder + " doesn't match the one in the exe");
            }
            return folder;
        }

        private static byte[] Resource(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    return null;
                }
                var copy = new MemoryStream();
                stream.CopyTo(copy);
                return copy.ToArray();
            }
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}

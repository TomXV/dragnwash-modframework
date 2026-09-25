using System;
using System.IO;

namespace DragNWash.Installer.Tests
{
    // Checks of the mod icon: mod-install.json's "icon" (Manifest.cs's validation) and
    // ModIcon.DataUrl (the PNG/JPEG check and the data URL), plus InstallerCore.ModIconPath
    // resolving it under a payload or a game folder.
    internal static partial class Program
    {
        // A minimal real PNG (1x1, transparent) and JPEG (a valid SOI/EOI with a tiny
        // scan), for the magic-byte check; and a file that is neither.
        private static readonly byte[] PngBytes =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89,
        };

        private static readonly byte[] JpegBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };

        private static void ModIconManifest()
        {
            ModManifest Load(string json)
            {
                string dir = Path.Combine(Path.GetTempPath(), "dnw-installer-tests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, ModManifest.FileName);
                File.WriteAllText(path, json);
                try
                {
                    return ModManifest.Load(path);
                }
                finally
                {
                    DeleteGame(dir);
                }
            }

            string Base(string icon) =>
                "{\"schema\":1,\"name\":\"Test Mod\",\"version\":\"1.0.0\",\"plugins\":[\"TestMod\"]" + (icon == null ? "" : $",\"icon\":\"{icon}\"") + "}";

            ModManifest none = Load(Base(null));
            Check("no \"icon\": Icon is null", none.Icon == null);

            ModManifest plain = Load(Base("TestMod/icon.png"));
            Same("a plain relative path is kept, slashes as given", plain.Icon, "TestMod/icon.png");

            ModManifest backslashes = Load(Base("TestMod\\\\icon.jpg"));
            Same("backslashes are normalized to /", backslashes.Icon, "TestMod/icon.jpg");

            Check("\"..\" is refused (BadManifest-style, like a bad keep path)", Throws(() => Load(Base("../icon.png"))));
            Check("no extension is refused", Throws(() => Load(Base("TestMod/icon"))));
            Check("the wrong extension is refused", Throws(() => Load(Base("TestMod/icon.gif"))));
            Check("a .jpeg is accepted", !Throws(() => Load(Base("TestMod/icon.jpeg"))));
            Check("an empty \"icon\" is the same as none", string.IsNullOrEmpty(Load(Base("")).Icon));
        }

        private static bool Throws(Func<ModManifest> load)
        {
            try
            {
                load();
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }

        private static void ModIconDataUrl()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dnw-installer-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string png = Path.Combine(dir, "icon.png");
                string jpeg = Path.Combine(dir, "icon.jpg");
                string notAPicture = Path.Combine(dir, "icon-but-not.png");
                string big = Path.Combine(dir, "too-big.png");
                File.WriteAllBytes(png, PngBytes);
                File.WriteAllBytes(jpeg, JpegBytes);
                File.WriteAllText(notAPicture, "not a picture, just named like one");
                File.WriteAllBytes(big, PngBytes.Length < 600 * 1024 ? Pad(PngBytes, 600 * 1024) : PngBytes);

                Check("a real PNG: data:image/png;base64,...", ModIcon.DataUrl(png)?.StartsWith("data:image/png;base64,") == true);
                Check("a real JPEG: data:image/jpeg;base64,...", ModIcon.DataUrl(jpeg)?.StartsWith("data:image/jpeg;base64,") == true);
                Check("a .png that isn't one (checked by its bytes, not its name): null", ModIcon.DataUrl(notAPicture) == null);
                Check("over the size cap: null", ModIcon.DataUrl(big) == null);
                Check("a missing file: null", ModIcon.DataUrl(Path.Combine(dir, "nope.png")) == null);
                Check("no path at all: null", ModIcon.DataUrl(null) == null);

                // InstallerCore.ModIconPath: relative to root, inside it, the file there.
                var manifest = new ModManifest { Schema = 1, Name = "Test Mod", Version = "1.0.0", Plugins = new[] { "TestMod" }, Icon = "sub/icon.png" };
                string root = Path.Combine(dir, "root");
                Directory.CreateDirectory(Path.Combine(root, "sub"));
                File.WriteAllBytes(Path.Combine(root, "sub", "icon.png"), PngBytes);
                var core = new InstallerCore(manifest, root, _ => { });
                string resolved = core.ModIconPath(root);
                Check("ModIconPath resolves under root", resolved != null && File.Exists(resolved) && Path.GetFullPath(resolved) == Path.GetFullPath(Path.Combine(root, "sub", "icon.png")));

                var noIcon = new ModManifest { Schema = 1, Name = "Test Mod", Version = "1.0.0", Plugins = new[] { "TestMod" } };
                Check("no icon in the manifest: ModIconPath is null", new InstallerCore(noIcon, root, _ => { }).ModIconPath(root) == null);

                var missingFile = new ModManifest { Schema = 1, Name = "Test Mod", Version = "1.0.0", Plugins = new[] { "TestMod" }, Icon = "sub/not-there.png" };
                Check("named but not on disk: ModIconPath is null", new InstallerCore(missingFile, root, _ => { }).ModIconPath(root) == null);

                // Validate() (called by Load, above) already refuses "..", but ModIconPath
                // checks again at resolve time too, the same defence Unpack gives a zip
                // entry: a manifest built by hand (not through Load) can't walk out of root.
                File.WriteAllBytes(Path.Combine(dir, "outside.png"), PngBytes);
                var escaping = new ModManifest { Schema = 1, Name = "Test Mod", Version = "1.0.0", Plugins = new[] { "TestMod" }, Icon = "../outside.png" };
                Check("a path built to escape root: ModIconPath is null even so", new InstallerCore(escaping, root, _ => { }).ModIconPath(root) == null);
            }
            finally
            {
                DeleteGame(dir);
            }
        }

        private static byte[] Pad(byte[] bytes, int length)
        {
            var padded = new byte[length];
            Array.Copy(bytes, padded, bytes.Length);
            return padded;
        }
    }
}

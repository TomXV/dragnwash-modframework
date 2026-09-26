using System;
using System.IO;

namespace DragNWash.Installer
{
    // A mod's icon file (from mod-install.json's "icon", or from updates.json's "icon",
    // written by the framework), turned into the data URL the installer's and the
    // launcher's pages show a picture from. Compiled into Install.exe and linked into
    // the launcher, like Core.cs.
    internal static class ModIcon
    {
        // Nothing the pages show needs one bigger than this; a bigger file is ignored,
        // the same as a picture the pages just can't use.
        private const long MaxBytes = 512 * 1024;

        // "data:image/png;base64,..." or "data:image/jpeg;base64,..."; null when the
        // file is missing, empty, too big, or neither a PNG nor a JPEG (checked by its
        // first bytes, not its extension, since nothing here decodes the picture).
        internal static string DataUrl(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            FileInfo info;
            try
            {
                info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > MaxBytes)
                {
                    return null;
                }
            }
            catch (Exception)
            {
                return null;
            }
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception)
            {
                return null;
            }
            string mime = MimeOf(bytes);
            return mime == null ? null : $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A. JPEG: FF D8 FF.
        private static string MimeOf(byte[] bytes)
        {
            if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            {
                return "image/png";
            }
            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            {
                return "image/jpeg";
            }
            return null;
        }
    }
}

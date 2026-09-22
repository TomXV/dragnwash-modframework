using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BepInEx;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace DragNWash.ModFramework.Assets
{
    // Rasterizing a language's characters into a fallback face is FreeType's
    // SDF renderer at the atlas point size: 100-400 ms for each CJK language,
    // about a second in all on Direct3D 12, where every installed language is
    // prepared at startup. Uploading the finished atlases is cheap by
    // comparison, so what each face holds is kept between sessions in
    // BepInEx/cache/FontAtlases - its atlas pixels, its glyph and character
    // tables and the free space left on its last atlas - and a face created
    // again from the same font file with the same settings takes it all back
    // at once, then rasterizes only what is new.
    //
    // A restore uploads the atlases, as the rasterizing it replaces would, and
    // happens only where a face is created: when a mod prepares its text, at
    // startup on Direct3D 12. Saving copies pixels out of the atlases and
    // uploads nothing, so it can wait until the game is running.
    //
    // A file made for another font file or version, other atlas settings, or
    // another Unity, TextMeshPro or library version is ignored and written
    // again, and so is one that is damaged.
    internal static class FontAtlasCache
    {
        private const int FormatVersion = 1;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("DNWFONTS");
        private static readonly byte[] EndMagic = Encoding.ASCII.GetBytes("DNWFEND.");

        // Sanity limits for a file that says otherwise.
        private const int MaxAtlases = 256;
        private const int MaxEntries = 1 << 20;

        // Saving waits until no face changed for this long, so the startup's
        // many Prepare calls end in one write per face.
        private const float QuietSeconds = 2f;

        private static readonly FieldInfo SourcePathField = AccessTools.Field(typeof(TMP_FontAsset), "m_SourceFontFilePath");
        private static readonly FieldInfo AtlasIndexField = AccessTools.Field(typeof(TMP_FontAsset), "m_AtlasTextureIndex");
        private static readonly FieldInfo UsedRectsField = AccessTools.Field(typeof(TMP_FontAsset), "m_UsedGlyphRects");
        private static readonly FieldInfo FreeRectsField = AccessTools.Field(typeof(TMP_FontAsset), "m_FreeGlyphRects");
        private static readonly PropertyInfo FaceIndexProperty = AccessTools.Property(typeof(FaceInfo), "faceIndex");

        private static readonly List<TMP_FontAsset> Dirty = new List<TMP_FontAsset>();
        private static readonly object WriteLock = new object();
        private static float _lastChange;
        private static bool _saveScheduled;
        private static bool? _usable;

        internal static string Folder => Path.Combine(Paths.CachePath, "FontAtlases");

        private static bool Enabled
        {
            get
            {
                if (AssetsLibraryPlugin.CacheAtlases != null && !AssetsLibraryPlugin.CacheAtlases.Value)
                {
                    return false;
                }
                if (_usable == null)
                {
                    _usable = SourcePathField != null && AtlasIndexField != null && UsedRectsField != null && FreeRectsField != null && FaceIndexProperty != null;
                    if (!_usable.Value)
                    {
                        AssetsLibraryPlugin.Log.LogInfo("[font] This TextMeshPro has no fields the font cache knows; glyphs are rasterized at every start.");
                    }
                }
                return _usable.Value;
            }
        }

        /// <summary>
        /// Fills a face that was just created with what the cache holds for it.
        /// Returns how many characters came back; 0 when there was nothing to
        /// restore or the file did not fit.
        /// </summary>
        internal static int Restore(TMP_FontAsset face)
        {
            if (face == null || !Enabled)
            {
                return 0;
            }
            string path = null;
            try
            {
                string key = KeyOf(face);
                if (key == null)
                {
                    return 0;
                }
                path = FileOf(face);
                if (!File.Exists(path))
                {
                    return 0;
                }
                var watch = Stopwatch.StartNew();
                byte[] data = File.ReadAllBytes(path);
                Contents contents = Parse(data, key, face, out string refusal);
                if (contents == null)
                {
                    AssetsLibraryPlugin.Log.LogInfo($"[font] {face.name}: the font cache is not used ({refusal}); its characters are rasterized again.");
                    return 0;
                }
                if (!Apply(face, contents, data))
                {
                    TryDelete(path);
                    return 0;
                }
                AssetsLibraryPlugin.Log.LogInfo($"[font] {face.name}: {contents.Characters.Length} characters in {contents.AtlasCount} atlas(es) taken from the font cache ({watch.ElapsedMilliseconds} ms).");
                return contents.Characters.Length;
            }
            catch (Exception ex)
            {
                AssetsLibraryPlugin.Log.LogInfo($"[font] {face.name}: the font cache could not be read ({ex.Message}); its characters are rasterized again.");
                return 0;
            }
        }

        /// <summary>Notes that a face gained glyphs, so it is written to the cache once things are quiet.</summary>
        internal static void Changed(TMP_FontAsset face)
        {
            if (face == null || !Enabled)
            {
                return;
            }
            if (!Dirty.Contains(face))
            {
                Dirty.Add(face);
            }
            _lastChange = Time.realtimeSinceStartup;
            if (!_saveScheduled && AssetsLibraryPlugin.Instance != null)
            {
                _saveScheduled = true;
                AssetsLibraryPlugin.Instance.StartCoroutine(SaveWhenQuiet());
            }
        }

        // One face a frame: copying its atlases' pixels is main-thread work
        // (a few milliseconds a face); the file is written on a worker thread.
        private static IEnumerator SaveWhenQuiet()
        {
            yield return null;
            while (Time.realtimeSinceStartup - _lastChange < QuietSeconds)
            {
                yield return null;
            }
            _saveScheduled = false;
            var faces = new List<TMP_FontAsset>(Dirty);
            Dirty.Clear();
            foreach (TMP_FontAsset face in faces)
            {
                Snapshot snapshot = null;
                try
                {
                    snapshot = Take(face);
                }
                catch (Exception ex)
                {
                    AssetsLibraryPlugin.Log.LogInfo($"[font] {(face != null ? face.name : "A face")} could not be copied for the font cache: {ex.Message}");
                }
                if (snapshot != null)
                {
                    ThreadPool.QueueUserWorkItem(_ => Write(snapshot));
                }
                yield return null;
            }
        }

        // Everything that decides what the atlases look like. A file whose key
        // differs in anything is not used.
        private static string KeyOf(TMP_FontAsset face)
        {
            string source = SourcePathField.GetValue(face) as string;
            if (string.IsNullOrEmpty(source) || !File.Exists(source))
            {
                return null;
            }
            var file = new FileInfo(source);
            FaceInfo info = face.faceInfo;
            return string.Join("\n", new[]
            {
                "font=" + file.FullName,
                "bytes=" + file.Length,
                "written=" + file.LastWriteTimeUtc.Ticks,
                "face=" + FaceIndexProperty.GetValue(info, null),
                "family=" + info.familyName,
                "style=" + info.styleName,
                "points=" + info.pointSize,
                "padding=" + face.atlasPadding,
                "atlas=" + face.atlasWidth + "x" + face.atlasHeight,
                "mode=" + (int)face.atlasRenderMode,
                "unity=" + Application.unityVersion,
                "tmp=" + typeof(TMP_FontAsset).Assembly.ManifestModule.ModuleVersionId,
                "library=" + GameFonts.Version,
            });
        }

        private static string FileOf(TMP_FontAsset face)
        {
            var name = new StringBuilder(face.name);
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name.Replace(c, '_');
            }
            return Path.Combine(Folder, name + ".atlas");
        }

        // TextMeshPro keeps its atlases in Alpha8, or RGBA32 for colour glyphs.
        private static TextureFormat FormatOf(TMP_FontAsset face) =>
            ((int)face.atlasRenderMode & 0x10000) == 0x10000 ? TextureFormat.RGBA32 : TextureFormat.Alpha8;

        private static int BytesPerPixel(TextureFormat format) => format == TextureFormat.RGBA32 ? 4 : 1;

        private sealed class Contents
        {
            internal int AtlasCount;
            internal TextureFormat Format;
            internal Glyph[] Glyphs;
            internal uint[] Characters;
            internal uint[] CharacterGlyphs;
            internal GlyphRect[] Used;
            internal GlyphRect[] Free;
            internal int PixelsOffset;
            internal int AtlasBytes;
        }

        // Reads and checks the whole file before the face is touched.
        private static Contents Parse(byte[] data, string key, TMP_FontAsset face, out string refusal)
        {
            refusal = null;
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(data, false), Encoding.UTF8))
                {
                    if (!Same(reader.ReadBytes(Magic.Length), Magic) || reader.ReadInt32() != FormatVersion)
                    {
                        refusal = "written in another format";
                        return null;
                    }
                    if (reader.ReadString() != key)
                    {
                        refusal = "made for another font file, atlas settings or version";
                        return null;
                    }
                    int width = reader.ReadInt32();
                    int height = reader.ReadInt32();
                    var format = (TextureFormat)reader.ReadInt32();
                    int atlasCount = reader.ReadInt32();
                    if (width != face.atlasWidth || height != face.atlasHeight || format != FormatOf(face) || atlasCount < 1 || atlasCount > MaxAtlases)
                    {
                        refusal = "its atlases do not match";
                        return null;
                    }
                    var contents = new Contents { AtlasCount = atlasCount, Format = format, AtlasBytes = width * height * BytesPerPixel(format) };

                    int glyphCount = Count(reader);
                    contents.Glyphs = new Glyph[glyphCount];
                    var glyphIndexes = new HashSet<uint>();
                    for (int i = 0; i < glyphCount; i++)
                    {
                        uint index = reader.ReadUInt32();
                        var metrics = new GlyphMetrics(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        GlyphRect rect = ReadRect(reader);
                        float scale = reader.ReadSingle();
                        int atlas = reader.ReadInt32();
                        int classType = reader.ReadInt32();
                        if (atlas < 0 || atlas >= atlasCount || !Inside(rect, width, height) || !glyphIndexes.Add(index))
                        {
                            refusal = "damaged";
                            return null;
                        }
                        contents.Glyphs[i] = new Glyph(index, metrics, rect, scale, atlas) { classDefinitionType = (GlyphClassDefinitionType)classType };
                    }

                    int characterCount = Count(reader);
                    contents.Characters = new uint[characterCount];
                    contents.CharacterGlyphs = new uint[characterCount];
                    var unicodes = new HashSet<uint>();
                    for (int i = 0; i < characterCount; i++)
                    {
                        contents.Characters[i] = reader.ReadUInt32();
                        contents.CharacterGlyphs[i] = reader.ReadUInt32();
                        if (!glyphIndexes.Contains(contents.CharacterGlyphs[i]) || !unicodes.Add(contents.Characters[i]))
                        {
                            refusal = "damaged";
                            return null;
                        }
                    }

                    contents.Used = ReadRects(reader, width, height);
                    contents.Free = ReadRects(reader, width, height);
                    if (contents.Used == null || contents.Free == null || reader.ReadInt32() != contents.AtlasBytes)
                    {
                        refusal = "damaged";
                        return null;
                    }
                    contents.PixelsOffset = (int)reader.BaseStream.Position;
                    long end = (long)contents.PixelsOffset + (long)atlasCount * contents.AtlasBytes;
                    if (end + EndMagic.Length != data.Length)
                    {
                        refusal = "cut short or damaged";
                        return null;
                    }
                    reader.BaseStream.Position = end;
                    if (!Same(reader.ReadBytes(EndMagic.Length), EndMagic))
                    {
                        refusal = "damaged";
                        return null;
                    }
                    return contents;
                }
            }
            catch (EndOfStreamException)
            {
                refusal = "cut short";
                return null;
            }
        }

        private static int Count(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > MaxEntries)
            {
                throw new InvalidDataException("a count out of range");
            }
            return count;
        }

        private static GlyphRect ReadRect(BinaryReader reader) =>
            new GlyphRect(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

        private static GlyphRect[] ReadRects(BinaryReader reader, int width, int height)
        {
            var rects = new GlyphRect[Count(reader)];
            for (int i = 0; i < rects.Length; i++)
            {
                rects[i] = ReadRect(reader);
                if (!Inside(rects[i], width, height))
                {
                    return null;
                }
            }
            return rects;
        }

        private static bool Inside(GlyphRect rect, int width, int height) =>
            rect.x >= 0 && rect.y >= 0 && rect.width >= 0 && rect.height >= 0 && rect.x + rect.width <= width && rect.y + rect.height <= height;

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        // Only into a face nothing was added to yet. The face's own first
        // atlas (a 1x1 placeholder until the first glyph) is kept, since its
        // material already points at it; the others are new textures, as
        // TextMeshPro makes them when an atlas fills up.
        private static bool Apply(TMP_FontAsset face, Contents contents, byte[] data)
        {
            Texture2D[] current = face.atlasTextures;
            if (current == null || current.Length == 0 || current[0] == null || current[0].width > 1 || face.glyphTable.Count > 0 || face.characterTable.Count > 0
                || current[0].format != contents.Format)
            {
                return false;
            }
            var used = (List<GlyphRect>)UsedRectsField.GetValue(face);
            var free = (List<GlyphRect>)FreeRectsField.GetValue(face);
            var usedBefore = new List<GlyphRect>(used);
            var freeBefore = new List<GlyphRect>(free);
            Texture2D first = current[0];
            var textures = new Texture2D[contents.AtlasCount];
            GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                first.Reinitialize(face.atlasWidth, face.atlasHeight);
                textures[0] = first;
                for (int i = 1; i < textures.Length; i++)
                {
                    textures[i] = new Texture2D(face.atlasWidth, face.atlasHeight, contents.Format, false);
                }
                IntPtr pixels = pin.AddrOfPinnedObject();
                for (int i = 0; i < textures.Length; i++)
                {
                    textures[i].LoadRawTextureData(IntPtr.Add(pixels, contents.PixelsOffset + i * contents.AtlasBytes), contents.AtlasBytes);
                    textures[i].Apply(false, false);
                }

                face.atlasTextures = textures;
                AtlasIndexField.SetValue(face, contents.AtlasCount - 1);
                used.Clear();
                used.AddRange(contents.Used);
                free.Clear();
                free.AddRange(contents.Free);
                var byIndex = new Dictionary<uint, Glyph>();
                foreach (Glyph glyph in contents.Glyphs)
                {
                    face.glyphTable.Add(glyph);
                    byIndex[glyph.index] = glyph;
                }
                for (int i = 0; i < contents.Characters.Length; i++)
                {
                    face.characterTable.Add(new TMP_Character(contents.Characters[i], face, byIndex[contents.CharacterGlyphs[i]]));
                }
                face.ReadFontAssetDefinition();
                return true;
            }
            catch (Exception ex)
            {
                // Put the face back as it was created, so rasterizing works as without a cache.
                AssetsLibraryPlugin.Log.LogInfo($"[font] {face.name}: the font cache could not be applied ({ex.Message}); its characters are rasterized again.");
                try
                {
                    for (int i = 1; i < textures.Length; i++)
                    {
                        if (textures[i] != null) UnityEngine.Object.Destroy(textures[i]);
                    }
                    first.Reinitialize(1, 1);
                    face.atlasTextures = current;
                    AtlasIndexField.SetValue(face, 0);
                    used.Clear();
                    used.AddRange(usedBefore);
                    free.Clear();
                    free.AddRange(freeBefore);
                    face.glyphTable.Clear();
                    face.characterTable.Clear();
                    face.ReadFontAssetDefinition();
                }
                catch (Exception again)
                {
                    AssetsLibraryPlugin.Log.LogWarning($"[font] {face.name} could not be reset after a failed cache restore: {again.Message}");
                }
                return false;
            }
            finally
            {
                pin.Free();
            }
        }

        private sealed class Snapshot
        {
            internal string Name;
            internal string Path;
            internal string Key;
            internal int Width;
            internal int Height;
            internal TextureFormat Format;
            internal Glyph[] Glyphs;
            internal uint[] Characters;
            internal uint[] CharacterGlyphs;
            internal GlyphRect[] Used;
            internal GlyphRect[] Free;
            internal byte[][] Atlases;
        }

        // Copies on the main thread everything the worker thread writes.
        private static Snapshot Take(TMP_FontAsset face)
        {
            if (face == null || face.glyphTable.Count == 0)
            {
                return null;
            }
            string key = KeyOf(face);
            if (key == null)
            {
                return null;
            }
            TextureFormat format = FormatOf(face);
            int count = face.atlasTextureCount;
            var atlases = new byte[count][];
            for (int i = 0; i < count; i++)
            {
                Texture2D texture = face.atlasTextures[i];
                if (texture == null || !texture.isReadable || texture.width != face.atlasWidth || texture.height != face.atlasHeight || texture.format != format)
                {
                    return null;
                }
                atlases[i] = texture.GetRawTextureData();
                if (atlases[i].Length != face.atlasWidth * face.atlasHeight * BytesPerPixel(format))
                {
                    return null;
                }
            }
            var glyphs = new Glyph[face.glyphTable.Count];
            for (int i = 0; i < glyphs.Length; i++)
            {
                glyphs[i] = new Glyph(face.glyphTable[i]) { classDefinitionType = face.glyphTable[i].classDefinitionType };
            }
            List<TMP_Character> table = face.characterTable;
            var characters = new uint[table.Count];
            var characterGlyphs = new uint[table.Count];
            for (int i = 0; i < table.Count; i++)
            {
                characters[i] = table[i].unicode;
                characterGlyphs[i] = table[i].glyphIndex;
            }
            return new Snapshot
            {
                Name = face.name,
                Path = FileOf(face),
                Key = key,
                Width = face.atlasWidth,
                Height = face.atlasHeight,
                Format = format,
                Glyphs = glyphs,
                Characters = characters,
                CharacterGlyphs = characterGlyphs,
                Used = ((List<GlyphRect>)UsedRectsField.GetValue(face)).ToArray(),
                Free = ((List<GlyphRect>)FreeRectsField.GetValue(face)).ToArray(),
                Atlases = atlases,
            };
        }

        // Written next to the old file and then moved over it, so a reader
        // finds the old file, the new one or none, never half of one.
        private static void Write(Snapshot s)
        {
            lock (WriteLock)
            {
                string temp = s.Path + ".tmp";
                try
                {
                    Directory.CreateDirectory(Folder);
                    using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                    using (var writer = new BinaryWriter(stream, Encoding.UTF8))
                    {
                        writer.Write(Magic);
                        writer.Write(FormatVersion);
                        writer.Write(s.Key);
                        writer.Write(s.Width);
                        writer.Write(s.Height);
                        writer.Write((int)s.Format);
                        writer.Write(s.Atlases.Length);
                        writer.Write(s.Glyphs.Length);
                        foreach (Glyph glyph in s.Glyphs)
                        {
                            writer.Write(glyph.index);
                            GlyphMetrics m = glyph.metrics;
                            writer.Write(m.width);
                            writer.Write(m.height);
                            writer.Write(m.horizontalBearingX);
                            writer.Write(m.horizontalBearingY);
                            writer.Write(m.horizontalAdvance);
                            WriteRect(writer, glyph.glyphRect);
                            writer.Write(glyph.scale);
                            writer.Write(glyph.atlasIndex);
                            writer.Write((int)glyph.classDefinitionType);
                        }
                        writer.Write(s.Characters.Length);
                        for (int i = 0; i < s.Characters.Length; i++)
                        {
                            writer.Write(s.Characters[i]);
                            writer.Write(s.CharacterGlyphs[i]);
                        }
                        WriteRects(writer, s.Used);
                        WriteRects(writer, s.Free);
                        writer.Write(s.Width * s.Height * BytesPerPixel(s.Format));
                        foreach (byte[] atlas in s.Atlases)
                        {
                            writer.Write(atlas);
                        }
                        writer.Write(EndMagic);
                    }
                    if (File.Exists(s.Path))
                    {
                        try
                        {
                            File.Replace(temp, s.Path, null);
                        }
                        catch (Exception)
                        {
                            File.Delete(s.Path);
                            File.Move(temp, s.Path);
                        }
                    }
                    else
                    {
                        File.Move(temp, s.Path);
                    }
                    AssetsLibraryPlugin.Log.LogInfo($"[font] {s.Name}: {s.Characters.Length} characters in {s.Atlases.Length} atlas(es) saved to the font cache.");
                }
                catch (Exception ex)
                {
                    AssetsLibraryPlugin.Log.LogInfo($"[font] {s.Name} could not be saved to the font cache: {ex.Message}");
                    TryDelete(temp);
                }
            }
        }

        private static void WriteRect(BinaryWriter writer, GlyphRect rect)
        {
            writer.Write(rect.x);
            writer.Write(rect.y);
            writer.Write(rect.width);
            writer.Write(rect.height);
        }

        private static void WriteRects(BinaryWriter writer, GlyphRect[] rects)
        {
            writer.Write(rects.Length);
            foreach (GlyphRect rect in rects)
            {
                WriteRect(writer, rect);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (path != null && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}

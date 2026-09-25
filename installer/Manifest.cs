using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace DragNWash.Installer
{
    // mod-install.json: what the installer needs to know about the mod in its zip.
    // Everything the installer does that is specific to one mod comes from here.
    [DataContract]
    internal sealed class ModManifest
    {
        internal const string FileName = "mod-install.json";

        // Bumped only for a change old installers cannot read.
        [DataMember(Name = "schema")] public int Schema;

        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "version")] public string Version;
        [DataMember(Name = "website")] public string Website;

        // Folders under BepInEx/plugins that belong to the mod.
        [DataMember(Name = "plugins")] public string[] Plugins;

        // Paths under BepInEx/plugins, inside the mod's folders, that hold the
        // player's own data (working files, snapshots). Kept on uninstall unless
        // the player asks to remove everything. Updates never delete anything.
        [DataMember(Name = "keep")] public string[] Keep;

        // The mod's files in BepInEx/config, removed on uninstall.
        [DataMember(Name = "configFiles")] public string[] ConfigFiles;

        [DataMember(Name = "choices")] public ModChoice[] Choices;

        // Schema 2: the ModFramework release the mod was built against, fetched when
        // the zip does not bundle the framework. Null in schema 1.
        [DataMember(Name = "framework", EmitDefaultValue = false)] public FrameworkPin Framework;

        // A picture for the mod, shown instead of its initials on the Mods screen and in
        // the launcher: a relative path inside the payload (from the zip's top, next to
        // BepInEx\), .png/.jpg/.jpeg. Optional; null when the mod has none.
        [DataMember(Name = "icon", EmitDefaultValue = false)] public string Icon;

        // The newest schema this installer reads.
        internal const int NewestSchema = 2;

        internal static ModManifest Load(string path)
        {
            var settings = new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };
            var serializer = new DataContractJsonSerializer(typeof(ModManifest), settings);
            ModManifest manifest;
            using (var stream = File.OpenRead(path))
            {
                manifest = (ModManifest)serializer.ReadObject(stream);
            }
            manifest.Validate();
            return manifest;
        }

        internal string ToJson()
        {
            var settings = new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };
            var serializer = new DataContractJsonSerializer(typeof(ModManifest), settings);
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, this);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static readonly Regex SimpleName = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._ \-]{0,99}$");
        private static readonly Regex SimpleId = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._\-]{0,63}$");

        // The manifest decides what gets deleted, so every path is checked to stay
        // inside the mod's own folders and files.
        private void Validate()
        {
            if (Schema < 1 || Schema > NewestSchema)
            {
                throw new InvalidDataException($"{FileName}: schema {Schema} is not supported by this installer (it reads schema 1 and 2). Use the installer from the mod's release.");
            }
            if (Schema == 1 && Framework != null)
            {
                // An installer that reads schema 1 would skip the block and leave the mod without its framework.
                throw new InvalidDataException($"{FileName}: \"framework\" needs schema 2.");
            }
            if (Schema == 2)
            {
                if (Framework == null)
                {
                    throw new InvalidDataException($"{FileName}: schema 2 needs a \"framework\" block.");
                }
                Framework.Validate();
            }
            if (string.IsNullOrWhiteSpace(Name))
            {
                throw new InvalidDataException($"{FileName}: \"name\" is required.");
            }
            Plugins = (Plugins ?? new string[0]).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            if (Plugins.Length == 0)
            {
                throw new InvalidDataException($"{FileName}: \"plugins\" must name at least one folder under BepInEx/plugins.");
            }
            foreach (string plugin in Plugins)
            {
                if (!SimpleName.IsMatch(plugin) || plugin.EndsWith(".", StringComparison.Ordinal) || plugin.StartsWith(Paths.FrameworkPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"{FileName}: \"{plugin}\" is not a plugin folder name this installer accepts.");
                }
            }
            Keep = (Keep ?? new string[0]).Select(NormalizeKeep).ToArray();
            if (!string.IsNullOrEmpty(Icon))
            {
                Icon = NormalizeIcon(Icon);
            }
            ConfigFiles = (ConfigFiles ?? new string[0]).Where(f => !string.IsNullOrWhiteSpace(f)).ToArray();
            foreach (string file in ConfigFiles)
            {
                CheckConfigFile(file);
            }
            Choices = Choices ?? new ModChoice[0];
            foreach (ModChoice choice in Choices)
            {
                if (choice == null || choice.Id == null || !SimpleId.IsMatch(choice.Id))
                {
                    throw new InvalidDataException($"{FileName}: every choice needs an \"id\" of letters, digits, '.', '_' or '-'.");
                }
                if (choice.Config == null || string.IsNullOrWhiteSpace(choice.Config.Section) || string.IsNullOrWhiteSpace(choice.Config.Key))
                {
                    throw new InvalidDataException($"{FileName}: choice \"{choice.Id}\" needs config.file, config.section and config.key.");
                }
                CheckConfigFile(choice.Config.File);
                if (choice.Config.Section.IndexOfAny(new[] { '[', ']', '\r', '\n' }) >= 0 || choice.Config.Key.IndexOfAny(new[] { '=', '\r', '\n' }) >= 0)
                {
                    throw new InvalidDataException($"{FileName}: choice \"{choice.Id}\" has an invalid section or key.");
                }
                choice.Options = (choice.Options ?? new ModChoiceOption[0]).Where(o => o != null && !string.IsNullOrEmpty(o.Value)).ToArray();
                if (choice.Options.Length == 0)
                {
                    throw new InvalidDataException($"{FileName}: choice \"{choice.Id}\" has no options.");
                }
                if (choice.Options.Any(o => o.Value.IndexOfAny(new[] { '\r', '\n' }) >= 0))
                {
                    throw new InvalidDataException($"{FileName}: choice \"{choice.Id}\" has an option value with a line break.");
                }
            }
        }

        private string NormalizeKeep(string path)
        {
            string p = (path ?? "").Replace('\\', '/').Trim('/');
            string[] parts = p.Split('/');
            if (parts.Length < 2 || parts.Any(s => s.Length == 0 || s == "." || s == "..") || !Plugins.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{FileName}: keep path \"{path}\" must be inside one of the mod's plugin folders.");
            }
            return p;
        }

        private static readonly Regex IconExtension = new Regex(@"\.(png|jpe?g)$", RegexOptions.IgnoreCase);

        // Same shape of check as NormalizeKeep: a relative path, no "." or "..", every
        // segment non-empty; an icon isn't tied to a plugin folder, so unlike keep it
        // may sit anywhere under the payload.
        private static string NormalizeIcon(string path)
        {
            string p = path.Replace('\\', '/').Trim('/');
            string[] parts = p.Split('/');
            if (parts.Any(s => s.Length == 0 || s == "." || s == "..") || !IconExtension.IsMatch(p))
            {
                throw new InvalidDataException($"{FileName}: \"icon\" must be a relative path inside the payload, ending in .png, .jpg or .jpeg.");
            }
            return p;
        }

        private static void CheckConfigFile(string file)
        {
            if (file == null || !SimpleName.IsMatch(file) || file.Contains("..") ||
                !(file.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ||
                file.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase) || file.StartsWith(Paths.FrameworkConfigPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{FileName}: \"{file}\" is not a config file name this installer accepts.");
            }
        }
    }

    // The "framework" block of schema 2: one published ModFramework release, pinned by
    // its version and its zip's SHA-256 and size, and the lowest version of each part
    // the mod needs. The address is never read from here: the installer builds it
    // from the version (Paths.FrameworkUrl), so an edited manifest cannot point it
    // anywhere else.
    [DataContract]
    internal sealed class FrameworkPin
    {
        // No framework zip is near this; anything bigger is not one.
        internal const long MaxSize = 20L * 1024 * 1024;

        [DataMember(Name = "version")] public string Version;
        [DataMember(Name = "sha256")] public string Sha256;
        [DataMember(Name = "size")] public long Size;

        // Plugin folder under BepInEx/plugins, as in the release zip → the lowest
        // version of it the mod works with. The core may be left out; it is always
        // installed. Libraries not named here are not installed.
        [DataMember(Name = "needs")] public Dictionary<string, string> Needs;

        private static readonly Regex VersionText = new Regex(@"^[0-9]{1,5}(\.[0-9]{1,5}){1,3}$");
        private static readonly Regex Hash = new Regex(@"^[0-9a-f]{64}$");
        private static readonly Regex Part = new Regex(@"^DragNWash\.ModFramework(\.[A-Za-z][A-Za-z0-9]{0,39})?$");

        internal System.Version Pinned => System.Version.Parse(Version);

        internal string ZipName => $"DragNWash.ModFramework-{Version}.zip";

        // The lowest version of a part the mod needs; 0.0 for the core when it is not named.
        internal System.Version Minimum(string part)
        {
            foreach (var pair in Needs)
            {
                if (string.Equals(pair.Key, part, StringComparison.OrdinalIgnoreCase))
                {
                    return System.Version.Parse(pair.Value);
                }
            }
            return new System.Version(0, 0);
        }

        // The needed libraries, without the core.
        internal List<string> Libraries()
        {
            return Needs.Keys.Where(k => !string.Equals(k, Paths.FrameworkPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal void Validate()
        {
            string where = ModManifest.FileName + ": framework";
            if (Version == null || !VersionText.IsMatch(Version) || !System.Version.TryParse(Version, out _))
            {
                throw new InvalidDataException($"{where}.version \"{Version}\" is not a version such as 1.4.3.");
            }
            if (Sha256 == null || !Hash.IsMatch(Sha256))
            {
                throw new InvalidDataException($"{where}.sha256 must be the zip's SHA-256 as 64 lowercase hexadecimal digits.");
            }
            if (Size <= 0 || Size > MaxSize)
            {
                throw new InvalidDataException($"{where}.size must be the zip's size in bytes, above 0 and at most 20 MB.");
            }
            Needs = Needs ?? new Dictionary<string, string>();
            if (Needs.Keys.GroupBy(k => k, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            {
                throw new InvalidDataException($"{where}.needs names a folder twice.");
            }
            foreach (var pair in Needs)
            {
                if (!Part.IsMatch(pair.Key) || string.Equals(pair.Key, Paths.FrameworkPrefix + ".Preloader", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"{where}.needs: \"{pair.Key}\" is not a ModFramework plugin folder (DragNWash.ModFramework or DragNWash.ModFramework.<Library>).");
                }
                if (pair.Value == null || !VersionText.IsMatch(pair.Value) || !System.Version.TryParse(pair.Value, out _))
                {
                    throw new InvalidDataException($"{where}.needs: \"{pair.Key}\" needs a version such as 1.0.0, not \"{pair.Value}\".");
                }
            }
        }
    }

    [DataContract]
    internal sealed class ModChoice
    {
        [DataMember(Name = "id")] public string Id;

        // Label per installer language (en, ja, zh); "en" is the fallback.
        [DataMember(Name = "label")] public Dictionary<string, string> Label;

        [DataMember(Name = "config")] public ModConfigTarget Config;
        [DataMember(Name = "options")] public ModChoiceOption[] Options;

        // An option value, or "ui-language" for the option that matches the
        // system's language. A value already in the config file always wins, so
        // updating keeps what the player chose.
        [DataMember(Name = "default")] public string Default;

        internal string LabelFor(string language)
        {
            if (Label == null)
            {
                return Id;
            }
            if (Label.TryGetValue(language, out string text) && !string.IsNullOrEmpty(text))
            {
                return text;
            }
            return Label.TryGetValue("en", out text) && !string.IsNullOrEmpty(text) ? text : Id;
        }

        internal string DefaultValue(string existing)
        {
            if (existing != null && Options.Any(o => o.Value == existing))
            {
                return existing;
            }
            if (Default == "ui-language")
            {
                string match = MatchCulture(CultureInfo.CurrentUICulture);
                if (match != null)
                {
                    return match;
                }
            }
            else if (Default != null && Options.Any(o => o.Value == Default))
            {
                return Default;
            }
            return Options[0].Value;
        }

        // ja-JP → ja, zh-TW → zh-Hant, pt-BR → pt-BR, de-AT → de.
        private string MatchCulture(CultureInfo culture)
        {
            var candidates = new List<string> { culture.Name };
            if (culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                bool traditional = culture.Name.EndsWith("TW", StringComparison.OrdinalIgnoreCase) || culture.Name.EndsWith("HK", StringComparison.OrdinalIgnoreCase) ||
                                   culture.Name.EndsWith("MO", StringComparison.OrdinalIgnoreCase) || culture.Name.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0;
                candidates.Add(traditional ? "zh-Hant" : "zh-Hans");
            }
            candidates.Add(culture.TwoLetterISOLanguageName);
            foreach (string c in candidates)
            {
                ModChoiceOption option = Options.FirstOrDefault(o => string.Equals(o.Value, c, StringComparison.OrdinalIgnoreCase));
                if (option != null)
                {
                    return option.Value;
                }
            }
            return null;
        }
    }

    [DataContract]
    internal sealed class ModConfigTarget
    {
        [DataMember(Name = "file")] public string File;
        [DataMember(Name = "section")] public string Section;
        [DataMember(Name = "key")] public string Key;
    }

    [DataContract]
    internal sealed class ModChoiceOption
    {
        [DataMember(Name = "value")] public string Value;
        [DataMember(Name = "name")] public string Name;
    }
}

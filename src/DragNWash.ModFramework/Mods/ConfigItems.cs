using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace DragNWash.ModFramework.Mods
{
    // A mod's settings page on the Mods screen, built from its BepInEx config
    // entries. Any BepInEx plugin has one; the mod does not need to know about
    // the framework. Setting a value saves the config file at once (BepInEx's
    // SaveOnConfigSet, or a Save here when the mod turned that off), so there
    // is no separate Save step.
    internal sealed class ConfigItem
    {
        internal enum Kind
        {
            Toggle,
            Choice,
            Number,
            // Anything BepInEx can write to the config file as text: strings,
            // keyboard shortcuts, colours, vectors. Edited as that text.
            Text,
            ReadOnly,
        }

        internal ConfigEntryBase Entry;
        internal Kind Type;
        internal object[] Choices;
        internal bool HasRange;
        internal double Min;
        internal double Max;
        internal bool IsInteger;

        internal string Section => Entry.Definition.Section;
        internal string Key => Entry.Definition.Key;
        internal string Description => Entry.Description?.Description;

        // From a SettingMeta (or a look-alike) in the entry's tags.
        internal string DisplayName;
        internal int Order;
        internal bool Advanced;
        internal bool RequiresRestart;
        internal string Title => string.IsNullOrEmpty(DisplayName) ? Key : DisplayName;

        // From a SectionMeta in the tags of any entry of the section.
        internal string SectionDisplayName;
        internal string SectionDescription;
        internal int SectionOrder;
        internal string SectionTitle => string.IsNullOrEmpty(SectionDisplayName) ? Section : SectionDisplayName;

        internal bool IsDefault => Equals(Entry.BoxedValue, Entry.DefaultValue);

        internal bool IsShortcut => Entry.SettingType == typeof(KeyboardShortcut);

        // The value as it stands in the config file: what the text field edits.
        internal string SerializedText
        {
            get
            {
                try { return Entry.GetSerializedValue() ?? ""; }
                catch (Exception) { return Format(Entry.BoxedValue); }
            }
        }

        // Sets the value from text as the config file would give it. Returns
        // null when it was accepted, otherwise why it was not.
        internal string SetText(string text)
        {
            object value;
            try
            {
                value = TomlTypeConverter.ConvertToValue(text ?? "", Entry.SettingType);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            Set(value);
            return null;
        }

        internal void SetShortcut(KeyboardShortcut shortcut)
        {
            Set(shortcut);
        }

        internal static List<ConfigItem> For(ModCatalog.Entry mod)
        {
            var items = new List<ConfigItem>();
            if (mod?.Guid == null || !mod.Loaded || !Chainloader.PluginInfos.TryGetValue(mod.Guid, out PluginInfo plugin))
            {
                return items;
            }
            ConfigFile config = plugin.Instance?.Config;
            if (config == null)
            {
                return items;
            }
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> pair in config)
            {
                ConfigEntryBase entry = pair.Value;
                if (entry == null || Hidden(entry))
                {
                    continue;
                }
                items.Add(Describe(entry));
            }
            // A SectionMeta on one entry speaks for its whole section.
            foreach (IGrouping<string, ConfigItem> section in items.GroupBy(i => i.Section))
            {
                ConfigItem described = section.FirstOrDefault(i => i.SectionDisplayName != null || i.SectionDescription != null || i.SectionOrder != 0);
                if (described == null)
                {
                    continue;
                }
                foreach (ConfigItem item in section)
                {
                    item.SectionDisplayName = described.SectionDisplayName;
                    item.SectionDescription = described.SectionDescription;
                    item.SectionOrder = described.SectionOrder;
                }
            }
            return items
                .OrderBy(i => i.SectionOrder)
                .ThenBy(i => i.Section, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Order)
                .ThenBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // The name a setting goes by on its settings page, for naming it elsewhere.
        internal static string TitleOf(ConfigEntryBase entry)
        {
            var item = new ConfigItem { Entry = entry };
            ReadMeta(item, entry);
            return item.Title;
        }

        // Reads SettingMeta and SectionMeta, and any other tag that carries the
        // same member names (ConfigurationManager's attribute uses IsAdvanced,
        // DispName and Order), so a mod need not reference the framework.
        private static void ReadMeta(ConfigItem item, ConfigEntryBase entry)
        {
            object[] tags = entry.Description?.Tags;
            if (tags == null)
            {
                return;
            }
            foreach (object tag in tags)
            {
                if (tag == null)
                {
                    continue;
                }
                if (tag is SettingMeta meta)
                {
                    item.DisplayName = meta.DisplayName;
                    item.Order = meta.Order;
                    item.Advanced = meta.Advanced;
                    item.RequiresRestart = meta.RequiresRestart;
                    continue;
                }
                if (tag is SectionMeta section)
                {
                    item.SectionDisplayName = section.DisplayName;
                    item.SectionDescription = section.Description;
                    item.SectionOrder = section.Order;
                    continue;
                }
                if (tag is string)
                {
                    continue;
                }
                string name = Member(tag, "DisplayName") as string ?? Member(tag, "DispName") as string;
                if (!string.IsNullOrEmpty(name))
                {
                    item.DisplayName = name;
                }
                if (Member(tag, "Order") is int order)
                {
                    item.Order = order;
                }
                object advancedTag = Member(tag, "Advanced") ?? Member(tag, "IsAdvanced");
                if (advancedTag is bool advanced)
                {
                    item.Advanced = advanced;
                }
                if (Member(tag, "RequiresRestart") is bool restart)
                {
                    item.RequiresRestart = restart;
                }
            }
        }

        private static object Member(object tag, string name)
        {
            try
            {
                Type type = tag.GetType();
                return type.GetField(name)?.GetValue(tag) ?? type.GetProperty(name)?.GetValue(tag, null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Plugins that support BepInEx.ConfigurationManager can hide an entry with
        // a tag object carrying Browsable = false; honour it the same way.
        private static bool Hidden(ConfigEntryBase entry)
        {
            object[] tags = entry.Description?.Tags;
            if (tags == null)
            {
                return false;
            }
            foreach (object tag in tags)
            {
                if (tag == null)
                {
                    continue;
                }
                object browsable = tag.GetType().GetField("Browsable")?.GetValue(tag) ?? tag.GetType().GetProperty("Browsable")?.GetValue(tag, null);
                if (browsable is bool b && !b)
                {
                    return true;
                }
            }
            return false;
        }

        private static ConfigItem Describe(ConfigEntryBase entry)
        {
            var item = new ConfigItem { Entry = entry, Type = Kind.ReadOnly };
            ReadMeta(item, entry);
            Type type = entry.SettingType;
            AcceptableValueBase acceptable = entry.Description?.AcceptableValues;

            if (type == typeof(bool))
            {
                item.Type = Kind.Toggle;
            }
            else if (acceptable != null && acceptable.GetType().IsGenericType &&
                     acceptable.GetType().GetGenericTypeDefinition() == typeof(AcceptableValueList<>))
            {
                var values = acceptable.GetType().GetProperty("AcceptableValues")?.GetValue(acceptable, null) as Array;
                if (values != null && values.Length > 0)
                {
                    item.Type = Kind.Choice;
                    item.Choices = values.Cast<object>().ToArray();
                }
            }
            else if (type.IsEnum)
            {
                item.Type = Kind.Choice;
                item.Choices = Enum.GetValues(type).Cast<object>().ToArray();
            }
            else if (IsNumber(type))
            {
                item.Type = Kind.Number;
                item.IsInteger = type != typeof(float) && type != typeof(double) && type != typeof(decimal);
                if (acceptable != null && acceptable.GetType().IsGenericType &&
                    acceptable.GetType().GetGenericTypeDefinition() == typeof(AcceptableValueRange<>))
                {
                    item.HasRange = true;
                    item.Min = Convert.ToDouble(acceptable.GetType().GetProperty("MinValue").GetValue(acceptable, null), CultureInfo.InvariantCulture);
                    item.Max = Convert.ToDouble(acceptable.GetType().GetProperty("MaxValue").GetValue(acceptable, null), CultureInfo.InvariantCulture);
                }
            }
            else if (TomlTypeConverter.CanConvert(type))
            {
                item.Type = Kind.Text;
            }
            return item;
        }

        private static bool IsNumber(Type t)
        {
            return t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long) ||
                   t == typeof(short) || t == typeof(byte) || t == typeof(uint) || t == typeof(ulong) ||
                   t == typeof(ushort) || t == typeof(sbyte) || t == typeof(decimal);
        }

        internal string ValueText => Type == Kind.Text ? SerializedText : Format(Entry.BoxedValue);

        internal string DefaultText => Type == Kind.Text ? SerializeDefault() : Format(Entry.DefaultValue);

        private string SerializeDefault()
        {
            try
            {
                return Entry.DefaultValue == null ? "" : TomlTypeConverter.ConvertToString(Entry.DefaultValue, Entry.SettingType);
            }
            catch (Exception)
            {
                return Format(Entry.DefaultValue);
            }
        }

        // Toggle values use the same "On"/"Off" words as the rest of the screen,
        // so translation packs cover them.
        internal string Format(object value)
        {
            if (value == null)
            {
                return "";
            }
            if (value is bool b)
            {
                return b ? ModsMenu.TextOn : ModsMenu.TextOff;
            }
            if (value is float f)
            {
                return f.ToString("0.###", CultureInfo.InvariantCulture);
            }
            if (value is double d)
            {
                return d.ToString("0.###", CultureInfo.InvariantCulture);
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        // One step left (-1) or right (+1).
        internal void Step(int direction)
        {
            switch (Type)
            {
                case Kind.Toggle:
                    Set(!(bool)Entry.BoxedValue);
                    break;
                case Kind.Choice:
                {
                    int index = Array.FindIndex(Choices, c => Equals(c, Entry.BoxedValue));
                    int next = ((index < 0 ? 0 : index + direction) % Choices.Length + Choices.Length) % Choices.Length;
                    Set(Choices[next]);
                    break;
                }
                case Kind.Number:
                {
                    double current = Convert.ToDouble(Entry.BoxedValue, CultureInfo.InvariantCulture);
                    double step;
                    if (HasRange)
                    {
                        step = (Max - Min) / 20.0;
                        if (IsInteger)
                        {
                            step = Math.Max(1.0, Math.Round(step));
                        }
                    }
                    else
                    {
                        step = IsInteger ? 1.0 : 0.1;
                    }
                    double next = current + direction * step;
                    if (HasRange)
                    {
                        next = Math.Max(Min, Math.Min(Max, next));
                    }
                    if (IsInteger)
                    {
                        next = Math.Round(next);
                    }
                    else
                    {
                        next = Math.Round(next, 6);
                    }
                    Set(Convert.ChangeType(next, Entry.SettingType, CultureInfo.InvariantCulture));
                    break;
                }
            }
        }

        internal void ResetToDefault()
        {
            if (Type != Kind.ReadOnly)
            {
                Set(Entry.DefaultValue);
            }
        }

        private void Set(object value)
        {
            try
            {
                Entry.BoxedValue = value;
                // The page says a change is saved; a mod that turned off
                // BepInEx's saving on every change still gets it written.
                ConfigFile file = Entry.ConfigFile;
                if (file != null && !file.SaveOnConfigSet)
                {
                    file.Save();
                }
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogWarning($"Could not change {Section}.{Key}: {ex.Message}");
            }
        }
    }
}

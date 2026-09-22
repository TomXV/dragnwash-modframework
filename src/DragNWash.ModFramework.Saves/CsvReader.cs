using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DragNWash.ModFramework.Saves
{
    /// <summary>
    /// A minimal RFC4180-ish CSV reader: quoted fields, escaped quotes (<c>""</c>) and
    /// embedded commas/newlines inside quotes, with no dependency on the game's bundled
    /// CsvHelper. From the localization mod.
    /// </summary>
    public static class CsvReader
    {
        /// <summary>
        /// Reads a CSV file's data rows, keyed by the header row's column names
        /// (case-insensitive). A shared read: the file may be open elsewhere
        /// (Excel, an editor) while the game runs.
        /// </summary>
        public static IEnumerable<Dictionary<string, string>> ReadRows(string path)
        {
            // Shared read: translators keep these files open in Excel or an
            // editor while the game runs, and an exclusive open would throw.
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(fs, Encoding.UTF8, true))
            {
                text = reader.ReadToEnd();
            }
            List<List<string>> records = Parse(text);
            if (records.Count == 0)
            {
                yield break;
            }

            List<string> header = records[0];
            for (int i = 1; i < records.Count; i++)
            {
                List<string> row = records[i];
                var dict = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < header.Count; c++)
                {
                    dict[header[c]] = c < row.Count ? row[c] : string.Empty;
                }
                yield return dict;
            }
        }

        private static List<List<string>> Parse(string text)
        {
            var records = new List<List<string>>();
            var fields = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            int i = 0;
            int len = text.Length;

            while (i < len)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < len && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i += 2;
                            continue;
                        }
                        inQuotes = false;
                        i++;
                        continue;
                    }
                    field.Append(c);
                    i++;
                    continue;
                }

                // A '#' at the very start of a record is a comment line
                // (section headers in the published files); skip to EOL.
                if (c == '#' && fields.Count == 0 && field.Length == 0)
                {
                    while (i < len && text[i] != '\n') i++;
                    i++;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        i++;
                        break;
                    case ',':
                        fields.Add(field.ToString());
                        field.Clear();
                        i++;
                        break;
                    case '\r':
                        i++;
                        break;
                    case '\n':
                        // A blank line is not a record (the published files use
                        // them to space out section headers).
                        if (fields.Count == 0 && field.Length == 0)
                        {
                            i++;
                            break;
                        }
                        fields.Add(field.ToString());
                        field.Clear();
                        records.Add(fields);
                        fields = new List<string>();
                        i++;
                        break;
                    default:
                        field.Append(c);
                        i++;
                        break;
                }
            }

            if (field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                records.Add(fields);
            }

            return records;
        }

        // A leading '#' is quoted too: Parse() treats a '#' at the start of a
        // record as a comment, so an unquoted one would make the whole row
        // vanish on read-back. Quoting is enough: a record that opens with '"'
        // enters the quoted branch, where '#' is just another character.
        /// <summary>
        /// Escapes a value for a CSV field: quotes it when it contains a comma,
        /// a quote, a newline, or starts with <c>#</c> (which <see cref="ReadRows"/>
        /// would otherwise read back as a comment line).
        /// </summary>
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            bool needsQuotes = value[0] == '#' || value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!needsQuotes)
            {
                return value;
            }
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}

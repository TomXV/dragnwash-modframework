using System;
using System.Globalization;
using System.Text;

namespace DragNWash.Installer
{
    // Just enough JSON for the window's page: strings, numbers, booleans, lists and nested
    // objects. The same writer as the launcher's (launcher/Session.cs).
    internal static class Json
    {
        internal sealed class RawJson
        {
            internal readonly string Text;

            internal RawJson(string text)
            {
                Text = text;
            }
        }

        internal static RawJson Raw(string json) => new RawJson(json);

        // Name, value, name, value...
        internal static string Object(params object[] pairs)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                Write(sb, (string)pairs[i]);
                sb.Append(':');
                Write(sb, pairs[i + 1]);
            }
            return sb.Append('}').ToString();
        }

        // An object inside another one.
        internal static RawJson Obj(params object[] pairs) => Raw(Object(pairs));

        private static void Write(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case RawJson raw:
                    sb.Append(raw.Text);
                    break;
                case string s:
                    sb.Append('"');
                    foreach (char c in s)
                    {
                        // Also escaped: < (so no "</script>" can close anything) and the two JavaScript line breaks.
                        if (c == '"' || c == '\\')
                        {
                            sb.Append('\\').Append(c);
                        }
                        else if (c < ' ' || c == '<' || c == '>' || c == '&' || c == (char)0x2028 || c == (char)0x2029)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                    }
                    sb.Append('"');
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int n:
                    sb.Append(n.ToString(CultureInfo.InvariantCulture));
                    break;
                case long n:
                    sb.Append(n.ToString(CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("0.###", CultureInfo.InvariantCulture));
                    break;
                case System.Collections.IEnumerable list:
                    sb.Append('[');
                    bool first = true;
                    foreach (object item in list)
                    {
                        if (!first)
                        {
                            sb.Append(',');
                        }
                        first = false;
                        Write(sb, item);
                    }
                    sb.Append(']');
                    break;
                default:
                    Write(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
            }
        }
    }
}

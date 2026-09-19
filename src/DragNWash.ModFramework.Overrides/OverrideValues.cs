using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DragNWash.ModFramework.Overrides
{
    /// <summary>
    /// Values in override files, as text: written so they read back exactly
    /// (floats in their shortest exact form, a rotation as x, y, z, w), and read
    /// leniently, so a value typed by hand works too (a rotation as three Euler
    /// angles, a colour as #RRGGBB or #RRGGBBAA, true/false/on/off/1/0).
    /// </summary>
    public static class OverrideValues
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>True for the types an override can hold.</summary>
        public static bool Supports(Type t)
        {
            return t == typeof(string) || t == typeof(bool) || t.IsEnum || IsNumber(t)
                || t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4) || t == typeof(Quaternion)
                || t == typeof(Vector2Int) || t == typeof(Vector3Int) || t == typeof(Color) || t == typeof(Color32)
                || t == typeof(Rect) || t == typeof(Bounds);
        }

        private static bool IsNumber(Type t)
        {
            return t == typeof(int) || t == typeof(float) || t == typeof(double) || t == typeof(long) || t == typeof(short)
                || t == typeof(byte) || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte) || t == typeof(decimal);
        }

        private static string F(float f) => f.ToString("R", Invariant);

        private static string Join(params float[] values)
        {
            var parts = new string[values.Length];
            for (int i = 0; i < values.Length; i++) parts[i] = F(values[i]);
            return string.Join(", ", parts);
        }

        /// <summary>The value as an override file keeps it; reads back exactly with <see cref="Deserialize"/>.</summary>
        public static string Serialize(object v)
        {
            switch (v)
            {
                case null: return "";
                case string s: return s;
                case bool b: return b ? "true" : "false";
                case float f: return F(f);
                case double d: return d.ToString("R", Invariant);
                case Vector2 a: return Join(a.x, a.y);
                case Vector3 a: return Join(a.x, a.y, a.z);
                case Vector4 a: return Join(a.x, a.y, a.z, a.w);
                case Quaternion q: return Join(q.x, q.y, q.z, q.w);
                case Vector2Int a: return $"{a.x}, {a.y}";
                case Vector3Int a: return $"{a.x}, {a.y}, {a.z}";
                case Color c: return Join(c.r, c.g, c.b, c.a);
                case Color32 c: return $"{c.r}, {c.g}, {c.b}, {c.a}";
                case Rect r: return Join(r.x, r.y, r.width, r.height);
                case Bounds b: return Join(b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z);
                case Enum e: return e.ToString();
                case IFormattable f2: return f2.ToString(null, Invariant);
            }
            return v.ToString();
        }

        /// <summary>
        /// The text as a value of type <paramref name="t"/>, or null with the
        /// reason in <paramref name="error"/>.
        /// </summary>
        public static object Deserialize(Type t, string text, out string error)
        {
            error = null;
            text = (text ?? "").Trim();
            try
            {
                if (t == typeof(string)) return text;
                if (t == typeof(bool))
                {
                    switch (text.ToLowerInvariant())
                    {
                        case "true": case "1": case "on": case "yes": return true;
                        case "false": case "0": case "off": case "no": return false;
                    }
                    error = "true or false";
                    return null;
                }
                if (t.IsEnum)
                {
                    try { return Enum.Parse(t, text, true); }
                    catch { error = "one of " + string.Join(", ", Enum.GetNames(t)); return null; }
                }
                if (IsNumber(t)) return Convert.ChangeType(text, t, Invariant);
                if ((t == typeof(Color) || t == typeof(Color32)) && text.StartsWith("#", StringComparison.Ordinal))
                {
                    if (!ColorUtility.TryParseHtmlString(text, out Color html)) { error = "#RRGGBB or #RRGGBBAA"; return null; }
                    return t == typeof(Color) ? (object)html : (Color32)html;
                }
                float[] n = Numbers(text);
                if (n == null) { error = "numbers separated by commas"; return null; }
                if (t == typeof(Vector2)) return Need(n, 2, ref error) ? new Vector2(n[0], n[1]) : (object)null;
                if (t == typeof(Vector3)) return Need(n, 3, ref error) ? new Vector3(n[0], n[1], n[2]) : (object)null;
                if (t == typeof(Vector4)) return Need(n, 4, ref error) ? new Vector4(n[0], n[1], n[2], n[3]) : (object)null;
                if (t == typeof(Vector2Int)) return Need(n, 2, ref error) ? new Vector2Int((int)n[0], (int)n[1]) : (object)null;
                if (t == typeof(Vector3Int)) return Need(n, 3, ref error) ? new Vector3Int((int)n[0], (int)n[1], (int)n[2]) : (object)null;
                if (t == typeof(Quaternion))
                {
                    // Three numbers are Euler angles, as a person types them.
                    if (n.Length == 3) return Quaternion.Euler(n[0], n[1], n[2]);
                    return Need(n, 4, ref error) ? new Quaternion(n[0], n[1], n[2], n[3]) : (object)null;
                }
                if (t == typeof(Color))
                {
                    if (n.Length == 3) return new Color(n[0], n[1], n[2], 1f);
                    return Need(n, 4, ref error) ? new Color(n[0], n[1], n[2], n[3]) : (object)null;
                }
                if (t == typeof(Color32))
                {
                    if (n.Length == 3) return new Color32((byte)n[0], (byte)n[1], (byte)n[2], 255);
                    return Need(n, 4, ref error) ? new Color32((byte)n[0], (byte)n[1], (byte)n[2], (byte)n[3]) : (object)null;
                }
                if (t == typeof(Rect)) return Need(n, 4, ref error) ? new Rect(n[0], n[1], n[2], n[3]) : (object)null;
                if (t == typeof(Bounds)) return Need(n, 6, ref error) ? new Bounds(new Vector3(n[0], n[1], n[2]), new Vector3(n[3], n[4], n[5])) : (object)null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
            error = "a " + t.Name + " cannot be overridden";
            return null;
        }

        private static bool Need(float[] n, int count, ref string error)
        {
            if (n.Length == count) return true;
            error = $"{count} numbers separated by commas";
            return false;
        }

        private static float[] Numbers(string text)
        {
            var list = new List<float>();
            foreach (string part in text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!float.TryParse(part, NumberStyles.Float, Invariant, out float f)) return null;
                list.Add(f);
            }
            return list.ToArray();
        }
    }
}

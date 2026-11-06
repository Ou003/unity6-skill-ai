using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Unity6SkillAI.Editor
{
    /// <summary>
    /// Dependency-free JSON reader/writer. Unity's JsonUtility only maps onto
    /// [Serializable] classes and cannot parse the dynamic shape of an LLM
    /// response (tool_calls, content blocks, arbitrary tool arguments), so the
    /// package carries its own tiny parser instead of shipping Newtonsoft.Json.
    /// </summary>
    internal static class MiniJson
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            int index = 0;
            return ParseValue(json, ref index);
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length)
                return null;
            char c = s[i];
            if (c == '{')
                return ParseObject(s, ref i);
            if (c == '[')
                return ParseArray(s, ref i);
            if (c == '"')
                return ParseString(s, ref i);
            if (c == 't' && Literal(s, ref i, "true"))
                return true;
            if (c == 'f' && Literal(s, ref i, "false"))
                return false;
            if (c == 'n' && Literal(s, ref i, "null"))
                return null;
            if (c == '-' || (c >= '0' && c <= '9'))
                return ParseNumber(s, ref i);
            throw new FormatException("MiniJson: unexpected '" + c + "' at position " + i);
        }

        private static bool Literal(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length)
                return false;
            for (int k = 0; k < word.Length; k++)
            {
                if (s[i + k] != word[k])
                    return false;
            }
            i += word.Length;
            return true;
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // {
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}')
            {
                i++;
                return result;
            }
            while (i < s.Length)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"')
                    throw new FormatException("MiniJson: expected object key at position " + i);
                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    throw new FormatException("MiniJson: expected ':' at position " + i);
                i++;
                result[key] = ParseValue(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length)
                    break;
                if (s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (s[i] == '}')
                {
                    i++;
                    break;
                }
                throw new FormatException("MiniJson: expected ',' or '}' at position " + i);
            }
            return result;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var result = new List<object>();
            i++; // [
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']')
            {
                i++;
                return result;
            }
            while (i < s.Length)
            {
                result.Add(ParseValue(s, ref i));
                SkipWhitespace(s, ref i);
                if (i >= s.Length)
                    break;
                if (s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (s[i] == ']')
                {
                    i++;
                    break;
                }
                throw new FormatException("MiniJson: expected ',' or ']' at position " + i);
            }
            return result;
        }

        private static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                    return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (i >= s.Length)
                    break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 <= s.Length)
                        {
                            int code;
                            if (int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                    i++;
                else
                    break;
            }
            double value;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return 0d;
            return value;
        }

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }
            var text = value as string;
            if (text != null)
            {
                WriteString(sb, text);
                return;
            }
            if (value is bool)
            {
                sb.Append((bool)value ? "true" : "false");
                return;
            }
            if (value is double || value is float || value is int || value is long || value is short || value is byte || value is decimal)
            {
                double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d))
                    d = 0d;
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var dict = value as IDictionary<string, object>;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (var pair in dict)
                {
                    if (!first)
                        sb.Append(',');
                    first = false;
                    WriteString(sb, pair.Key);
                    sb.Append(':');
                    WriteValue(sb, pair.Value);
                }
                sb.Append('}');
                return;
            }
            var list = value as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first)
                        sb.Append(',');
                    first = false;
                    WriteValue(sb, item);
                }
                sb.Append(']');
                return;
            }
            WriteString(sb, value.ToString());
        }

        private static void WriteString(StringBuilder sb, string text)
        {
            sb.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---- small typed accessors used throughout the package ----

        public static Dictionary<string, object> Obj(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static List<object> Arr(object value)
        {
            return value as List<object>;
        }

        public static string Str(object value, string fallback = "")
        {
            if (value == null)
                return fallback;
            if (value is string)
                return (string)value;
            if (value is bool)
                return (bool)value ? "true" : "false";
            if (value is double || value is float || value is int || value is long)
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            if (value is Dictionary<string, object> || value is List<object>)
                return Serialize(value);
            return value.ToString();
        }

        public static string Get(Dictionary<string, object> dict, string key, string fallback = "")
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value))
                return fallback;
            return Str(value, fallback);
        }

        public static double GetNum(Dictionary<string, object> dict, string key, double fallback)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value) || value == null)
                return fallback;
            double parsed;
            if (value is double)
                return (double)value;
            if (double.TryParse(Str(value), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            return fallback;
        }

        public static bool GetBool(Dictionary<string, object> dict, string key, bool fallback)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value) || value == null)
                return fallback;
            if (value is bool)
                return (bool)value;
            bool parsed;
            if (bool.TryParse(Str(value), out parsed))
                return parsed;
            return fallback;
        }

        public static List<string> GetStrings(Dictionary<string, object> dict, string key)
        {
            var result = new List<string>();
            object value;
            if (dict == null || !dict.TryGetValue(key, out value))
                return result;
            var list = value as List<object>;
            if (list != null)
            {
                foreach (var item in list)
                    result.Add(Str(item));
                return result;
            }
            var single = value as string;
            if (single != null)
                result.Add(single);
            return result;
        }

        public static Dictionary<string, object> Dict(params object[] pairs)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                result[Str(pairs[i])] = pairs[i + 1];
            return result;
        }

        public static List<object> List(params object[] items)
        {
            return new List<object>(items);
        }
    }
}

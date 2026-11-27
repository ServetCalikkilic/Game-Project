using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Rts.Core
{
    /// <summary>
    /// Small strict JSON parser (objects -> Dictionary, arrays -> List, numbers -> double, strings, bool, null).
    /// Used for ActionPlan parsing so that unknown fields, wrong types and absent-vs-zero can be detected,
    /// which UnityEngine.JsonUtility cannot do. Rejects duplicate keys, trailing text and deep nesting.
    /// </summary>
    public static class MiniJson
    {
        public const int MaxLength = 64 * 1024;
        const int MaxDepth = 16;

        public sealed class JsonException : Exception { public JsonException(string m) : base(m) { } }

        public static object Parse(string json)
        {
            if (json == null) throw new JsonException("null input");
            if (json.Length > MaxLength) throw new JsonException("input too large");
            int i = 0;
            var v = ParseValue(json, ref i, 0);
            SkipWs(json, ref i);
            if (i != json.Length) throw new JsonException("unexpected trailing characters");
            return v;
        }

        static void SkipWs(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object ParseValue(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new JsonException("nesting too deep");
            SkipWs(s, ref i);
            if (i >= s.Length) throw new JsonException("unexpected end");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i, depth);
            if (c == '[') return ParseArray(s, ref i, depth);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't') return Literal(s, ref i, "true", true);
            if (c == 'f') return Literal(s, ref i, "false", false);
            if (c == 'n') return Literal(s, ref i, "null", null);
            return ParseNumber(s, ref i);
        }

        static object Literal(string s, ref int i, string word, object value)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new JsonException("invalid literal");
            i += word.Length;
            return value;
        }

        static Dictionary<string, object> ParseObject(string s, ref int i, int depth)
        {
            var d = new Dictionary<string, object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new JsonException("expected string key");
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new JsonException("expected ':'");
                i++;
                if (d.ContainsKey(key)) throw new JsonException("duplicate key '" + key + "'");
                d[key] = ParseValue(s, ref i, depth + 1);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new JsonException("unterminated object");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new JsonException("expected ',' or '}'");
            }
        }

        static List<object> ParseArray(string s, ref int i, int depth)
        {
            var l = new List<object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(ParseValue(s, ref i, depth + 1));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new JsonException("unterminated array");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return l; }
                throw new JsonException("expected ',' or ']'");
            }
        }

        static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c < 0x20) throw new JsonException("control character in string");
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
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
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp))
                            throw new JsonException("invalid unicode escape");
                        sb.Append((char)cp); i += 4; break;
                    default: throw new JsonException("invalid escape");
                }
            }
            throw new JsonException("unterminated string");
        }

        static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            if (i == start || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                || double.IsNaN(d) || double.IsInfinity(d))
                throw new JsonException("invalid number");
            return d;
        }
    }
}

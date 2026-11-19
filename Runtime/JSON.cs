// JSON.cs — lightweight, dependency-free JSON for the CMS text system.
//
// Contains:
//   * Json      : a compact, correct JSON parser/serialiser (MiniJSON, public domain).
//   * JSON      : a read-only navigation wrapper giving the chaining API the CMS uses:
//                     root.getJSON("Attract").getJSON("Title").getString("en")
//                 Missing keys return an empty node / "" instead of throwing, so a
//                 missing translation never breaks the app.
//
// MiniJSON is based on Calvin Rien's public-domain implementation.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SpotCMS
{
    /// <summary>Read-only navigation wrapper over a parsed JSON tree.</summary>
    public class JSON
    {
        private readonly object _value;

        public JSON(object value) { _value = value; }

        public static JSON Parse(string text) => new JSON(Json.Deserialize(text));

        private Dictionary<string, object> AsDict => _value as Dictionary<string, object>;

        /// <summary>True when a child object/value exists under key.</summary>
        public bool Has(string key)
        {
            var d = AsDict;
            return d != null && d.ContainsKey(key);
        }

        /// <summary>Navigate into a child object. Never null — returns an empty node if absent.</summary>
        public JSON getJSON(string key)
        {
            var d = AsDict;
            if (d != null && d.TryGetValue(key, out var child))
                return new JSON(child);
            return new JSON(null);
        }

        /// <summary>Read a string value by key. Returns "" if absent.</summary>
        public string getString(string key)
        {
            var d = AsDict;
            if (d != null && d.TryGetValue(key, out var v) && v != null)
                return Convert.ToString(v, CultureInfo.InvariantCulture);
            return "";
        }

        /// <summary>This node as a string (when the node is itself a string value).</summary>
        public string asString() =>
            _value == null ? "" : Convert.ToString(_value, CultureInfo.InvariantCulture);

        public float getFloat(string key, float fallback = 0f)
        {
            var d = AsDict;
            if (d != null && d.TryGetValue(key, out var v) && v != null &&
                float.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                return f;
            return fallback;
        }

        public int getInt(string key, int fallback = 0)
        {
            var d = AsDict;
            if (d != null && d.TryGetValue(key, out var v) && v != null &&
                int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                return i;
            return fallback;
        }

        /// <summary>Keys of this node (empty if not an object).</summary>
        public IEnumerable<string> Keys => AsDict != null ? AsDict.Keys : Array.Empty<string>();
    }

    // ------------------------------------------------------------------ MiniJSON
    public static class Json
    {
        public static object Deserialize(string json)
        {
            if (json == null) return null;
            return Parser.Parse(json);
        }

        sealed class Parser : IDisposable
        {
            const string WordBreak = "{}[],:\"";
            System.IO.StringReader json;

            Parser(string s) { json = new System.IO.StringReader(s); }

            public static object Parse(string s)
            {
                using (var p = new Parser(s)) return p.ParseValue();
            }

            public void Dispose() { json.Dispose(); json = null; }

            Dictionary<string, object> ParseObject()
            {
                var table = new Dictionary<string, object>();
                json.Read(); // {
                while (true)
                {
                    switch (NextToken)
                    {
                        case TOKEN.NONE: return null;
                        case TOKEN.COMMA: continue;
                        case TOKEN.CURLY_CLOSE: return table;
                        default:
                            string name = ParseString();
                            if (name == null) return null;
                            if (NextToken != TOKEN.COLON) return null;
                            json.Read(); // :
                            table[name] = ParseValue();
                            break;
                    }
                }
            }

            List<object> ParseArray()
            {
                var array = new List<object>();
                json.Read(); // [
                bool parsing = true;
                while (parsing)
                {
                    TOKEN nextToken = NextToken;
                    switch (nextToken)
                    {
                        case TOKEN.NONE: return null;
                        case TOKEN.COMMA: continue;
                        case TOKEN.SQUARED_CLOSE: parsing = false; break;
                        default:
                            array.Add(ParseByToken(nextToken));
                            break;
                    }
                }
                return array;
            }

            object ParseValue() => ParseByToken(NextToken);

            object ParseByToken(TOKEN token)
            {
                switch (token)
                {
                    case TOKEN.STRING: return ParseString();
                    case TOKEN.NUMBER: return ParseNumber();
                    case TOKEN.CURLY_OPEN: return ParseObject();
                    case TOKEN.SQUARED_OPEN: return ParseArray();
                    case TOKEN.TRUE: return true;
                    case TOKEN.FALSE: return false;
                    case TOKEN.NULL: return null;
                    default: return null;
                }
            }

            string ParseString()
            {
                var s = new StringBuilder();
                json.Read(); // "
                bool parsing = true;
                while (parsing)
                {
                    if (json.Peek() == -1) break;
                    char c = NextChar;
                    switch (c)
                    {
                        case '"': parsing = false; break;
                        case '\\':
                            if (json.Peek() == -1) { parsing = false; break; }
                            c = NextChar;
                            switch (c)
                            {
                                case '"': s.Append('"'); break;
                                case '\\': s.Append('\\'); break;
                                case '/': s.Append('/'); break;
                                case 'b': s.Append('\b'); break;
                                case 'f': s.Append('\f'); break;
                                case 'n': s.Append('\n'); break;
                                case 'r': s.Append('\r'); break;
                                case 't': s.Append('\t'); break;
                                case 'u':
                                    var hex = new char[4];
                                    for (int i = 0; i < 4; i++) hex[i] = NextChar;
                                    s.Append((char)Convert.ToInt32(new string(hex), 16));
                                    break;
                            }
                            break;
                        default: s.Append(c); break;
                    }
                }
                return s.ToString();
            }

            object ParseNumber()
            {
                string number = NextWord;
                if (number.IndexOf('.') == -1 && number.IndexOf('e') == -1 && number.IndexOf('E') == -1)
                {
                    long.TryParse(number, NumberStyles.Any, CultureInfo.InvariantCulture, out long l);
                    return l;
                }
                double.TryParse(number, NumberStyles.Any, CultureInfo.InvariantCulture, out double d);
                return d;
            }

            void EatWhitespace()
            {
                while (char.IsWhiteSpace(PeekChar))
                {
                    json.Read();
                    if (json.Peek() == -1) break;
                }
            }

            char PeekChar => Convert.ToChar(json.Peek());
            char NextChar => Convert.ToChar(json.Read());

            string NextWord
            {
                get
                {
                    var word = new StringBuilder();
                    while (!IsWordBreak(PeekChar))
                    {
                        word.Append(NextChar);
                        if (json.Peek() == -1) break;
                    }
                    return word.ToString();
                }
            }

            static bool IsWordBreak(char c) => char.IsWhiteSpace(c) || WordBreak.IndexOf(c) != -1;

            enum TOKEN { NONE, CURLY_OPEN, CURLY_CLOSE, SQUARED_OPEN, SQUARED_CLOSE, COLON, COMMA, STRING, NUMBER, TRUE, FALSE, NULL }

            TOKEN NextToken
            {
                get
                {
                    EatWhitespace();
                    if (json.Peek() == -1) return TOKEN.NONE;
                    switch (PeekChar)
                    {
                        case '{': return TOKEN.CURLY_OPEN;
                        case '}': json.Read(); return TOKEN.CURLY_CLOSE;
                        case '[': return TOKEN.SQUARED_OPEN;
                        case ']': json.Read(); return TOKEN.SQUARED_CLOSE;
                        case ',': json.Read(); return TOKEN.COMMA;
                        case '"': return TOKEN.STRING;
                        case ':': return TOKEN.COLON;
                        case '0': case '1': case '2': case '3': case '4':
                        case '5': case '6': case '7': case '8': case '9':
                        case '-': return TOKEN.NUMBER;
                    }
                    switch (NextWord)
                    {
                        case "false": return TOKEN.FALSE;
                        case "true": return TOKEN.TRUE;
                        case "null": return TOKEN.NULL;
                    }
                    return TOKEN.NONE;
                }
            }
        }

        public static string Serialize(object obj)
        {
            var s = new StringBuilder();
            Serializer.Value(s, obj, 0);
            return s.ToString();
        }

        sealed class Serializer
        {
            public static void Value(StringBuilder b, object value, int indent)
            {
                if (value == null) { b.Append("null"); return; }

                if (value is string str) { WriteString(b, str); return; }
                if (value is bool bo) { b.Append(bo ? "true" : "false"); return; }
                if (value is IDictionary dict) { WriteObject(b, dict, indent); return; }
                if (value is IList list) { WriteArray(b, list, indent); return; }
                if (value is float f) { b.Append(f.ToString("R", CultureInfo.InvariantCulture)); return; }
                if (value is double d) { b.Append(d.ToString("R", CultureInfo.InvariantCulture)); return; }
                if (value is int || value is long || value is short || value is byte)
                {
                    b.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                }
                WriteString(b, value.ToString());
            }

            static void WriteObject(StringBuilder b, IDictionary obj, int indent)
            {
                b.Append('{');
                bool first = true;
                foreach (DictionaryEntry e in obj)
                {
                    if (!first) b.Append(',');
                    b.Append('\n').Append(' ', (indent + 1) * 2);
                    WriteString(b, e.Key.ToString());
                    b.Append(": ");
                    Value(b, e.Value, indent + 1);
                    first = false;
                }
                if (!first) b.Append('\n').Append(' ', indent * 2);
                b.Append('}');
            }

            static void WriteArray(StringBuilder b, IList array, int indent)
            {
                b.Append('[');
                bool first = true;
                foreach (var o in array)
                {
                    if (!first) b.Append(',');
                    b.Append('\n').Append(' ', (indent + 1) * 2);
                    Value(b, o, indent + 1);
                    first = false;
                }
                if (!first) b.Append('\n').Append(' ', indent * 2);
                b.Append(']');
            }

            static void WriteString(StringBuilder b, string str)
            {
                b.Append('"');
                foreach (char c in str)
                {
                    switch (c)
                    {
                        case '"': b.Append("\\\""); break;
                        case '\\': b.Append("\\\\"); break;
                        case '\b': b.Append("\\b"); break;
                        case '\f': b.Append("\\f"); break;
                        case '\n': b.Append("\\n"); break;
                        case '\r': b.Append("\\r"); break;
                        case '\t': b.Append("\\t"); break;
                        default:
                            if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
                            else b.Append(c);
                            break;
                    }
                }
                b.Append('"');
            }
        }
    }
}

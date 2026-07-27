using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Wonderfold.Core.Serialization
{
    public enum JsonType
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object
    }

    /// <summary>
    /// A small, dependency-free JSON value.
    ///
    /// <para>Three hundred lines of parser buys the gameplay core the right to have no package
    /// references at all: it compiles identically inside Unity, inside <c>dotnet test</c> and inside the
    /// headless simulator, and level files never break because a JSON library version moved. For a
    /// format this small — levels are objects, arrays, numbers and strings — that trade is worth it.</para>
    /// </summary>
    public sealed class JsonValue
    {
        private static readonly JsonValue NullValue = new JsonValue { Type = JsonType.Null };

        public JsonType Type { get; private set; }
        private bool _bool;
        private double _number;
        private string _string;
        private List<JsonValue> _array;
        private Dictionary<string, JsonValue> _object;
        private List<string> _keyOrder;

        // ------------------------------------------------------------------ construction

        public static JsonValue Null() => NullValue;
        public static JsonValue Create(bool value) => new JsonValue { Type = JsonType.Bool, _bool = value };
        public static JsonValue Create(double value) => new JsonValue { Type = JsonType.Number, _number = value };
        public static JsonValue Create(int value) => new JsonValue { Type = JsonType.Number, _number = value };
        public static JsonValue Create(string value) =>
            value == null ? NullValue : new JsonValue { Type = JsonType.String, _string = value };

        public static JsonValue NewArray() => new JsonValue
        {
            Type = JsonType.Array,
            _array = new List<JsonValue>()
        };

        public static JsonValue NewObject() => new JsonValue
        {
            Type = JsonType.Object,
            _object = new Dictionary<string, JsonValue>(),
            _keyOrder = new List<string>()
        };

        // ------------------------------------------------------------------ reading

        public bool IsNull => Type == JsonType.Null;
        public int Count => Type == JsonType.Array ? _array.Count : (Type == JsonType.Object ? _keyOrder.Count : 0);
        public IReadOnlyList<string> Keys => _keyOrder ?? (IReadOnlyList<string>)Array.Empty<string>();
        public IReadOnlyList<JsonValue> Items => _array ?? (IReadOnlyList<JsonValue>)Array.Empty<JsonValue>();

        public JsonValue this[int index] =>
            Type == JsonType.Array && index >= 0 && index < _array.Count ? _array[index] : NullValue;

        public JsonValue this[string key]
        {
            get
            {
                if (Type != JsonType.Object || key == null) return NullValue;
                return _object.TryGetValue(key, out var value) ? value : NullValue;
            }
        }

        public bool Has(string key) => Type == JsonType.Object && _object.ContainsKey(key);

        public bool AsBool(bool fallback = false) => Type == JsonType.Bool ? _bool : fallback;

        public int AsInt(int fallback = 0) =>
            Type == JsonType.Number ? (int)Math.Round(_number) : fallback;

        public float AsFloat(float fallback = 0f) => Type == JsonType.Number ? (float)_number : fallback;

        public double AsDouble(double fallback = 0d) => Type == JsonType.Number ? _number : fallback;

        public string AsString(string fallback = null) => Type == JsonType.String ? _string : fallback;

        public T AsEnum<T>(T fallback) where T : struct
        {
            if (Type != JsonType.String) return fallback;
            return Enum.TryParse<T>(_string, true, out var parsed) ? parsed : fallback;
        }

        public List<string> AsStringList()
        {
            var result = new List<string>();
            if (Type != JsonType.Array) return result;
            for (int i = 0; i < _array.Count; i++)
            {
                var s = _array[i].AsString();
                if (s != null) result.Add(s);
            }

            return result;
        }

        public List<int> AsIntList()
        {
            var result = new List<int>();
            if (Type != JsonType.Array) return result;
            for (int i = 0; i < _array.Count; i++) result.Add(_array[i].AsInt());
            return result;
        }

        // ------------------------------------------------------------------ writing

        public JsonValue Add(JsonValue value)
        {
            if (Type != JsonType.Array) throw new InvalidOperationException("Not an array.");
            _array.Add(value ?? NullValue);
            return this;
        }

        public JsonValue Set(string key, JsonValue value)
        {
            if (Type != JsonType.Object) throw new InvalidOperationException("Not an object.");
            if (!_object.ContainsKey(key)) _keyOrder.Add(key);
            _object[key] = value ?? NullValue;
            return this;
        }

        public JsonValue Set(string key, string value) => Set(key, Create(value));
        public JsonValue Set(string key, int value) => Set(key, Create(value));
        public JsonValue Set(string key, bool value) => Set(key, Create(value));
        public JsonValue Set(string key, double value) => Set(key, Create(value));

        /// <summary>Skips the key entirely when the value is the default — keeps authored level files terse.</summary>
        public JsonValue SetIf(string key, int value, int skipWhen = 0) =>
            value == skipWhen ? this : Set(key, value);

        public JsonValue SetIf(string key, string value) =>
            string.IsNullOrEmpty(value) ? this : Set(key, value);

        public static JsonValue FromStrings(IEnumerable<string> values)
        {
            var array = NewArray();
            if (values == null) return array;
            foreach (var v in values) array.Add(Create(v));
            return array;
        }

        public static JsonValue FromInts(IEnumerable<int> values)
        {
            var array = NewArray();
            if (values == null) return array;
            foreach (var v in values) array.Add(Create(v));
            return array;
        }

        // ------------------------------------------------------------------ serialisation

        public string ToJson(bool pretty = true)
        {
            var sb = new StringBuilder();
            Write(sb, pretty, 0);
            return sb.ToString();
        }

        private void Write(StringBuilder sb, bool pretty, int indent)
        {
            switch (Type)
            {
                case JsonType.Null:
                    sb.Append("null");
                    break;
                case JsonType.Bool:
                    sb.Append(_bool ? "true" : "false");
                    break;
                case JsonType.Number:
                    if (Math.Abs(_number - Math.Round(_number)) < 1e-9 && Math.Abs(_number) < 1e15)
                        sb.Append(((long)Math.Round(_number)).ToString(CultureInfo.InvariantCulture));
                    else
                        sb.Append(_number.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case JsonType.String:
                    WriteString(sb, _string);
                    break;
                case JsonType.Array:
                    WriteArray(sb, pretty, indent);
                    break;
                case JsonType.Object:
                    WriteObject(sb, pretty, indent);
                    break;
            }
        }

        private void WriteArray(StringBuilder sb, bool pretty, int indent)
        {
            if (_array.Count == 0)
            {
                sb.Append("[]");
                return;
            }

            // Arrays of scalars stay on one line: level layouts are far more readable that way.
            bool inline = true;
            for (int i = 0; i < _array.Count; i++)
            {
                var t = _array[i].Type;
                if (t == JsonType.Array || t == JsonType.Object) { inline = false; break; }
            }

            sb.Append('[');
            for (int i = 0; i < _array.Count; i++)
            {
                if (i > 0) sb.Append(',');
                if (pretty && !inline) NewLine(sb, indent + 1);
                else if (i > 0) sb.Append(pretty ? " " : "");
                _array[i].Write(sb, pretty, indent + 1);
            }

            if (pretty && !inline) NewLine(sb, indent);
            sb.Append(']');
        }

        private void WriteObject(StringBuilder sb, bool pretty, int indent)
        {
            if (_keyOrder.Count == 0)
            {
                sb.Append("{}");
                return;
            }

            sb.Append('{');
            for (int i = 0; i < _keyOrder.Count; i++)
            {
                if (i > 0) sb.Append(',');
                if (pretty) NewLine(sb, indent + 1);
                WriteString(sb, _keyOrder[i]);
                sb.Append(pretty ? ": " : ":");
                _object[_keyOrder[i]].Write(sb, pretty, indent + 1);
            }

            if (pretty) NewLine(sb, indent);
            sb.Append('}');
        }

        private static void NewLine(StringBuilder sb, int indent)
        {
            sb.Append('\n');
            sb.Append(' ', indent * 2);
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
        }

        // ------------------------------------------------------------------ parsing

        public static JsonValue Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new JsonParseException("Empty document.", 0);
            int index = 0;
            var value = ParseValue(text, ref index);
            SkipWhitespace(text, ref index);
            if (index < text.Length) throw new JsonParseException("Trailing characters after document.", index);
            return value;
        }

        public static bool TryParse(string text, out JsonValue value, out string error)
        {
            try
            {
                value = Parse(text);
                error = null;
                return true;
            }
            catch (JsonParseException e)
            {
                value = NullValue;
                error = e.Message;
                return false;
            }
        }

        private static JsonValue ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new JsonParseException("Unexpected end of document.", i);

            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return Create(ParseString(s, ref i));
                case 't':
                    Expect(s, ref i, "true");
                    return Create(true);
                case 'f':
                    Expect(s, ref i, "false");
                    return Create(false);
                case 'n':
                    Expect(s, ref i, "null");
                    return NullValue;
                default:
                    return ParseNumber(s, ref i);
            }
        }

        private static JsonValue ParseObject(string s, ref int i)
        {
            var result = NewObject();
            i++; // {
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return result; }

            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new JsonParseException("Expected a key.", i);
                string key = ParseString(s, ref i);

                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new JsonParseException("Expected ':'.", i);
                i++;

                result.Set(key, ParseValue(s, ref i));

                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new JsonParseException("Unterminated object.", i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return result; }
                throw new JsonParseException("Expected ',' or '}'.", i);
            }
        }

        private static JsonValue ParseArray(string s, ref int i)
        {
            var result = NewArray();
            i++; // [
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return result; }

            while (true)
            {
                result.Add(ParseValue(s, ref i));
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new JsonParseException("Unterminated array.", i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return result; }
                throw new JsonParseException("Expected ',' or ']'.", i);
            }
        }

        private static string ParseString(string s, ref int i)
        {
            i++; // opening quote
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (i >= s.Length) break;
                char escape = s[i++];
                switch (escape)
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
                        if (i + 4 > s.Length) throw new JsonParseException("Truncated \\u escape.", i);
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default:
                        throw new JsonParseException($"Unknown escape '\\{escape}'.", i);
                }
            }

            throw new JsonParseException("Unterminated string.", i);
        }

        private static JsonValue ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' ||
                                    s[i] == '-' || s[i] == '+')) i++;

            string slice = s.Substring(start, i - start);
            if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new JsonParseException($"'{slice}' is not a number.", start);

            return Create(value);
        }

        private static void Expect(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                throw new JsonParseException($"Expected '{literal}'.", i);
            i += literal.Length;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') { i++; continue; }

                // Line comments are not JSON, but hand-authored level files are much nicer with them.
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    continue;
                }

                break;
            }
        }

        public override string ToString() => ToJson(false);
    }

    public sealed class JsonParseException : Exception
    {
        public int Index { get; }

        public JsonParseException(string message, int index) : base($"{message} (at character {index})")
        {
            Index = index;
        }
    }
}

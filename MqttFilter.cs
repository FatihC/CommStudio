using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CommStudio
{
    internal sealed class MqttFilterField
    {
        public string Path, Name, Value;
    }

    // A small, local query language; payloads and queries are never executed as code.
    internal sealed class MqttFilter
    {
        private sealed class Token
        {
            public string Text;
            public bool Quoted;
        }
        private readonly List<Token> tokens = new List<Token>();
        private int position;
        private MqttFilter(string query)
        {
            if (query.Length > 4096) throw new FormatException("Filtre en fazla 4096 karakter olabilir.");
            for (int i = 0; i < query.Length;)
            {
                if (char.IsWhiteSpace(query[i])) { i++; continue; }
                if ("():=".IndexOf(query[i]) >= 0) { tokens.Add(new Token { Text = query[i++].ToString() }); continue; }
                if (query[i] == '"')
                {
                    i++; StringBuilder text = new StringBuilder(); bool closed = false;
                    while (i < query.Length)
                    {
                        char c = query[i++];
                        if (c == '"') { closed = true; break; }
                        if (c == '\\' && i < query.Length && (query[i] == '"' || query[i] == '\\')) c = query[i++];
                        text.Append(c);
                    }
                    if (!closed) throw new FormatException("Çift tırnak kapatılmamış.");
                    tokens.Add(new Token { Text = text.ToString(), Quoted = true });
                }
                else
                {
                    int start = i;
                    while (i < query.Length && !char.IsWhiteSpace(query[i]) && "():=\"".IndexOf(query[i]) < 0) i++;
                    tokens.Add(new Token { Text = query.Substring(start, i - start) });
                }
                if (tokens.Count > 512) throw new FormatException("Filtre çok fazla koşul içeriyor.");
            }
        }
        public static Func<MqttLogEntry, bool> Compile(string query)
        {
            MqttFilter parser = new MqttFilter(query ?? "");
            if (parser.tokens.Count == 0) return delegate { return true; };
            Func<MqttLogEntry, bool> result = parser.Or(0);
            if (parser.position != parser.tokens.Count) throw new FormatException("Beklenmeyen ifade: " + parser.tokens[parser.position].Text);
            return result;
        }
        private bool Is(string value)
        { return position < tokens.Count && !tokens[position].Quoted && string.Equals(tokens[position].Text, value, StringComparison.OrdinalIgnoreCase); }
        private bool Take(string value) { if (!Is(value)) return false; position++; return true; }
        private Func<MqttLogEntry, bool> Or(int depth)
        {
            Func<MqttLogEntry, bool> left = And(depth);
            while (Take("OR"))
            { Func<MqttLogEntry, bool> previous = left, right = And(depth); left = delegate(MqttLogEntry e) { return previous(e) || right(e); }; }
            return left;
        }
        private Func<MqttLogEntry, bool> And(int depth)
        {
            Func<MqttLogEntry, bool> left = Unary(depth);
            while (position < tokens.Count && !Is("OR") && !Is(")"))
            {
                Take("AND"); // Adjacent terms imply AND.
                Func<MqttLogEntry, bool> previous = left, right = Unary(depth);
                left = delegate(MqttLogEntry e) { return previous(e) && right(e); };
            }
            return left;
        }
        private Func<MqttLogEntry, bool> Unary(int depth)
        {
            if (depth > 64) throw new FormatException("Filtre çok fazla iç içe koşul içeriyor.");
            if (Take("NOT")) { Func<MqttLogEntry, bool> inner = Unary(depth + 1); return delegate(MqttLogEntry e) { return !inner(e); }; }
            if (Take("("))
            {
                Func<MqttLogEntry, bool> inner = Or(depth + 1);
                if (!Take(")")) throw new FormatException("Kapanış parantezi eksik.");
                return inner;
            }
            string term = Value(false);
            if (Take(":") || Take("="))
            {
                string value = Value(true);
                return delegate(MqttLogEntry e)
                {
                    if (string.Equals(term, "topic", StringComparison.OrdinalIgnoreCase)) return Equal(e.Topic, value);
                    if (string.Equals(term, "direction", StringComparison.OrdinalIgnoreCase)) return Equal(e.Direction, value);
                    foreach (MqttFilterField field in e.FilterFields)
                        if (Equal(term.IndexOf('.') >= 0 ? field.Path : field.Name, term) && Equal(field.Value, value)) return true;
                    return false;
                };
            }
            return delegate(MqttLogEntry e)
            { return Contains(e.Topic, term) || Contains(e.RawText, term) || Contains(e.DisplayText, term); };
        }
        private string Value(bool afterField)
        {
            if (position == tokens.Count || Is("(") || Is(")") || Is(":") || Is("=") ||
                (!afterField && (Is("AND") || Is("OR") || Is("NOT"))))
                throw new FormatException(afterField ? "Alan adından sonra bir değer yazın." : "Bir arama sözcüğü veya alan koşulu bekleniyor.");
            return tokens[position++].Text;
        }
        private static bool Equal(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        private static bool Contains(string text, string value) { return text != null && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0; }

        public static List<MqttFilterField> ReadFields(string json)
        {
            if (JsonSyntaxValidator.Validate(json) != null) return new List<MqttFilterField>();
            JsonFields reader = new JsonFields(json); reader.Value("", ""); return reader.Fields;
        }
        // Walk validated JSON without converting numbers to floating point. Large
        // serial numbers, duplicate keys and escaped string values remain searchable.
        private sealed class JsonFields
        {
            private readonly string json;
            private int index;
            public readonly List<MqttFilterField> Fields = new List<MqttFilterField>();
            public JsonFields(string text) { json = text; }
            private void WhiteSpace() { while (index < json.Length && char.IsWhiteSpace(json[index])) index++; }
            private string String()
            {
                index++; StringBuilder result = new StringBuilder();
                while (json[index] != '"')
                {
                    char c = json[index++];
                    if (c == '\\')
                    {
                        c = json[index++];
                        if (c == 'u') { c = (char)int.Parse(json.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture); index += 4; }
                        else if (c == 'n') c = '\n'; else if (c == 'r') c = '\r'; else if (c == 't') c = '\t';
                        else if (c == 'b') c = '\b'; else if (c == 'f') c = '\f';
                    }
                    result.Append(c);
                }
                index++; return result.ToString();
            }
            public void Value(string path, string name)
            {
                WhiteSpace();
                if (json[index] == '{')
                {
                    index++; WhiteSpace();
                    while (json[index] != '}')
                    {
                        string key = String(); WhiteSpace(); index++;
                        Value(path.Length == 0 ? key : path + "." + key, key); WhiteSpace();
                        if (json[index] != ',') break; index++; WhiteSpace();
                    }
                    index++; return;
                }
                if (json[index] == '[')
                {
                    index++; WhiteSpace();
                    while (json[index] != ']') { Value(path, name); WhiteSpace(); if (json[index] != ',') break; index++; }
                    index++; return;
                }
                string value;
                if (json[index] == '"') value = String();
                else
                {
                    int start = index;
                    while (index < json.Length && !char.IsWhiteSpace(json[index]) && ",]}".IndexOf(json[index]) < 0) index++;
                    value = json.Substring(start, index - start);
                }
                if (name.Length > 0) Fields.Add(new MqttFilterField { Path = path, Name = name, Value = value });
            }
        }
    }
}

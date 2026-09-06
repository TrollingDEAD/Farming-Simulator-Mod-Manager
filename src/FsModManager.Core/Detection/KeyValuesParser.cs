namespace FsModManager.Core.Detection;

/// <summary>
/// Minimal parser for Valve's KeyValues format ("VDF") as used by libraryfolders.vdf
/// and appmanifest_*.acf files. Handles quoted keys/values, nested { } blocks,
/// backslash escapes and // comments. Deliberately does NOT support the full grammar
/// (no conditionals like [platform], no #include/#base, no unquoted values).
/// A value in the returned dictionaries is either a string or a nested Dictionary.
/// </summary>
internal static class KeyValuesParser
{
    public static Dictionary<string, object> Parse(string text)
    {
        var reader = new TokenReader(text);
        var root = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        ParseBlock(reader, root);
        return root;
    }

    public static Dictionary<string, object>? GetSection(IReadOnlyDictionary<string, object> parent, string key)
        => parent.TryGetValue(key, out var value) ? value as Dictionary<string, object> : null;

    public static string? GetString(IReadOnlyDictionary<string, object> parent, string key)
        => parent.TryGetValue(key, out var value) ? value as string : null;

    private static void ParseBlock(TokenReader reader, Dictionary<string, object> target)
    {
        while (reader.Read() is { } key)
        {
            if (key == "}")
            {
                return;
            }

            if (key == "{")
            {
                continue; // stray opening brace — tolerate and keep going
            }

            var next = reader.Read();
            if (next == "{")
            {
                var child = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                ParseBlock(reader, child);
                target[key] = child;
            }
            else if (next is not null && next != "}")
            {
                target[key] = next;
            }
            else
            {
                return; // malformed input (dangling key or premature close) — stop quietly
            }
        }
    }

    private sealed class TokenReader(string text)
    {
        private int _pos;

        public string? Read()
        {
            SkipWhitespaceAndComments();
            if (_pos >= text.Length)
            {
                return null;
            }

            var c = text[_pos];
            if (c is '{' or '}')
            {
                _pos++;
                return c.ToString();
            }

            return c == '"' ? ReadQuoted() : ReadBare();
        }

        private void SkipWhitespaceAndComments()
        {
            while (_pos < text.Length)
            {
                if (char.IsWhiteSpace(text[_pos]))
                {
                    _pos++;
                }
                else if (_pos + 1 < text.Length && text[_pos] == '/' && text[_pos + 1] == '/')
                {
                    while (_pos < text.Length && text[_pos] != '\n')
                    {
                        _pos++;
                    }
                }
                else
                {
                    return;
                }
            }
        }

        private string ReadQuoted()
        {
            _pos++; // opening quote
            var sb = new System.Text.StringBuilder();
            while (_pos < text.Length)
            {
                var c = text[_pos++];
                if (c == '"')
                {
                    break;
                }

                if (c == '\\' && _pos < text.Length)
                {
                    // Keep the escaped character itself ("\\" -> "\", "\"" -> """).
                    sb.Append(text[_pos++]);
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private string ReadBare()
        {
            var start = _pos;
            while (_pos < text.Length && !char.IsWhiteSpace(text[_pos]) && text[_pos] is not '{' and not '}')
            {
                _pos++;
            }

            return text[start.._pos];
        }
    }
}

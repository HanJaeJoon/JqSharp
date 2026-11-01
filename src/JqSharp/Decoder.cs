using System.Text;

namespace JqSharp;

// A JSON decoder with the behavior and error messages of Go's json.Decoder with UseNumber,
// which gojq uses for fromjson, module data and command line inputs. Numbers decode to JsonNumber.
internal static class Decoder
{
    private const int MaxDepth = 10000;

    // Decodes all the values in s.
    public static List<object?> DecodeAll(string s)
    {
        List<object?> values = new();
        int pos = 0;
        while (TryDecode(s, ref pos, out object? v))
        {
            values.Add(v);
        }
        return values;
    }

    // Decodes the next value at pos; returns false when only whitespace is left.
    public static bool TryDecode(string s, ref int pos, out object? value)
    {
        SkipSpace(s, ref pos);
        if (pos >= s.Length)
        {
            value = null;
            return false;
        }
        value = ParseValue(s, ref pos, 0, true);
        return true;
    }

    private static void SkipSpace(string s, ref int pos)
    {
        while (pos < s.Length && IsSpace(s[pos]))
        {
            pos++;
        }
    }

    private static bool IsSpace(char c)
    {
        return c == ' ' || c == '\t' || c == '\r' || c == '\n';
    }

    private static object? ParseValue(string s, ref int pos, int depth, bool top)
    {
        Jq.EnsureStack();
        SkipSpace(s, ref pos);
        if (pos >= s.Length)
        {
            throw UnexpectedEof();
        }
        char c = s[pos];
        switch (c)
        {
            case '{':
                return ParseObject(s, ref pos, depth + 1);
            case '[':
                return ParseArray(s, ref pos, depth + 1);
            case '"':
                return ParseString(s, ref pos);
            case 't':
                ParseLiteral(s, ref pos, "true");
                return true;
            case 'f':
                ParseLiteral(s, ref pos, "false");
                return false;
            case 'n':
                ParseLiteral(s, ref pos, "null");
                return null;
            default:
                if (c == '-' || (c >= '0' && c <= '9'))
                {
                    return ParseNumber(s, ref pos);
                }
                throw InvalidChar(s, pos, "looking for beginning of value");
        }
    }

    private static Dictionary<string, object?> ParseObject(string s, ref int pos, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new SimpleError("exceeded max depth");
        }
        Dictionary<string, object?> m = new();
        pos++;
        SkipSpace(s, ref pos);
        if (pos >= s.Length)
        {
            throw UnexpectedEof();
        }
        if (s[pos] == '}')
        {
            pos++;
            return m;
        }
        while (true)
        {
            SkipSpace(s, ref pos);
            if (pos >= s.Length)
            {
                throw UnexpectedEof();
            }
            if (s[pos] != '"')
            {
                throw InvalidChar(s, pos, "looking for beginning of object key string");
            }
            string key = ParseString(s, ref pos);
            SkipSpace(s, ref pos);
            if (pos >= s.Length)
            {
                throw UnexpectedEof();
            }
            if (s[pos] != ':')
            {
                throw InvalidChar(s, pos, "after object key");
            }
            pos++;
            m[key] = ParseValue(s, ref pos, depth, false);
            AfterValue(s, ref pos);
            if (s[pos] == ',')
            {
                pos++;
                continue;
            }
            if (s[pos] == '}')
            {
                pos++;
                return m;
            }
            throw InvalidChar(s, pos, "after object key:value pair");
        }
    }

    private static List<object?> ParseArray(string s, ref int pos, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new SimpleError("exceeded max depth");
        }
        List<object?> a = new();
        pos++;
        SkipSpace(s, ref pos);
        if (pos >= s.Length)
        {
            throw UnexpectedEof();
        }
        if (s[pos] == ']')
        {
            pos++;
            return a;
        }
        while (true)
        {
            a.Add(ParseValue(s, ref pos, depth, false));
            AfterValue(s, ref pos);
            if (s[pos] == ',')
            {
                pos++;
                continue;
            }
            if (s[pos] == ']')
            {
                pos++;
                return a;
            }
            throw InvalidChar(s, pos, "after array element");
        }
    }

    private static void AfterValue(string s, ref int pos)
    {
        SkipSpace(s, ref pos);
        if (pos >= s.Length)
        {
            throw UnexpectedEof();
        }
    }

    private static void ParseLiteral(string s, ref int pos, string literal)
    {
        for (int i = 1; i < literal.Length; i++)
        {
            if (pos + i >= s.Length)
            {
                throw UnexpectedEof();
            }
            if (s[pos + i] != literal[i])
            {
                pos += i;
                throw InvalidChar(s, pos, "in literal " + literal + " (expecting '" + literal[i] + "')");
            }
        }
        pos += literal.Length;
    }

    private static JsonNumber ParseNumber(string s, ref int pos)
    {
        int start = pos;
        if (s[pos] == '-')
        {
            pos++;
            if (pos >= s.Length)
            {
                throw UnexpectedEof();
            }
            if (!IsDigit(s[pos]))
            {
                throw InvalidChar(s, pos, "in numeric literal");
            }
        }
        if (s[pos] == '0')
        {
            pos++;
        }
        else
        {
            while (pos < s.Length && IsDigit(s[pos]))
            {
                pos++;
            }
        }
        if (pos < s.Length && s[pos] == '.')
        {
            pos++;
            if (pos >= s.Length)
            {
                throw UnexpectedEof();
            }
            if (!IsDigit(s[pos]))
            {
                throw InvalidChar(s, pos, "after decimal point in numeric literal");
            }
            while (pos < s.Length && IsDigit(s[pos]))
            {
                pos++;
            }
        }
        if (pos < s.Length && (s[pos] == 'e' || s[pos] == 'E'))
        {
            pos++;
            if (pos < s.Length && (s[pos] == '+' || s[pos] == '-'))
            {
                pos++;
            }
            if (pos >= s.Length)
            {
                throw UnexpectedEof();
            }
            if (!IsDigit(s[pos]))
            {
                throw InvalidChar(s, pos, "in exponent of numeric literal");
            }
            while (pos < s.Length && IsDigit(s[pos]))
            {
                pos++;
            }
        }
        return new JsonNumber(s.Substring(start, pos - start));
    }

    private static bool IsDigit(char c)
    {
        return c >= '0' && c <= '9';
    }

    private static string ParseString(string s, ref int pos)
    {
        pos++;
        StringBuilder? sb = null;
        int start = pos;
        while (true)
        {
            if (pos >= s.Length)
            {
                throw UnexpectedEof();
            }
            char c = s[pos];
            if (c == '"')
            {
                string str = sb == null ? s.Substring(start, pos - start) : sb.Append(s, start, pos - start).ToString();
                pos++;
                return str;
            }
            if (c < 0x20)
            {
                throw InvalidChar(s, pos, "in string literal");
            }
            if (char.IsSurrogate(c))
            {
                if (char.IsHighSurrogate(c) && pos + 1 < s.Length && char.IsLowSurrogate(s[pos + 1]))
                {
                    pos += 2;
                    continue;
                }
                sb ??= new StringBuilder();
                sb.Append(s, start, pos - start).Append('\uFFFD');
                pos++;
                start = pos;
                continue;
            }
            if (c != '\\')
            {
                pos++;
                continue;
            }
            sb ??= new StringBuilder();
            sb.Append(s, start, pos - start);
            pos++;
            if (pos >= s.Length)
            {
                throw UnexpectedEof();
            }
            c = s[pos];
            switch (c)
            {
                case '"':
                case '\\':
                case '/':
                    sb.Append(c);
                    pos++;
                    break;
                case 'b':
                    sb.Append('\b');
                    pos++;
                    break;
                case 'f':
                    sb.Append('\f');
                    pos++;
                    break;
                case 'n':
                    sb.Append('\n');
                    pos++;
                    break;
                case 'r':
                    sb.Append('\r');
                    pos++;
                    break;
                case 't':
                    sb.Append('\t');
                    pos++;
                    break;
                case 'u':
                {
                    int r = ReadHex4(s, pos + 1);
                    pos += 5;
                    if (char.IsSurrogate((char)r))
                    {
                        if (char.IsHighSurrogate((char)r) && pos + 1 < s.Length && s[pos] == '\\' && s[pos + 1] == 'u'
                            && Lexer.TryParseHex4(s, pos + 2, out int r2) && char.IsLowSurrogate((char)r2))
                        {
                            sb.Append((char)r).Append((char)r2);
                            pos += 6;
                        }
                        else
                        {
                            sb.Append('\uFFFD');
                        }
                    }
                    else
                    {
                        sb.Append((char)r);
                    }
                    break;
                }
                default:
                    throw InvalidChar(s, pos, "in string escape code");
            }
            start = pos;
        }
    }

    private static int ReadHex4(string s, int pos)
    {
        int r = 0;
        for (int i = 0; i < 4; i++)
        {
            if (pos + i >= s.Length)
            {
                throw UnexpectedEof();
            }
            char c = s[pos + i];
            int d = c >= '0' && c <= '9' ? c - '0'
                : c >= 'a' && c <= 'f' ? c - 'a' + 10
                : c >= 'A' && c <= 'F' ? c - 'A' + 10
                : -1;
            if (d < 0)
            {
                throw InvalidChar(s, pos + i, "in \\u hexadecimal character escape");
            }
            r = (r << 4) | d;
        }
        return r;
    }

    private static JqException UnexpectedEof()
    {
        return new SimpleError("unexpected EOF");
    }

    internal static JqException InvalidChar(string s, int pos, string context)
    {
        return new SimpleError("invalid character " + QuoteChar(s, pos) + " " + context);
    }

    // Go reports the first byte of the UTF-8 encoding, quoted like strconv.Quote of that byte as a rune.
    private static string QuoteChar(string s, int pos)
    {
        char c = s[pos];
        if (c == '\'')
        {
            return "'\\''";
        }
        if (c == '"')
        {
            return "'\"'";
        }
        int b = c < 0x80 ? c : Encoding.UTF8.GetBytes(s.Substring(pos, char.IsHighSurrogate(c) && pos + 1 < s.Length ? 2 : 1))[0];
        string q = b switch
        {
            7 => "\\a",
            8 => "\\b",
            9 => "\\t",
            10 => "\\n",
            11 => "\\v",
            12 => "\\f",
            13 => "\\r",
            '\\' => "\\\\",
            < 0x20 or 0x7f => "\\x" + b.ToString("x2"),
            (>= 0x80 and <= 0xa0) or 0xad => "\\u00" + b.ToString("x2"),
            _ => ((char)b).ToString(),
        };
        return "'" + q + "'";
    }
}

using System.Numerics;

namespace JqSharp;

internal static partial class Jq
{
    // Returns the jq type name of v; v must be one of the value types JqSharp works with.
    internal static string TypeOf(object? v)
    {
        return v switch
        {
            null => "null",
            bool => "boolean",
            long or double or BigInteger or JsonNumber => "number",
            string => "string",
            List<object?> => "array",
            Dictionary<string, object?> => "object",
            _ => throw new InvalidOperationException("invalid type: " + v.GetType() + " (" + v + ")"),
        };
    }

    internal static string TypeErrorPreview(object? v)
    {
        return v switch
        {
            null => "null",
            IIter => "gojq.Iter",
            _ => TypeOf(v) + " (" + Preview(v) + ")",
        };
    }

    // Returns the JSON encoding of v truncated to 30 UTF-8 bytes, as used in error messages.
    internal static string Preview(object? v)
    {
        string s = Encoder.LimitedMarshal(v, 32);
        const int l = 30;
        if (Utf8Length(s) > l)
        {
            string trailing = v switch
            {
                string => " ...\"",
                List<object?> => " ...]",
                Dictionary<string, object?> => " ...}",
                _ => " ...",
            };
            int n = Utf8Length(s);
            int end = s.Length;
            while (n > l - trailing.Length)
            {
                int size = end >= 2 && char.IsLowSurrogate(s[end - 1]) && char.IsHighSurrogate(s[end - 2]) ? 2 : 1;
                n -= Utf8Length(s.Substring(end - size, size));
                end -= size;
            }
            s = s.Substring(0, end) + trailing;
        }
        return s;
    }

    internal static int Utf8Length(string s)
    {
        int n = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c < 0x80)
            {
                n++;
            }
            else if (c < 0x800)
            {
                n += 2;
            }
            else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                n += 4;
                i++;
            }
            else
            {
                n += 3;
            }
        }
        return n;
    }
}

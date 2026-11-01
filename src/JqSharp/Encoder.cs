using System.Globalization;
using System.Numerics;
using System.Text;

namespace JqSharp;

// The jq flavored JSON encoder: NaN is null, infinities are clamped to the largest finite doubles, \b and \f are
// used in strings, and '<', '>', '&', U+2028 and U+2029 are not escaped. Object keys are sorted.
internal sealed class Encoder
{
    private const string Hex = "0123456789abcdef";

    private readonly StringBuilder _w;
    private readonly int _limit;
    private int _bytes;

    private Encoder(StringBuilder w, int limit)
    {
        _w = w;
        _limit = limit;
    }

    public static string Marshal(object? v)
    {
        Encoder e = new(new StringBuilder(), -1);
        e.Encode(v);
        return e._w.ToString();
    }

    // Encodes v until at least n UTF-8 bytes are written, without splitting surrogate pairs.
    public static string LimitedMarshal(object? v, int n)
    {
        Encoder e = new(new StringBuilder(), n);
        try
        {
            e.Encode(v);
        }
        catch (LimitReachedException)
        {
        }
        return e._w.ToString();
    }

    public static void EncodeString(StringBuilder sb, string s)
    {
        new Encoder(sb, -1).EncodeStr(s);
    }

    private void Write(string s)
    {
        if (_limit < 0)
        {
            _w.Append(s);
            return;
        }
        foreach (char c in s)
        {
            Write(c);
        }
    }

    private void Write(char c)
    {
        _w.Append(c);
        if (_limit < 0)
        {
            return;
        }
        if (c < 0x80)
        {
            _bytes++;
        }
        else if (c < 0x800)
        {
            _bytes += 2;
        }
        else if (char.IsHighSurrogate(c))
        {
            return; // counted with the low surrogate
        }
        else
        {
            _bytes += char.IsLowSurrogate(c) ? 4 : 3;
        }
        if (_bytes >= _limit)
        {
            throw new LimitReachedException();
        }
    }

    private void Encode(object? v)
    {
        Jq.EnsureStack();
        switch (v)
        {
            case null:
                Write("null");
                break;
            case bool b:
                Write(b ? "true" : "false");
                break;
            case long i:
                Write(i.ToString(CultureInfo.InvariantCulture));
                break;
            case double f:
                Write(FormatFloat(f));
                break;
            case BigInteger bi:
                Write(bi.ToString(CultureInfo.InvariantCulture));
                break;
            case JsonNumber n:
                Write(n.Value);
                break;
            case string s:
                EncodeStr(s);
                break;
            case List<object?> a:
                EncodeArray(a);
                break;
            case Dictionary<string, object?> m:
                EncodeObject(m);
                break;
            default:
                throw new InvalidOperationException("invalid type: " + v.GetType() + " (" + v + ")");
        }
    }

    // Formats like Go's strconv.FormatFloat(f, 'f' or 'e', -1, 64), as encoding/json does.
    internal static string FormatFloat(double f)
    {
        if (double.IsNaN(f))
        {
            return "null";
        }
        f = Math.Min(Math.Max(f, -double.MaxValue), double.MaxValue);
        double x = Math.Abs(f);
        bool exp = (x != 0 && x < 1e-6) || x >= 1e21;
        bool negative = f < 0 || (f == 0 && 1 / f < 0);
        StringBuilder sb = new();
        if (negative)
        {
            sb.Append('-');
        }
        if (x == 0)
        {
            sb.Append('0');
            return sb.ToString();
        }
        ShortestDigits(x, out string digits, out int point);
        if (exp)
        {
            sb.Append(digits[0]);
            if (digits.Length > 1)
            {
                sb.Append('.').Append(digits, 1, digits.Length - 1);
            }
            int e = point - 1;
            sb.Append('e').Append(e < 0 ? '-' : '+');
            e = Math.Abs(e);
            // Go writes at least two exponent digits, then encoding/json trims e-07 to e-7
            if (e < 10 && point - 1 >= 0)
            {
                sb.Append('0');
            }
            sb.Append(e.ToString(CultureInfo.InvariantCulture));
        }
        else if (point <= 0)
        {
            sb.Append("0.").Append('0', -point).Append(digits);
        }
        else if (point >= digits.Length)
        {
            sb.Append(digits).Append('0', point - digits.Length);
        }
        else
        {
            sb.Append(digits, 0, point).Append('.').Append(digits, point, digits.Length - point);
        }
        return sb.ToString();
    }

    // Finds the shortest decimal digits that round trip to x (x > 0); the value is 0.digits * 10^point.
    private static void ShortestDigits(double x, out string digits, out int point)
    {
#if NET
        // "R" is the shortest round trip string since .NET Core 3.0
        string s = x.ToString("R", CultureInfo.InvariantCulture);
#else
        // Older runtimes do not guarantee the shortest "R". For a normal double, a round tripping 15 digit form with
        // its trailing zeros removed is the shortest; subnormals have less precision and need the full search.
        // TryParse because .NET Framework fails to parse values that round above double.MaxValue.
        string s = "";
        for (int precision = x < 2.2250738585072014E-308 ? 1 : 15; precision <= 17; precision++)
        {
            s = x.ToString("E" + (precision - 1), CultureInfo.InvariantCulture);
            if (precision == 17 || RoundTrips(s, x))
            {
                break;
            }
        }
#endif
        int e = s.IndexOf('E');
        string mantissa = e < 0 ? s : s.Substring(0, e);
        int exponent = e < 0 ? 0 : int.Parse(s.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        int dot = mantissa.IndexOf('.');
        point = (dot < 0 ? mantissa.Length : dot) + exponent;
        string all = dot < 0 ? mantissa : mantissa.Remove(dot, 1);
        int lead = 0;
        while (lead < all.Length - 1 && all[lead] == '0')
        {
            lead++;
        }
        point -= lead;
        digits = all.Substring(lead).TrimEnd('0');
        if (digits.Length == 0)
        {
            digits = "0";
        }
    }

#if !NET
    private static bool RoundTrips(string s, double x)
    {
        if (x >= 2.2250738585072014E-308)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double y) && y == x;
        }
        // .NET Framework parses subnormals inexactly, so check exactly that s is within half an ulp (2^-1075) of x
        int e = s.IndexOf('E');
        string mantissa = s.Substring(0, e);
        int dot = mantissa.IndexOf('.');
        int exponent = int.Parse(s.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) - (dot < 0 ? 0 : mantissa.Length - dot - 1);
        BigInteger d = BigInteger.Parse(dot < 0 ? mantissa : mantissa.Remove(dot, 1), CultureInfo.InvariantCulture);
        long m = BitConverter.DoubleToInt64Bits(x) & 0xFFFFFFFFFFFFFL;
        // compare |d * 10^exponent - m * 2^-1074| with 2^-1075, scaled by 2^1075 * 10^-exponent (exponent < 0 here)
        BigInteger scale = BigInteger.Pow(10, -exponent);
        BigInteger diff = BigInteger.Abs((d << 1075) - ((new BigInteger(m) << 1) * scale));
        int c = diff.CompareTo(scale);
        return c < 0 || (c == 0 && (m & 1) == 0);
    }
#endif

    private void EncodeStr(string s)
    {
        Write('"');
        int start = 0;
        int i = 0;
        while (i < s.Length)
        {
            char b = s[i];
            if (b < 0x80)
            {
                if (b >= ' ' && b <= '~' && b != '"' && b != '\\')
                {
                    i++;
                    continue;
                }
                Flush(s, start, i);
                switch (b)
                {
                    case '"':
                        Write("\\\"");
                        break;
                    case '\\':
                        Write("\\\\");
                        break;
                    case '\b':
                        Write("\\b");
                        break;
                    case '\f':
                        Write("\\f");
                        break;
                    case '\n':
                        Write("\\n");
                        break;
                    case '\r':
                        Write("\\r");
                        break;
                    case '\t':
                        Write("\\t");
                        break;
                    default:
                        Write("\\u00");
                        Write(Hex[b >> 4]);
                        Write(Hex[b & 0xF]);
                        break;
                }
                i++;
                start = i;
                continue;
            }
            if (char.IsSurrogate(b))
            {
                if (char.IsHighSurrogate(b) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    i += 2;
                    continue;
                }
                // a lone surrogate is invalid UTF-16, like invalid UTF-8 in Go
                Flush(s, start, i);
                Write("\\ufffd");
                i++;
                start = i;
                continue;
            }
            i++;
        }
        Flush(s, start, s.Length);
        Write('"');
    }

    private void Flush(string s, int start, int end)
    {
        if (start >= end)
        {
            return;
        }
        if (_limit < 0)
        {
            _w.Append(s, start, end - start);
            return;
        }
        for (int i = start; i < end; i++)
        {
            Write(s[i]);
        }
    }

    private void EncodeArray(List<object?> vs)
    {
        Write('[');
        for (int i = 0; i < vs.Count; i++)
        {
            if (i > 0)
            {
                Write(',');
            }
            Encode(vs[i]);
        }
        Write(']');
    }

    private void EncodeObject(Dictionary<string, object?> vs)
    {
        Write('{');
        List<string> keys = Jq.Keys(vs);
        for (int i = 0; i < keys.Count; i++)
        {
            if (i > 0)
            {
                Write(',');
            }
            EncodeStr(keys[i]);
            Write(':');
            Encode(vs[keys[i]]);
        }
        Write('}');
    }

    private sealed class LimitReachedException : Exception
    {
    }
}

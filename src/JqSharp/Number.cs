using System.Globalization;
using System.Numerics;

namespace JqSharp;

/// <summary>
/// A JSON number kept as its literal text, like Go's <c>json.Number</c>.
/// </summary>
/// <remarks>
/// <see cref="JqValue.FromJson"/> decodes numbers to this type so that the literal survives a round trip
/// (<c>10.0</c> and <c>100000000000000000000001</c> print unchanged). Arithmetic converts it to
/// <see cref="long"/>, <see cref="double"/> or <see cref="BigInteger"/> first.
/// </remarks>
public sealed class JsonNumber : IEquatable<JsonNumber>
{
    /// <summary>
    /// Creates a number from its literal text.
    /// </summary>
    /// <param name="value">The literal, which must be a valid JSON number.</param>
    public JsonNumber(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// The literal text.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc/>
    public bool Equals(JsonNumber? other) => other is not null && other.Value == Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is JsonNumber n && Equals(n);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => Value;
}

internal static partial class Jq
{
    private const double TwoPow63 = 9223372036854775808.0;

    // Converts a number to an integer, truncating toward zero and clamping out of range values (NaN becomes long.MinValue).
    internal static bool ToInt(object? v, out long i)
    {
        switch (v)
        {
            case long l:
                i = l;
                return true;
            case double f:
                i = FloatToInt(f);
                return true;
            case BigInteger b:
                if (b >= long.MinValue && b <= long.MaxValue)
                {
                    i = (long)b;
                }
                else
                {
                    i = b.Sign > 0 ? long.MaxValue : long.MinValue;
                }
                return true;
            case JsonNumber n:
                return ToInt(ParseNumber(n), out i);
            default:
                i = 0;
                return false;
        }
    }

    internal static bool ToFloat64(object? v, out double f)
    {
        switch (v)
        {
            case long l:
                f = l;
                return true;
            case double d:
                f = d;
                return true;
            case BigInteger b:
                f = BigToFloat(b);
                return true;
            case JsonNumber n:
                return ToFloat64(ParseNumber(n), out f);
            default:
                f = 0.0;
                return false;
        }
    }

    internal static long FloatToInt(double x)
    {
        if (-TwoPow63 <= x && x < TwoPow63)
        {
            return (long)x;
        }
        return x > 0 ? long.MaxValue : long.MinValue;
    }

    internal static double BigToFloat(BigInteger x)
    {
        if (x >= long.MinValue && x <= long.MaxValue)
        {
            return (long)x;
        }
        if (TryParseFloat(x.ToString(CultureInfo.InvariantCulture), out double f))
        {
            return f;
        }
        return x.Sign > 0 ? double.PositiveInfinity : double.NegativeInfinity;
    }

    // Like strconv.ParseFloat: fails on syntax errors and on overflow.
    private static bool TryParseFloat(string s, out double f)
    {
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f) && !double.IsInfinity(f);
    }

    internal static object ParseNumber(JsonNumber v)
    {
        return ParseNumber(v.Value);
    }

    internal static object ParseNumber(string s)
    {
        if (long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long i))
        {
            return i;
        }
        if (s.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0)
        {
            if (TryParseFloat(s, out double f))
            {
                return f;
            }
        }
        else if (BigInteger.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger b))
        {
            return b;
        }
        return s.StartsWith("-", StringComparison.Ordinal) ? double.NegativeInfinity : double.PositiveInfinity;
    }

    internal static object ToNumber(string v)
    {
        return ParseNumber(v);
    }
}

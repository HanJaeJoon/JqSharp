using System.Numerics;

namespace JqSharp;

/// <summary>
/// Converts between JSON text and the values JqSharp works with.
/// </summary>
/// <remarks>
/// Values are <see langword="null"/>, <see cref="bool"/>, numbers (<see cref="long"/>, <see cref="double"/>,
/// <see cref="BigInteger"/>, <see cref="JsonNumber"/>), <see cref="string"/>, <c>List&lt;object?&gt;</c> for arrays and
/// <c>Dictionary&lt;string, object?&gt;</c> for objects.
/// </remarks>
public static class JqValue
{
    /// <summary>
    /// Decodes one JSON value. Numbers become <see cref="JsonNumber"/> values that keep their literal text.
    /// </summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="JqException">
    /// The text is not exactly one valid JSON value, or it is nested too deeply. The message gives the reason and
    /// the offset (in UTF-16 code units) where decoding stopped, like
    /// <c>invalid JSON at offset 2: invalid character '2' after top-level value</c>.
    /// </exception>
    public static object? FromJson(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }
        int pos = 0;
        try
        {
            if (!Decoder.TryDecode(json, ref pos, out object? v))
            {
                throw new SimpleError("unexpected EOF");
            }
            while (pos < json.Length && (json[pos] == ' ' || json[pos] == '\t' || json[pos] == '\r' || json[pos] == '\n'))
            {
                pos++;
            }
            if (pos < json.Length)
            {
                throw Decoder.InvalidChar(json, pos, "after top-level value");
            }
            return v;
        }
        catch (JqException err)
        {
            // The decoder advances pos by reference, so it is where decoding stopped.
            throw new SimpleError("invalid JSON at offset " + pos + ": " + err.Message);
        }
    }

    /// <summary>
    /// Encodes a value as compact JSON, the way jq does: object keys are sorted, NaN is <c>null</c>,
    /// and infinities become the largest finite numbers.
    /// </summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>The JSON text.</returns>
    /// <exception cref="ArgumentException">The value contains an unsupported type.</exception>
    /// <exception cref="JqException">The value is nested too deeply ("exceeded max depth").</exception>
    public static string ToJson(object? value)
    {
        return Encoder.Marshal(Normalize(value));
    }

    // Converts other primitive numeric types to long, double or BigInteger, copying only the containers that change.
    internal static object? Normalize(object? v)
    {
        Jq.EnsureStack();
        switch (v)
        {
            case null or bool or long or double or BigInteger or JsonNumber or string:
                return v;
            case int i:
                return (long)i;
            case short s:
                return (long)s;
            case sbyte sb:
                return (long)sb;
            case byte b:
                return (long)b;
            case ushort us:
                return (long)us;
            case uint ui:
                return (long)ui;
            case ulong ul:
                return ul <= long.MaxValue ? (long)ul : new BigInteger(ul);
            case float f:
                return (double)f;
            case decimal d:
                return (double)d;
            case List<object?> a:
            {
                List<object?>? copy = null;
                for (int i = 0; i < a.Count; i++)
                {
                    object? x = Normalize(a[i]);
                    if (copy == null && !ReferenceEquals(x, a[i]))
                    {
                        copy = new List<object?>(a);
                    }
                    if (copy != null)
                    {
                        copy[i] = x;
                    }
                }
                return copy ?? a;
            }
            case Dictionary<string, object?> m:
            {
                Dictionary<string, object?>? copy = null;
                foreach (KeyValuePair<string, object?> kv in m)
                {
                    object? x = Normalize(kv.Value);
                    if (!ReferenceEquals(x, kv.Value))
                    {
                        copy ??= new Dictionary<string, object?>(m);
                        copy[kv.Key] = x;
                    }
                }
                return copy ?? m;
            }
            default:
                throw new ArgumentException("invalid type: " + v.GetType() + "; arrays must be List<object?> and objects Dictionary<string, object?>");
        }
    }
}

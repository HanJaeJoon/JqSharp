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
    /// <exception cref="JqException">The text is not exactly one valid JSON value.</exception>
    public static object? FromJson(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }
        object? v = Jq.InternalFuncs["fromjson"].Callback!(json, Array.Empty<object?>());
        if (v is JqException err)
        {
            throw err;
        }
        return v;
    }

    /// <summary>
    /// Encodes a value as compact JSON, the way jq does: object keys are sorted, NaN is <c>null</c>,
    /// and infinities become the largest finite numbers.
    /// </summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>The JSON text.</returns>
    public static string ToJson(object? value)
    {
        return Encoder.Marshal(Normalize(value));
    }

    // Converts other primitive numeric types to long, double or BigInteger, copying only the containers that change.
    internal static object? Normalize(object? v)
    {
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
                return v;
        }
    }
}

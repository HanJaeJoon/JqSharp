using System.Text;

namespace JqSharp.Tests;

// The indenting encoder of the gojq command (cli/encoder.go), without colors.
internal static class PrettyEncoder
{
    public static string Encode(object? v, int indent, bool tab)
    {
        StringBuilder sb = new();
        Write(sb, v, indent, tab, 0);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, object? v, int indent, bool tab, int depth)
    {
        switch (v)
        {
            case List<object?> a:
                sb.Append('[');
                for (int i = 0; i < a.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    WriteIndent(sb, indent, tab, depth + indent);
                    Write(sb, a[i], indent, tab, depth + indent);
                }
                if (a.Count > 0)
                {
                    WriteIndent(sb, indent, tab, depth);
                }
                sb.Append(']');
                break;
            case Dictionary<string, object?> m:
                sb.Append('{');
                List<string> keys = Jq.Keys(m);
                for (int i = 0; i < keys.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    WriteIndent(sb, indent, tab, depth + indent);
                    Encoder.EncodeString(sb, keys[i]);
                    sb.Append(':');
                    if (indent >= 0)
                    {
                        sb.Append(' ');
                    }
                    Write(sb, m[keys[i]], indent, tab, depth + indent);
                }
                if (keys.Count > 0)
                {
                    WriteIndent(sb, indent, tab, depth);
                }
                sb.Append('}');
                break;
            default:
                sb.Append(Encoder.Marshal(v));
                break;
        }
    }

    private static void WriteIndent(StringBuilder sb, int indent, bool tab, int depth)
    {
        if (indent < 0)
        {
            return;
        }
        sb.Append('\n').Append(tab ? '\t' : ' ', depth);
    }
}

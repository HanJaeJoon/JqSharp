using System.Numerics;

namespace JqSharp;

internal static partial class Jq
{
    // Compares l and r in jq order: null < false < true < numbers < strings < arrays < objects.
    internal static int Compare(object? l, object? r)
    {
        return BinopTypeSwitch(l, r,
            static (l, r) => l.CompareTo(r),
            static (l, r) =>
            {
                if (Lt(l, r))
                {
                    return -1;
                }
                return l == r ? 0 : 1;
            },
            static (l, r) => l.CompareTo(r),
            static (l, r) => CompareStrings(l, r),
            static (l, r) =>
            {
                int n = Math.Min(l.Count, r.Count);
                for (int i = 0; i < n; i++)
                {
                    int c = Compare(l[i], r[i]);
                    if (c != 0)
                    {
                        return c;
                    }
                }
                return l.Count.CompareTo(r.Count);
            },
            static (l, r) =>
            {
                List<string> lk = Keys(l);
                List<string> rk = Keys(r);
                int n = Math.Min(lk.Count, rk.Count);
                for (int i = 0; i < n; i++)
                {
                    int c = CompareStrings(lk[i], rk[i]);
                    if (c != 0)
                    {
                        return c;
                    }
                }
                if (lk.Count != rk.Count)
                {
                    return lk.Count.CompareTo(rk.Count);
                }
                foreach (string k in lk)
                {
                    int c = Compare(l[k], r[k]);
                    if (c != 0)
                    {
                        return c;
                    }
                }
                return 0;
            },
            static (l, r) => TypeIndex(l).CompareTo(TypeIndex(r)));
    }

    internal static bool Lt(double l, double r)
    {
        return l < r || double.IsNaN(l);
    }

    private static int TypeIndex(object? v)
    {
        return v switch
        {
            false => 1,
            true => 2,
            long or double or BigInteger or JsonNumber => 3,
            string => 4,
            List<object?> => 5,
            Dictionary<string, object?> => 6,
            _ => 0,
        };
    }

    // Compares strings by code point, which is the byte order of their UTF-8 encodings (Go's strings.Compare).
    internal static int CompareStrings(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            char x = a[i];
            char y = b[i];
            if (x != y)
            {
                if (x >= 0xD800 && y >= 0xD800)
                {
                    return FixupSurrogateOrder(x).CompareTo(FixupSurrogateOrder(y));
                }
                return x.CompareTo(y);
            }
        }
        return a.Length.CompareTo(b.Length);
    }

    private static int FixupSurrogateOrder(char c)
    {
        return c >= 0xE000 ? c - 0x800 : c + 0x2000;
    }

    internal static readonly IComparer<string> StringComparer = Comparer<string>.Create(CompareStrings);
}

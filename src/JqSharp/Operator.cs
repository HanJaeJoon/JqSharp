using System.Numerics;

namespace JqSharp;

internal enum Operator
{
    Pipe = 1,
    Comma,
    Add,
    Sub,
    Mul,
    Div,
    Mod,
    Eq,
    Ne,
    Gt,
    Lt,
    Ge,
    Le,
    And,
    Or,
    Alt,
    Assign,
    Modify,
    UpdateAdd,
    UpdateSub,
    UpdateMul,
    UpdateDiv,
    UpdateMod,
    UpdateAlt,
}

internal static class OperatorExtensions
{
    public static string ToToken(this Operator op)
    {
        return op switch
        {
            Operator.Pipe => "|",
            Operator.Comma => ",",
            Operator.Add => "+",
            Operator.Sub => "-",
            Operator.Mul => "*",
            Operator.Div => "/",
            Operator.Mod => "%",
            Operator.Eq => "==",
            Operator.Ne => "!=",
            Operator.Gt => ">",
            Operator.Lt => "<",
            Operator.Ge => ">=",
            Operator.Le => "<=",
            Operator.And => "and",
            Operator.Or => "or",
            Operator.Alt => "//",
            Operator.Assign => "=",
            Operator.Modify => "|=",
            Operator.UpdateAdd => "+=",
            Operator.UpdateSub => "-=",
            Operator.UpdateMul => "*=",
            Operator.UpdateDiv => "/=",
            Operator.UpdateMod => "%=",
            Operator.UpdateAlt => "//=",
            _ => throw new InvalidOperationException("invalid operator: " + (int)op),
        };
    }

    public static string GetFunc(this Operator op)
    {
        return op switch
        {
            Operator.Add or Operator.UpdateAdd => "_add",
            Operator.Sub or Operator.UpdateSub => "_subtract",
            Operator.Mul or Operator.UpdateMul => "_multiply",
            Operator.Div or Operator.UpdateDiv => "_divide",
            Operator.Mod or Operator.UpdateMod => "_modulo",
            Operator.Eq => "_equal",
            Operator.Ne => "_notequal",
            Operator.Gt => "_greater",
            Operator.Lt => "_less",
            Operator.Ge => "_greatereq",
            Operator.Le => "_lesseq",
            Operator.Assign => "_assign",
            Operator.Modify => "_modify",
            Operator.UpdateAlt => "_alternative",
            _ => throw new InvalidOperationException("invalid operator: " + (int)op),
        };
    }
}

internal static partial class Jq
{
    internal static T BinopTypeSwitch<T>(
        object? l,
        object? r,
        Func<long, long, T> callbackInts,
        Func<double, double, T> callbackFloats,
        Func<BigInteger, BigInteger, T> callbackBigInts,
        Func<string, string, T> callbackStrings,
        Func<List<object?>, List<object?>, T> callbackArrays,
        Func<Dictionary<string, object?>, Dictionary<string, object?>, T> callbackMaps,
        Func<object?, object?, T> fallback)
    {
        if (l is JsonNumber ln)
        {
            l = ParseNumber(ln);
        }
        if (r is JsonNumber rn)
        {
            r = ParseNumber(rn);
        }
        switch (l)
        {
            case long li:
                return r switch
                {
                    long ri => callbackInts(li, ri),
                    double rf => callbackFloats(li, rf),
                    BigInteger rb => callbackBigInts(li, rb),
                    _ => fallback(l, r),
                };
            case double lf:
                return r switch
                {
                    long ri => callbackFloats(lf, ri),
                    double rf => callbackFloats(lf, rf),
                    BigInteger rb => callbackFloats(lf, BigToFloat(rb)),
                    _ => fallback(l, r),
                };
            case BigInteger lb:
                return r switch
                {
                    long ri => callbackBigInts(lb, ri),
                    double rf => callbackFloats(BigToFloat(lb), rf),
                    BigInteger rb => callbackBigInts(lb, rb),
                    _ => fallback(l, r),
                };
            case string ls:
                return r is string rs ? callbackStrings(ls, rs) : fallback(l, r);
            case List<object?> la:
                return r is List<object?> ra ? callbackArrays(la, ra) : fallback(l, r);
            case Dictionary<string, object?> lm:
                return r is Dictionary<string, object?> rm ? callbackMaps(lm, rm) : fallback(l, r);
            default:
                return fallback(l, r);
        }
    }

    internal static object? FuncOpPlus(object? v)
    {
        return v switch
        {
            long or double or BigInteger or JsonNumber => v,
            _ => new UnaryTypeError("plus", v),
        };
    }

    private static object Negate(long v)
    {
        if (v == long.MinValue)
        {
            return -new BigInteger(v);
        }
        return -v;
    }

    internal static object? FuncOpNegate(object? v)
    {
        switch (v)
        {
            case long i:
                return Negate(i);
            case double f:
                return -f;
            case BigInteger b:
                return -b;
            case JsonNumber n:
                return n.Value.StartsWith("-", StringComparison.Ordinal) ? new JsonNumber(n.Value.Substring(1)) : new JsonNumber("-" + n.Value);
            default:
                return new UnaryTypeError("negate", v);
        }
    }

    internal static object? FuncOpAdd(object? l, object? r)
    {
        return BinopTypeSwitch<object?>(l, r,
            static (l, r) =>
            {
                long v = unchecked(l + r);
                if ((v >= l) == (r >= 0))
                {
                    return v;
                }
                return new BigInteger(l) + r;
            },
            static (l, r) => l + r,
            static (l, r) => l + r,
            static (l, r) => l + r,
            static (l, r) =>
            {
                if (l.Count == 0)
                {
                    return r;
                }
                if (r.Count == 0)
                {
                    return l;
                }
                List<object?> v = new(l.Count + r.Count);
                v.AddRange(l);
                v.AddRange(r);
                return v;
            },
            static (l, r) =>
            {
                if (l.Count == 0)
                {
                    return r;
                }
                if (r.Count == 0)
                {
                    return l;
                }
                Dictionary<string, object?> m = new(l);
                foreach (KeyValuePair<string, object?> kv in r)
                {
                    m[kv.Key] = kv.Value;
                }
                return m;
            },
            static (l, r) =>
            {
                if (l == null)
                {
                    return r;
                }
                if (r == null)
                {
                    return l;
                }
                return new BinopTypeError("add", l, r);
            });
    }

    internal static object? FuncOpSub(object? l, object? r)
    {
        return BinopTypeSwitch<object?>(l, r,
            static (l, r) =>
            {
                long v = unchecked(l - r);
                if ((v <= l) == (r >= 0))
                {
                    return v;
                }
                return new BigInteger(l) - r;
            },
            static (l, r) => l - r,
            static (l, r) => l - r,
            static (l, r) => new BinopTypeError("subtract", l, r),
            static (l, r) =>
            {
                List<object?> v = new(l.Count);
                foreach (object? x in l)
                {
                    bool found = false;
                    foreach (object? y in r)
                    {
                        if (Compare(x, y) == 0)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        v.Add(x);
                    }
                }
                return v;
            },
            static (l, r) => new BinopTypeError("subtract", l, r),
            static (l, r) => new BinopTypeError("subtract", l, r));
    }

    internal static object? FuncOpMul(object? l, object? r)
    {
        return BinopTypeSwitch<object?>(l, r,
            static (l, r) =>
            {
                if (r == -1)
                {
                    return Negate(l);
                }
                long v = unchecked(l * r);
                if (r == 0 || v / r == l)
                {
                    return v;
                }
                return new BigInteger(l) * r;
            },
            static (l, r) => l * r,
            static (l, r) => l * r,
            static (l, r) => new BinopTypeError("multiply", l, r),
            static (l, r) => new BinopTypeError("multiply", l, r),
            static (l, r) => DeepMergeObjects(l, r),
            static (l, r) =>
            {
                if (l is string ls && ToFloat64(r, out double rf))
                {
                    return RepeatString(ls, rf);
                }
                if (r is string rs && ToFloat64(l, out double lf))
                {
                    return RepeatString(rs, lf);
                }
                return new BinopTypeError("multiply", l, r);
            });
    }

    private static Dictionary<string, object?> DeepMergeObjects(Dictionary<string, object?> l, Dictionary<string, object?> r)
    {
        EnsureStack();
        Dictionary<string, object?> m = new(l);
        foreach (KeyValuePair<string, object?> kv in r)
        {
            object? v = kv.Value;
            if (m.TryGetValue(kv.Key, out object? mk) && mk is Dictionary<string, object?> mm && v is Dictionary<string, object?> w)
            {
                v = DeepMergeObjects(mm, w);
            }
            m[kv.Key] = v;
        }
        return m;
    }

    private static object? RepeatString(string s, double n)
    {
        if (Lt(n, 0))
        {
            return null;
        }
        int c = (int)Math.Min(n, int.MaxValue);
        if ((ulong)Utf8Length(s) * (ulong)c >= int.MaxValue)
        {
            return new RepeatStringTooLargeError(s, n);
        }
        System.Text.StringBuilder sb = new(s.Length * c);
        for (int i = 0; i < c; i++)
        {
            sb.Append(s);
        }
        return sb.ToString();
    }

    internal static object? FuncOpDiv(object? l, object? r)
    {
        return BinopTypeSwitch<object?>(l, r,
            static (l, r) =>
            {
                switch (r)
                {
                    case 0:
                        return new ZeroDivisionError(l, r);
                    case -1:
                        return Negate(l);
                    default:
                        if (l % r == 0)
                        {
                            return l / r;
                        }
                        return (double)l / r;
                }
            },
            static (l, r) =>
            {
                if (r == 0.0)
                {
                    return new ZeroDivisionError(l, r);
                }
                return l / r;
            },
            static (l, r) =>
            {
                if (r.Sign == 0)
                {
                    return new ZeroDivisionError(l, r);
                }
                BigInteger d = BigInteger.DivRem(l, r, out BigInteger m);
                if (m.Sign == 0)
                {
                    return d;
                }
                return BigToFloat(l) / BigToFloat(r);
            },
            static (l, r) => SplitString(l, r),
            static (l, r) => new BinopTypeError("divide", l, r),
            static (l, r) => new BinopTypeError("divide", l, r),
            static (l, r) => new BinopTypeError("divide", l, r));
    }

    internal static object? FuncOpMod(object? l, object? r)
    {
        return BinopTypeSwitch<object?>(l, r,
            static (l, r) =>
            {
                switch (r)
                {
                    case 0:
                        return new ZeroModuloError(l, r);
                    case -1:
                        return 0L;
                    default:
                        return l % r;
                }
            },
            static (l, r) =>
            {
                if (double.IsNaN(l) || double.IsNaN(r))
                {
                    return double.NaN;
                }
                long ri = FloatToInt(r);
                if (ri == 0)
                {
                    return new ZeroModuloError(l, r);
                }
                long li = FloatToInt(l);
                return ri == -1 ? 0L : li % ri;
            },
            static (l, r) =>
            {
                if (r.Sign == 0)
                {
                    return new ZeroModuloError(l, r);
                }
                return BigInteger.Remainder(l, r);
            },
            static (l, r) => new BinopTypeError("modulo", l, r),
            static (l, r) => new BinopTypeError("modulo", l, r),
            static (l, r) => new BinopTypeError("modulo", l, r),
            static (l, r) => new BinopTypeError("modulo", l, r));
    }

    internal static object? FuncOpAlt(object? l, object? r)
    {
        return l == null || l is false ? r : l;
    }

    internal static object? FuncOpEq(object? l, object? r) => Compare(l, r) == 0;

    internal static object? FuncOpNe(object? l, object? r) => Compare(l, r) != 0;

    internal static object? FuncOpGt(object? l, object? r) => Compare(l, r) > 0;

    internal static object? FuncOpLt(object? l, object? r) => Compare(l, r) < 0;

    internal static object? FuncOpGe(object? l, object? r) => Compare(l, r) >= 0;

    internal static object? FuncOpLe(object? l, object? r) => Compare(l, r) <= 0;
}

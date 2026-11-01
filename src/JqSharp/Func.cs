using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace JqSharp;

// An iterator a function can return to emit several values (gojq's Iter).
internal interface IIter
{
    bool Next(out object? value);
}

internal sealed class Function(int argCount, bool iter, Func<object?, object?[], object?>? callback)
{
    public int ArgCount { get; } = argCount;

    public bool Iter { get; } = iter;

    public Func<object?, object?[], object?>? Callback { get; } = callback;

    public bool Accept(int cnt) => (ArgCount & (1 << cnt)) != 0;
}

// The builtins implemented natively (gojq func.go).
internal static partial class Jq
{
    private const int ArgCount0 = 1;
    private const int ArgCount1 = 2;
    private const int ArgCount2 = 4;
    private const int ArgCount3 = 8;

    private static readonly string[] HtmlEscapes = { "<", "&lt;", ">", "&gt;", "&", "&amp;", "'", "&apos;", "\"", "&quot;" };
    private static readonly string[] CsvEscapes = { "\"", "\"\"", "\0", "\\0" };
    private static readonly string[] TsvEscapes = { "\t", "\\t", "\r", "\\r", "\n", "\\n", "\\", "\\\\", "\0", "\\0" };
    private static readonly string[] ShEscapes = { "'", "'\\''", "\0", "\\0" };

    // Placeholder for the values delpaths removes.
    internal static readonly object Empty = new();

    internal static readonly Dictionary<string, Function> InternalFuncs = new()
    {
        ["empty"] = ArgFunc0(null),
        ["path"] = ArgFunc1(null),
        ["env"] = ArgFunc0(null),
        ["builtins"] = ArgFunc0(null),
        ["input"] = ArgFunc0(null),
        ["modulemeta"] = ArgFunc0(null),
        ["debug"] = ArgFunc1(null),
        ["length"] = ArgFunc0(FuncLength),
        ["utf8bytelength"] = ArgFunc0(FuncUtf8ByteLength),
        ["keys"] = ArgFunc0(FuncKeys),
        ["has"] = ArgFunc1(FuncHas),
        ["add"] = ArgFunc0(FuncAdd),
        ["toboolean"] = ArgFunc0(FuncToBoolean),
        ["tonumber"] = ArgFunc0(FuncToNumber),
        ["tostring"] = ArgFunc0(static v => ToStr(v)),
        ["type"] = ArgFunc0(static v => TypeOf(v)),
        ["reverse"] = ArgFunc0(FuncReverse),
        ["contains"] = ArgFunc1(FuncContains),
        ["inside"] = ArgFunc1(static (v, x) => FuncContains(x, v)),
        ["indices"] = ArgFunc1(static (v, x) => IndexFunc("indices", v, x, Indices)),
        ["index"] = ArgFunc1(static (v, x) => IndexFunc("index", v, x, IndexOf)),
        ["rindex"] = ArgFunc1(static (v, x) => IndexFunc("rindex", v, x, RindexOf)),
        ["startswith"] = ArgFunc1(static (v, x) => StringFunc1("startswith", v, x, static (s, t) => s.StartsWith(t, StringComparison.Ordinal))),
        ["endswith"] = ArgFunc1(static (v, x) => StringFunc1("endswith", v, x, static (s, t) => s.EndsWith(t, StringComparison.Ordinal))),
        ["ltrimstr"] = ArgFunc1(static (v, x) => StringFunc1("ltrimstr", v, x, static (s, t) => TrimPrefix(s, t))),
        ["rtrimstr"] = ArgFunc1(static (v, x) => StringFunc1("rtrimstr", v, x, static (s, t) => TrimSuffix(s, t))),
        ["trimstr"] = ArgFunc1(static (v, x) => StringFunc1("trimstr", v, x, static (s, t) => TrimSuffix(TrimPrefix(s, t), t))),
        ["ltrim"] = ArgFunc0(static v => v is string s ? s.TrimStart() : new Func0TypeError("ltrim", v)),
        ["rtrim"] = ArgFunc0(static v => v is string s ? s.TrimEnd() : new Func0TypeError("rtrim", v)),
        ["trim"] = ArgFunc0(static v => v is string s ? s.Trim() : new Func0TypeError("trim", v)),
        ["explode"] = ArgFunc0(static v => v is string s ? Explode(s) : new Func0TypeError("explode", v)),
        ["implode"] = ArgFunc0(FuncImplode),
        ["split"] = ArgFunc1(static (v, x) => StringFunc1("split", v, x, static (s, t) => SplitString(s, t))),
        ["join"] = ArgFunc1(FuncJoin),
        ["ascii_downcase"] = ArgFunc0(static v => v is string s ? MapAscii(s, 'A', 'Z', 'a' - 'A') : new Func0TypeError("ascii_downcase", v)),
        ["ascii_upcase"] = ArgFunc0(static v => v is string s ? MapAscii(s, 'a', 'z', 'A' - 'a') : new Func0TypeError("ascii_upcase", v)),
        ["tojson"] = ArgFunc0(static v => Encoder.Marshal(v)),
        ["fromjson"] = ArgFunc0(FuncFromJson),
        ["format"] = ArgFunc1(FuncFormat),
        ["_tohtml"] = ArgFunc0(FuncToHtml),
        ["_touri"] = ArgFunc0(FuncToUri),
        ["_tourid"] = ArgFunc0(FuncToUrid),
        ["_tocsv"] = ArgFunc0(static v => FormatJoin("csv", v, ",", static s => "\"" + Replace(s, CsvEscapes) + "\"")),
        ["_totsv"] = ArgFunc0(static v => FormatJoin("tsv", v, "\t", static s => Replace(s, TsvEscapes))),
        ["_tosh"] = ArgFunc0(static v => FormatJoin("sh", v is List<object?> ? v : new List<object?> { v }, " ", static s => "'" + Replace(s, ShEscapes) + "'")),
        ["_tobase64"] = ArgFunc0(static v => Convert.ToBase64String(Encoding.UTF8.GetBytes(ToStr(v)))),
        ["_tobase64d"] = ArgFunc0(FuncToBase64d),
        ["_index"] = ArgOp2(FuncIndex2),
        ["_slice"] = new Function(ArgCount3, false, static (_, args) => FuncSlice(args[0], args[1], args[2])),
        ["_plus"] = ArgFunc0(FuncOpPlus),
        ["_negate"] = ArgFunc0(FuncOpNegate),
        ["_add"] = ArgOp2(FuncOpAdd),
        ["_subtract"] = ArgOp2(FuncOpSub),
        ["_multiply"] = ArgOp2(FuncOpMul),
        ["_divide"] = ArgOp2(FuncOpDiv),
        ["_modulo"] = ArgOp2(FuncOpMod),
        ["_alternative"] = ArgOp2(FuncOpAlt),
        ["_equal"] = ArgOp2(FuncOpEq),
        ["_notequal"] = ArgOp2(FuncOpNe),
        ["_greater"] = ArgOp2(FuncOpGt),
        ["_less"] = ArgOp2(FuncOpLt),
        ["_greatereq"] = ArgOp2(FuncOpGe),
        ["_lesseq"] = ArgOp2(FuncOpLe),
        ["flatten"] = new Function(ArgCount0 | ArgCount1, false, FuncFlatten),
        ["_range"] = new Function(ArgCount3, true, FuncRange),
        ["min"] = ArgFunc0(static v => v is List<object?> vs ? MinMaxBy(vs, vs, true) : new Func0TypeError("min", v)),
        ["_min_by"] = ArgFunc1(static (v, x) => MinMaxByFunc("min_by", v, x, true)),
        ["max"] = ArgFunc0(static v => v is List<object?> vs ? MinMaxBy(vs, vs, false) : new Func0TypeError("max", v)),
        ["_max_by"] = ArgFunc1(static (v, x) => MinMaxByFunc("max_by", v, x, false)),
        ["sort"] = ArgFunc0(static v => SortBy("sort", v, v)),
        ["_sort_by"] = ArgFunc1(static (v, x) => SortBy("sort_by", v, x)),
        ["_group_by"] = ArgFunc1(FuncGroupBy),
        ["unique"] = ArgFunc0(static v => UniqueBy("unique", v, v)),
        ["_unique_by"] = ArgFunc1(static (v, x) => UniqueBy("unique_by", v, x)),
        ["sin"] = MathFunc("sin", Math.Sin),
        ["cos"] = MathFunc("cos", Math.Cos),
        ["tan"] = MathFunc("tan", Math.Tan),
        ["asin"] = MathFunc("asin", Math.Asin),
        ["acos"] = MathFunc("acos", Math.Acos),
        ["atan"] = MathFunc("atan", Math.Atan),
        ["sinh"] = MathFunc("sinh", Math.Sinh),
        ["cosh"] = MathFunc("cosh", Math.Cosh),
        ["tanh"] = MathFunc("tanh", Math.Tanh),
        ["asinh"] = MathFunc("asinh", GoMath.Asinh),
        ["acosh"] = MathFunc("acosh", GoMath.Acosh),
        ["atanh"] = MathFunc("atanh", GoMath.Atanh),
        ["floor"] = MathFunc("floor", static x => KeepZeroSign(Math.Floor(x), x)),
        ["round"] = MathFunc("round", static x => KeepZeroSign(Math.Round(x, MidpointRounding.AwayFromZero), x)),
        ["nearbyint"] = MathFunc("nearbyint", static x => KeepZeroSign(Math.Round(x, MidpointRounding.ToEven), x)),
        ["rint"] = MathFunc("rint", static x => KeepZeroSign(Math.Round(x, MidpointRounding.ToEven), x)),
        ["ceil"] = MathFunc("ceil", static x => KeepZeroSign(Math.Ceiling(x), x)),
        ["trunc"] = MathFunc("trunc", static x => KeepZeroSign(Math.Truncate(x), x)),
        ["significand"] = MathFunc("significand", static x => GoMath.Frexp(x, out _) * 2),
        ["fabs"] = MathFunc("fabs", Math.Abs),
        ["sqrt"] = MathFunc("sqrt", Math.Sqrt),
        ["cbrt"] = MathFunc("cbrt", GoMath.Cbrt),
        ["exp"] = MathFunc("exp", Math.Exp),
        ["exp10"] = MathFunc("exp10", static x => GoMath.Pow(10, x)),
        ["exp2"] = MathFunc("exp2", GoMath.Exp2),
        ["expm1"] = MathFunc("expm1", GoMath.Expm1),
        ["frexp"] = ArgFunc0(FuncFrexp),
        ["modf"] = ArgFunc0(FuncModf),
        ["log"] = MathFunc("log", Math.Log),
        ["log10"] = MathFunc("log10", static x => Math.Log(x) * InvLn10),
        ["log1p"] = MathFunc("log1p", GoMath.Log1p),
        ["log2"] = MathFunc("log2", GoMath.Log2),
        ["logb"] = MathFunc("logb", GoMath.Logb),
        ["gamma"] = MathFunc("gamma", GoMath.Gamma),
        ["tgamma"] = MathFunc("tgamma", GoMath.Gamma),
        ["lgamma"] = MathFunc("lgamma", static x => GoMath.Lgamma(x, out _)),
        ["erf"] = MathFunc("erf", GoMath.Erf),
        ["erfc"] = MathFunc("erfc", GoMath.Erfc),
        ["j0"] = MathFunc("j0", GoMath.J0),
        ["j1"] = MathFunc("j1", GoMath.J1),
        ["y0"] = MathFunc("y0", GoMath.Y0),
        ["y1"] = MathFunc("y1", GoMath.Y1),
        ["atan2"] = MathFunc2("atan2", Math.Atan2),
        ["copysign"] = MathFunc2("copysign", GoMath.Copysign),
        ["drem"] = MathFunc2("drem", FuncDrem),
        ["fdim"] = MathFunc2("fdim", GoMath.Dim),
        ["fmax"] = MathFunc2("fmax", static (l, r) => double.IsNaN(l) ? r : double.IsNaN(r) ? l : Math.Max(l, r)),
        ["fmin"] = MathFunc2("fmin", static (l, r) => double.IsNaN(l) ? r : double.IsNaN(r) ? l : Math.Min(l, r)),
        ["fmod"] = MathFunc2("fmod", static (l, r) => l % r),
        ["hypot"] = MathFunc2("hypot", GoMath.Hypot),
        ["jn"] = MathFunc2("jn", FuncJn),
        ["nextafter"] = MathFunc2("nextafter", GoMath.Nextafter),
        ["nexttoward"] = MathFunc2("nexttoward", GoMath.Nextafter),
        ["remainder"] = MathFunc2("remainder", GoMath.Remainder),
        ["ldexp"] = MathFunc2("ldexp", FuncLdexp),
        ["scalb"] = MathFunc2("scalb", FuncLdexp),
        ["scalbln"] = MathFunc2("scalbln", FuncLdexp),
        ["yn"] = MathFunc2("yn", FuncYn),
        ["pow"] = MathFunc2("pow", GoMath.Pow),
        ["fma"] = MathFunc3("fma", GoMath.FMA),
        ["infinite"] = ArgFunc0(static _ => double.PositiveInfinity),
        ["isfinite"] = ArgFunc0(static v => ToFloat64(v, out double x) && !double.IsInfinity(x)),
        ["isinfinite"] = ArgFunc0(static v => ToFloat64(v, out double x) && double.IsInfinity(x)),
        ["nan"] = ArgFunc0(static _ => double.NaN),
        ["isnan"] = ArgFunc0(static v => ToFloat64(v, out double x) && double.IsNaN(x)),
        ["isnormal"] = ArgFunc0(FuncIsnormal),
        ["setpath"] = ArgFunc2(static (v, p, n) => Setpath(v, p, n, null)),
        ["delpaths"] = ArgFunc1(static (v, p) => Delpaths(v, p, new Allocator())),
        ["getpath"] = ArgFunc1(FuncGetpath),
        ["transpose"] = ArgFunc0(FuncTranspose),
        ["bsearch"] = ArgFunc1(FuncBsearch),
        ["gmtime"] = ArgFunc0(static v => TimeToArrayFunc("gmtime", v, null)),
        ["localtime"] = ArgFunc0(static v => TimeToArrayFunc("localtime", v, GoTime.Local)),
        ["mktime"] = ArgFunc0(FuncMktime),
        ["strftime"] = ArgFunc1(static (v, x) => FormatTimeFunc("strftime", v, x, null)),
        ["strflocaltime"] = ArgFunc1(static (v, x) => FormatTimeFunc("strflocaltime", v, x, GoTime.Local)),
        ["strptime"] = ArgFunc1(FuncStrptime),
        ["now"] = ArgFunc0(static _ => TimeToEpoch(GoTime.Now())),
        ["_match"] = ArgFunc3(null),
        ["_captures"] = ArgFunc0(FuncCaptures),
        ["error"] = new Function(ArgCount0 | ArgCount1, false, static (v, args) => new JqValueException(args.Length > 0 ? args[0] : v, 5)),
        ["halt"] = ArgFunc0(static _ => new JqHaltException(null, 0)),
        ["halt_error"] = new Function(ArgCount0 | ArgCount1, false, FuncHaltError),
    };

    // Go folds 1/Ln10 exactly; this is that constant rounded to a double.
    private const double InvLn10 = 0.43429448190325182765;

    // Go keeps the sign of a zero result (ceil(-0.5) is -0); .NET Framework does not.
    private static double KeepZeroSign(double r, double x)
    {
        return r == 0 ? GoMath.Copysign(0, x) : r;
    }

    private static Function ArgFunc0(Func<object?, object?>? f)
    {
        return new Function(ArgCount0, false, f == null ? null : (v, _) => f(v));
    }

    private static Function ArgFunc1(Func<object?, object?, object?>? f)
    {
        return new Function(ArgCount1, false, f == null ? null : (v, args) => f(v, args[0]));
    }

    private static Function ArgOp2(Func<object?, object?, object?> f)
    {
        return new Function(ArgCount2, false, (_, args) => f(args[0], args[1]));
    }

    private static Function ArgFunc2(Func<object?, object?, object?, object?> f)
    {
        return new Function(ArgCount2, false, (v, args) => f(v, args[0], args[1]));
    }

    private static Function ArgFunc3(Func<object?, object?, object?, object?, object?>? f)
    {
        return new Function(ArgCount3, false, f == null ? null : (v, args) => f(v, args[0], args[1], args[2]));
    }

    private static Function MathFunc(string name, Func<double, double> f)
    {
        return ArgFunc0(v => ToFloat64(v, out double x) ? f(x) : new Func0TypeError(name, v));
    }

    private static Function MathFunc2(string name, Func<double, double, double> f)
    {
        return ArgFunc2((_, x, y) =>
        {
            if (!ToFloat64(x, out double l))
            {
                return new Func0TypeError(name, x);
            }
            if (!ToFloat64(y, out double r))
            {
                return new Func0TypeError(name, y);
            }
            return f(l, r);
        });
    }

    private static Function MathFunc3(string name, Func<double, double, double, double> f)
    {
        return ArgFunc3((_, a, b, c) =>
        {
            if (!ToFloat64(a, out double x))
            {
                return new Func0TypeError(name, a);
            }
            if (!ToFloat64(b, out double y))
            {
                return new Func0TypeError(name, b);
            }
            if (!ToFloat64(c, out double z))
            {
                return new Func0TypeError(name, c);
            }
            return f(x, y, z);
        });
    }

    private static object? FuncLength(object? v)
    {
        switch (v)
        {
            case null:
                return 0L;
            case long i:
                return i >= 0 ? i : Negate(i);
            case double f:
                return Math.Abs(f);
            case BigInteger b:
                return b.Sign >= 0 ? b : BigInteger.Abs(b);
            case JsonNumber n:
                return n.Value.StartsWith("-", StringComparison.Ordinal) ? new JsonNumber(n.Value.Substring(1)) : n;
            case string s:
                return (long)CodePointCount(s);
            case List<object?> a:
                return (long)a.Count;
            case Dictionary<string, object?> m:
                return (long)m.Count;
            default:
                return new Func0TypeError("length", v);
        }
    }

    private static object? FuncUtf8ByteLength(object? v)
    {
        return v is string s ? (long)Utf8Length(s) : new Func0TypeError("utf8bytelength", v);
    }

    internal static object? FuncKeys(object? v)
    {
        switch (v)
        {
            case List<object?> a:
            {
                List<object?> w = new(a.Count);
                for (int i = 0; i < a.Count; i++)
                {
                    w.Add((long)i);
                }
                return w;
            }
            case Dictionary<string, object?> m:
            {
                List<object?> w = new(m.Count);
                foreach (string k in Keys(m))
                {
                    w.Add(k);
                }
                return w;
            }
            default:
                return new Func0TypeError("keys", v);
        }
    }

    internal static List<string> Keys(Dictionary<string, object?> v)
    {
        List<string> w = new(v.Keys);
        w.Sort(StringComparer);
        return w;
    }

    private static bool Values(object? v, out List<object?> vs)
    {
        switch (v)
        {
            case List<object?> a:
                vs = a;
                return true;
            case Dictionary<string, object?> m:
                vs = new List<object?>(m.Count);
                foreach (string k in Keys(m))
                {
                    vs.Add(m[k]);
                }
                return true;
            default:
                vs = null!;
                return false;
        }
    }

    private static object? FuncHas(object? v, object? x)
    {
        switch (v)
        {
            case List<object?> a:
                if (ToInt(x, out long i))
                {
                    return 0 <= i && i < a.Count;
                }
                break;
            case Dictionary<string, object?> m:
                if (x is string k)
                {
                    return m.ContainsKey(k);
                }
                break;
            case null:
                return false;
        }
        return new Func1TypeError("has", v, x);
    }

    private static object? FuncAdd(object? v)
    {
        if (!Values(v, out List<object?> vs))
        {
            return new Func0TypeError("add", v);
        }
        return Add(vs);
    }

    private static object? Add(IEnumerable<object?> xs)
    {
        object? v = null;
        StringBuilder? sb = null;
        foreach (object? x in xs)
        {
            switch (x)
            {
                case null:
                    continue;
                case string s:
                    if (sb != null)
                    {
                        sb.Append(s);
                        continue;
                    }
                    if (v == null)
                    {
                        sb = new StringBuilder(s);
                        continue;
                    }
                    break;
                case List<object?> a:
                    if (sb == null)
                    {
                        if (v == null)
                        {
                            v = new List<object?>(a);
                            continue;
                        }
                        if (v is List<object?> w)
                        {
                            w.AddRange(a);
                            continue;
                        }
                    }
                    break;
                case Dictionary<string, object?> m:
                    if (sb == null)
                    {
                        if (v == null)
                        {
                            v = new Dictionary<string, object?>(m);
                            continue;
                        }
                        if (v is Dictionary<string, object?> w)
                        {
                            foreach (KeyValuePair<string, object?> kv in m)
                            {
                                w[kv.Key] = kv.Value;
                            }
                            continue;
                        }
                    }
                    break;
            }
            if (sb != null)
            {
                v = sb.ToString();
                sb = null;
            }
            v = FuncOpAdd(v, x);
            if (v is JqException)
            {
                return v;
            }
        }
        return sb != null ? sb.ToString() : v;
    }

    private static object? FuncToBoolean(object? v)
    {
        switch (v)
        {
            case bool:
                return v;
            case string s:
                return s switch
                {
                    "true" => true,
                    "false" => false,
                    _ => new Func0WrapError("toboolean", v, new SimpleError("invalid boolean")),
                };
            default:
                return new Func0TypeError("toboolean", v);
        }
    }

    private static object? FuncToNumber(object? v)
    {
        switch (v)
        {
            case long or double or BigInteger or JsonNumber:
                return v;
            case string s:
                if (!new Lexer(s).ValidNumber())
                {
                    return new Func0WrapError("tonumber", v, new SimpleError("invalid number"));
                }
                return ToNumber(s);
            default:
                return new Func0TypeError("tonumber", v);
        }
    }

    internal static string ToStr(object? v)
    {
        return v as string ?? Encoder.Marshal(v);
    }

    private static object? FuncReverse(object? v)
    {
        if (v is not List<object?> vs)
        {
            return new Func0TypeError("reverse", v);
        }
        List<object?> ws = new(vs);
        ws.Reverse();
        return ws;
    }

    private static object? FuncContains(object? v, object? x)
    {
        return BinopTypeSwitch<object?>(v, x,
            static (l, r) => l == r,
            static (l, r) => l == r,
            static (l, r) => l == r,
            static (l, r) => l.Contains(r),
            static (l, r) =>
            {
                foreach (object? y in r)
                {
                    bool found = false;
                    foreach (object? z in l)
                    {
                        if (FuncContains(z, y) is true)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        return false;
                    }
                }
                return true;
            },
            static (l, r) =>
            {
                if (l.Count < r.Count)
                {
                    return false;
                }
                foreach (KeyValuePair<string, object?> kv in r)
                {
                    if (!l.TryGetValue(kv.Key, out object? w) || FuncContains(w, kv.Value) is not true)
                    {
                        return false;
                    }
                }
                return true;
            },
            static (l, r) => Equals(l, r) ? true : new Func1TypeError("contains", l, r));
    }

    private static bool EqualAt(List<object?> vs, int i, List<object?> xs)
    {
        for (int j = 0; j < xs.Count; j++)
        {
            if (Compare(vs[i + j], xs[j]) != 0)
            {
                return false;
            }
        }
        return true;
    }

    private static object? Indices(List<object?> vs, List<object?> xs)
    {
        List<object?> rs = new();
        if (xs.Count == 0)
        {
            return rs;
        }
        for (int i = 0; i < vs.Count - xs.Count + 1; i++)
        {
            if (EqualAt(vs, i, xs))
            {
                rs.Add((long)i);
            }
        }
        return rs;
    }

    private static object? IndexOf(List<object?> vs, List<object?> xs)
    {
        if (xs.Count == 0)
        {
            return null;
        }
        for (int i = 0; i < vs.Count - xs.Count + 1; i++)
        {
            if (EqualAt(vs, i, xs))
            {
                return (long)i;
            }
        }
        return null;
    }

    private static object? RindexOf(List<object?> vs, List<object?> xs)
    {
        if (xs.Count == 0)
        {
            return null;
        }
        for (int i = vs.Count - xs.Count; i >= 0; i--)
        {
            if (EqualAt(vs, i, xs))
            {
                return (long)i;
            }
        }
        return null;
    }

    private static object? IndexFunc(string name, object? v, object? x, Func<List<object?>, List<object?>, object?> f)
    {
        switch (v)
        {
            case null:
                return null;
            case List<object?> a:
                return x is List<object?> xa ? f(a, xa) : f(a, new List<object?> { x });
            case string s:
                if (x is string t)
                {
                    return f(Explode(s), Explode(t));
                }
                return new Func1TypeError(name, v, x);
            default:
                return new Func1TypeError(name, v, x);
        }
    }

    private static object? StringFunc1(string name, object? v, object? x, Func<string, string, object?> f)
    {
        if (v is string s && x is string t)
        {
            return f(s, t);
        }
        return new Func1TypeError(name, v, x);
    }

    private static string TrimPrefix(string s, string t)
    {
        return s.StartsWith(t, StringComparison.Ordinal) ? s.Substring(t.Length) : s;
    }

    private static string TrimSuffix(string s, string t)
    {
        return s.EndsWith(t, StringComparison.Ordinal) ? s.Substring(0, s.Length - t.Length) : s;
    }

    // Counts code points; a lone surrogate counts as one, like an invalid byte in Go.
    internal static int CodePointCount(string s)
    {
        int n = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
            }
            n++;
        }
        return n;
    }

    // Returns the UTF-16 index of the code point at index cp (or s.Length past the end).
    private static int CodePointOffset(string s, int cp)
    {
        int i = 0;
        while (cp > 0 && i < s.Length)
        {
            i += char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
            cp--;
        }
        return i;
    }

    private static List<object?> Explode(string s)
    {
        List<object?> xs = new(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                xs.Add((long)char.ConvertToUtf32(c, s[++i]));
            }
            else
            {
                xs.Add(char.IsSurrogate(c) ? 0xFFFDL : c);
            }
        }
        return xs;
    }

    private static object? FuncImplode(object? v)
    {
        if (v is not List<object?> vs)
        {
            return new Func0TypeError("implode", v);
        }
        StringBuilder sb = new(vs.Count);
        foreach (object? x in vs)
        {
            if (!ToInt(x, out long r))
            {
                return new Func0TypeError("implode", vs);
            }
            if (r < 0 || r > 0x10FFFF || (r >= 0xD800 && r <= 0xDFFF))
            {
                r = 0xFFFD;
            }
            sb.Append(char.ConvertFromUtf32((int)r));
        }
        return sb.ToString();
    }

    private static List<object?> SplitString(string s, string t)
    {
        List<object?> xs = new();
        if (s == "")
        {
            return xs;
        }
        if (t == "")
        {
            for (int i = 0; i < s.Length; i++)
            {
                int size = char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
                xs.Add(s.Substring(i, size));
                i += size - 1;
            }
            return xs;
        }
        foreach (string x in s.Split(new[] { t }, StringSplitOptions.None))
        {
            xs.Add(x);
        }
        return xs;
    }

    private static object? FuncJoin(object? v, object? x)
    {
        if (!Values(v, out List<object?> vs))
        {
            return new Func1TypeError("join", v, x);
        }
        if (vs.Count == 0)
        {
            return "";
        }
        return Add(JoinItems(vs, x));
    }

    private static IEnumerable<object?> JoinItems(List<object?> vs, object? x)
    {
        for (int i = 0; i < vs.Count; i++)
        {
            yield return i == 0 ? "" : x;
            object? v = vs[i];
            yield return v is bool or long or double or BigInteger or JsonNumber ? Encoder.Marshal(v) : v;
        }
    }

    private static string MapAscii(string s, char from, char to, int delta)
    {
        char[] cs = s.ToCharArray();
        for (int i = 0; i < cs.Length; i++)
        {
            if (cs[i] >= from && cs[i] <= to)
            {
                cs[i] = (char)(cs[i] + delta);
            }
        }
        return new string(cs);
    }

    private static object? FuncFromJson(object? v)
    {
        if (v is not string s)
        {
            return new Func0TypeError("fromjson", v);
        }
        int pos = 0;
        object? w;
        try
        {
            if (!Decoder.TryDecode(s, ref pos, out w))
            {
                return new Func0WrapError("fromjson", v, new SimpleError("EOF"));
            }
        }
        catch (JqException err)
        {
            return new Func0WrapError("fromjson", v, err);
        }
        while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t' || s[pos] == '\r' || s[pos] == '\n'))
        {
            pos++;
        }
        if (pos < s.Length)
        {
            return new Func0TypeError("fromjson", v);
        }
        return w;
    }

    private static object? FuncFormat(object? v, object? x)
    {
        if (x is not string s)
        {
            return new Func0TypeError("format", x);
        }
        string format = "@" + s;
        FuncCall? f = Compiler.FormatToFunc(format);
        if (f == null)
        {
            return new FormatNotFoundError(format);
        }
        return InternalFuncs[f.Name].Callback!(v, Array.Empty<object?>());
    }

    // Replaces single characters by their escapes in one pass, like strings.NewReplacer.
    private static string Replace(string s, string[] escapes)
    {
        StringBuilder? sb = null;
        for (int i = 0; i < s.Length; i++)
        {
            string? rep = null;
            for (int j = 0; j < escapes.Length; j += 2)
            {
                if (s[i] == escapes[j][0])
                {
                    rep = escapes[j + 1];
                    break;
                }
            }
            if (rep != null)
            {
                sb ??= new StringBuilder(s, 0, i, s.Length + 16);
                sb.Append(rep);
            }
            else
            {
                sb?.Append(s[i]);
            }
        }
        return sb?.ToString() ?? s;
    }

    private static object? FuncToHtml(object? v)
    {
        return Replace(ToStr(v), HtmlEscapes);
    }

    private static object? FuncToUri(object? v)
    {
        const string hex = "0123456789ABCDEF";
        StringBuilder sb = new();
        foreach (byte b in Encoding.UTF8.GetBytes(ToStr(v)))
        {
            if ((b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') || (b >= '0' && b <= '9') || b == '-' || b == '_' || b == '.' || b == '~')
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append('%').Append(hex[b >> 4]).Append(hex[b & 15]);
            }
        }
        return sb.ToString();
    }

    private static object? FuncToUrid(object? v)
    {
        string s = ToStr(v);
        List<byte> bytes = new(s.Length);
        byte[] src = Encoding.UTF8.GetBytes(s);
        for (int i = 0; i < src.Length; i++)
        {
            if (src[i] != '%')
            {
                bytes.Add(src[i]);
                continue;
            }
            if (i + 2 >= src.Length || !IsHexByte(src[i + 1]) || !IsHexByte(src[i + 2]))
            {
                string bad = Encoding.UTF8.GetString(src, i, Math.Min(3, src.Length - i));
                return new Func0WrapError("@urid", v, new SimpleError("invalid URL escape " + Encoder.Marshal(bad)));
            }
            bytes.Add((byte)((HexValue(src[i + 1]) << 4) | HexValue(src[i + 2])));
            i += 2;
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static bool IsHexByte(byte b)
    {
        return (b >= '0' && b <= '9') || (b >= 'a' && b <= 'f') || (b >= 'A' && b <= 'F');
    }

    private static int HexValue(byte b)
    {
        return b <= '9' ? b - '0' : (b | 0x20) - 'a' + 10;
    }

    private static object? FormatJoin(string typ, object? v, string sep, Func<string, string> escape)
    {
        if (v is not List<object?> vs)
        {
            return new Func0TypeError("@" + typ, v);
        }
        string[] ss = new string[vs.Count];
        for (int i = 0; i < vs.Count; i++)
        {
            switch (vs[i])
            {
                case List<object?> or Dictionary<string, object?>:
                    return new FormatRowError(typ, vs[i]);
                case string s:
                    ss[i] = escape(s);
                    break;
                default:
                {
                    string s = Encoder.Marshal(vs[i]);
                    ss[i] = s != "null" || typ == "sh" ? s : "";
                    break;
                }
            }
        }
        return string.Join(sep, ss);
    }

    private static object? FuncToBase64d(object? v)
    {
        string x = ToStr(v);
        int pad = x.IndexOf('=');
        if (pad >= 0)
        {
            x = x.Substring(0, pad);
        }
        byte[] src = Encoding.UTF8.GetBytes(x);
        List<byte> dst = new(src.Length * 3 / 4);
        int[] quantum = new int[4];
        int si = 0;
        while (true)
        {
            int j = 0;
            while (j < 4)
            {
                if (si == src.Length)
                {
                    break;
                }
                byte b = src[si++];
                int d = Base64Value(b);
                if (d >= 0)
                {
                    quantum[j++] = d;
                    continue;
                }
                if (b == '\n' || b == '\r')
                {
                    continue;
                }
                return Base64Error(v, si - 1);
            }
            if (j == 0)
            {
                break;
            }
            if (j == 1)
            {
                return Base64Error(v, si - 1);
            }
            int val = (quantum[0] << 18) | (quantum[1] << 12) | (j > 2 ? quantum[2] << 6 : 0) | (j > 3 ? quantum[3] : 0);
            dst.Add((byte)(val >> 16));
            if (j > 2)
            {
                dst.Add((byte)(val >> 8));
            }
            if (j > 3)
            {
                dst.Add((byte)val);
            }
            if (j < 4)
            {
                break;
            }
        }
        return Encoding.UTF8.GetString(dst.ToArray());
    }

    private static int Base64Value(byte b)
    {
        return b >= 'A' && b <= 'Z' ? b - 'A'
            : b >= 'a' && b <= 'z' ? b - 'a' + 26
            : b >= '0' && b <= '9' ? b - '0' + 52
            : b == '+' ? 62
            : b == '/' ? 63
            : -1;
    }

    private static JqException Base64Error(object? v, int offset)
    {
        return new Func0WrapError("@base64d", v, new SimpleError("illegal base64 data at input byte " + offset.ToString(CultureInfo.InvariantCulture)));
    }

    internal static object? FuncIndex2(object? v, object? x)
    {
        switch (x)
        {
            case string s:
                return v switch
                {
                    null => null,
                    Dictionary<string, object?> m => m.TryGetValue(s, out object? w) ? w : null,
                    _ => new ExpectedObjectError(v),
                };
            case long or double or BigInteger or JsonNumber:
            {
                ToInt(x, out long i);
                return v switch
                {
                    null => null,
                    List<object?> a => Index(a, i),
                    string str => IndexString(str, i),
                    _ => new ExpectedArrayError(v),
                };
            }
            case List<object?> xa:
                return v switch
                {
                    null => null,
                    List<object?> a => Indices(a, xa),
                    _ => new ExpectedArrayError(v),
                };
            case Dictionary<string, object?> xm:
            {
                if (v == null)
                {
                    return null;
                }
                if (!xm.TryGetValue("start", out object? start))
                {
                    return new ExpectedStartEndError(xm);
                }
                if (!xm.TryGetValue("end", out object? end))
                {
                    return new ExpectedStartEndError(xm);
                }
                return FuncSlice(v, end, start);
            }
            default:
                return v switch
                {
                    List<object?> => new ArrayIndexNotNumberError(x),
                    string => new StringIndexNotNumberError(x),
                    _ => new ObjectKeyNotStringError(x),
                };
        }
    }

    private static object? Index(List<object?> vs, long i)
    {
        i = ClampIndex(i, -1, vs.Count);
        return 0 <= i && i < vs.Count ? vs[(int)i] : null;
    }

    private static object? IndexString(string s, long i)
    {
        int l = CodePointCount(s);
        i = ClampIndex(i, -1, l);
        if (0 <= i && i < l)
        {
            int start = CodePointOffset(s, (int)i);
            int size = char.IsHighSurrogate(s[start]) && start + 1 < s.Length && char.IsLowSurrogate(s[start + 1]) ? 2 : 1;
            return char.IsSurrogate(s[start]) && size == 1 ? "\uFFFD" : s.Substring(start, size);
        }
        return null;
    }

    internal static object? FuncSlice(object? v, object? e, object? s)
    {
        return v switch
        {
            null => null,
            List<object?> a => Slice(a, e, s),
            string str => SliceString(str, e, s),
            _ => new ExpectedArrayError(v),
        };
    }

    private static object? Slice(List<object?> vs, object? e, object? s)
    {
        long start = 0;
        long end;
        if (s != null)
        {
            if (!ToIntFloor(s, out long i))
            {
                return new ArrayIndexNotNumberError(s);
            }
            start = ClampIndex(i, 0, vs.Count);
        }
        if (e != null)
        {
            if (!ToIntCeil(e, out long i))
            {
                return new ArrayIndexNotNumberError(e);
            }
            end = ClampIndex(i, start, vs.Count);
        }
        else
        {
            end = vs.Count;
        }
        // Go returns a subslice; the whole range is the same slice, which path tracking relies on
        if (start == 0 && end == vs.Count)
        {
            return vs;
        }
        return vs.GetRange((int)start, (int)(end - start));
    }

    private static object? SliceString(string v, object? e, object? s)
    {
        long start = 0;
        long end;
        int l = CodePointCount(v);
        if (s != null)
        {
            if (!ToIntFloor(s, out long i))
            {
                return new StringIndexNotNumberError(s);
            }
            start = ClampIndex(i, 0, l);
        }
        if (e != null)
        {
            if (!ToIntCeil(e, out long i))
            {
                return new StringIndexNotNumberError(e);
            }
            end = ClampIndex(i, start, l);
        }
        else
        {
            end = l;
        }
        int startIndex = CodePointOffset(v, (int)start);
        int endIndex = CodePointOffset(v, (int)end);
        return v.Substring(startIndex, endIndex - startIndex);
    }

    private static long ClampIndex(long i, long minimum, long maximum)
    {
        if (i < 0)
        {
            i += maximum;
        }
        if (i < minimum)
        {
            return minimum;
        }
        return i < maximum ? i : maximum;
    }

    private static object? FuncFlatten(object? v, object?[] args)
    {
        if (!Values(v, out List<object?> vs))
        {
            return new Func0TypeError("flatten", v);
        }
        double depth;
        if (args.Length == 0)
        {
            depth = -1;
        }
        else
        {
            if (!ToFloat64(args[0], out depth))
            {
                return new Func0TypeError("flatten", args[0]);
            }
            if (Lt(depth, 0))
            {
                return new FlattenDepthError(depth);
            }
        }
        return Flatten(new List<object?>(), vs, depth);
    }

    private static List<object?> Flatten(List<object?> xs, List<object?> vs, double depth)
    {
        foreach (object? v in vs)
        {
            if (v is List<object?> a && depth != 0)
            {
                Flatten(xs, a, depth - 1);
            }
            else
            {
                xs.Add(v);
            }
        }
        return xs;
    }

    private sealed class RangeIter(object? value, object? end, object? step) : IIter
    {
        private object? _value = value;

        public bool Next(out object? v)
        {
            if (Math.Sign(Compare(step, 0L)) * Math.Sign(Compare(_value, end)) >= 0)
            {
                v = null;
                return false;
            }
            v = _value;
            _value = FuncOpAdd(v, step);
            return true;
        }
    }

    private static object? FuncRange(object? v, object?[] xs)
    {
        foreach (object? x in xs)
        {
            if (x is not (long or double or BigInteger or JsonNumber))
            {
                return new Func0TypeError("range", x);
            }
        }
        return new RangeIter(xs[0], xs[1], xs[2]);
    }

    private static object? MinMaxByFunc(string name, object? v, object? x, bool isMin)
    {
        if (v is not List<object?> vs || x is not List<object?> xs)
        {
            return new Func1TypeError(name, v, x);
        }
        if (vs.Count != xs.Count)
        {
            return new Func1WrapError(name, v, x, new LengthMismatchError());
        }
        return MinMaxBy(vs, xs, isMin);
    }

    private static object? MinMaxBy(List<object?> vs, List<object?> xs, bool isMin)
    {
        if (vs.Count == 0)
        {
            return null;
        }
        int j = 0;
        object? x = xs[0];
        for (int i = 1; i < xs.Count; i++)
        {
            if ((Compare(x, xs[i]) > 0) == isMin)
            {
                j = i;
                x = xs[i];
            }
        }
        return vs[j];
    }

    // A merge sort, like slices.SortStableFunc; it terminates even though NaN makes Compare inconsistent.
    private static List<T> StableSort<T>(List<T> items, Comparison<T> compare)
    {
        T[] a = items.ToArray();
        T[] tmp = new T[a.Length];
        for (int width = 1; width < a.Length; width *= 2)
        {
            for (int lo = 0; lo < a.Length; lo += 2 * width)
            {
                int mid = Math.Min(lo + width, a.Length);
                int hi = Math.Min(lo + (2 * width), a.Length);
                int i = lo;
                int j = mid;
                int k = lo;
                while (i < mid && j < hi)
                {
                    tmp[k++] = compare(a[j], a[i]) < 0 ? a[j++] : a[i++];
                }
                while (i < mid)
                {
                    tmp[k++] = a[i++];
                }
                while (j < hi)
                {
                    tmp[k++] = a[j++];
                }
            }
            (a, tmp) = (tmp, a);
        }
        return new List<T>(a);
    }

    private static object? SortItems(string name, object? v, object? x, out List<KeyValuePair<object?, object?>> items)
    {
        items = null!;
        if (v is not List<object?> vs)
        {
            return name.EndsWith("_by", StringComparison.Ordinal) ? new Func1TypeError(name, v, x) : new Func0TypeError(name, v);
        }
        if (x is not List<object?> xs)
        {
            return new Func1TypeError(name, v, x);
        }
        if (vs.Count != xs.Count)
        {
            return new Func1WrapError(name, v, x, new LengthMismatchError());
        }
        List<KeyValuePair<object?, object?>> list = new(vs.Count);
        for (int i = 0; i < vs.Count; i++)
        {
            list.Add(new KeyValuePair<object?, object?>(xs[i], vs[i]));
        }
        items = StableSort(list, static (x, y) => Compare(x.Key, y.Key));
        return null;
    }

    private static object? SortBy(string name, object? v, object? x)
    {
        object? err = SortItems(name, v, x, out List<KeyValuePair<object?, object?>> items);
        if (err != null)
        {
            return err;
        }
        List<object?> rs = new(items.Count);
        foreach (KeyValuePair<object?, object?> item in items)
        {
            rs.Add(item.Value);
        }
        return rs;
    }

    private static object? FuncGroupBy(object? v, object? x)
    {
        object? err = SortItems("group_by", v, x, out List<KeyValuePair<object?, object?>> items);
        if (err != null)
        {
            return err;
        }
        List<object?> rs = new();
        object? last = null;
        for (int i = 0; i < items.Count; i++)
        {
            if (i == 0 || Compare(last, items[i].Key) != 0)
            {
                rs.Add(new List<object?> { items[i].Value });
                last = items[i].Key;
            }
            else
            {
                ((List<object?>)rs[rs.Count - 1]!).Add(items[i].Value);
            }
        }
        return rs;
    }

    private static object? UniqueBy(string name, object? v, object? x)
    {
        object? err = SortItems(name, v, x, out List<KeyValuePair<object?, object?>> items);
        if (err != null)
        {
            return err;
        }
        List<object?> rs = new();
        object? last = null;
        for (int i = 0; i < items.Count; i++)
        {
            if (i == 0 || Compare(last, items[i].Key) != 0)
            {
                rs.Add(items[i].Value);
                last = items[i].Key;
            }
        }
        return rs;
    }

    private static object? FuncFrexp(object? v)
    {
        if (!ToFloat64(v, out double x))
        {
            return new Func0TypeError("frexp", v);
        }
        double f = GoMath.Frexp(x, out int e);
        return new List<object?> { f, (long)e };
    }

    private static object? FuncModf(object? v)
    {
        if (!ToFloat64(v, out double x))
        {
            return new Func0TypeError("modf", v);
        }
        if (double.IsInfinity(x))
        {
            return new List<object?> { GoMath.Copysign(0, x), x };
        }
        double i = GoMath.Modf(x, out double f);
        return new List<object?> { f, i };
    }

    private static double FuncDrem(double l, double r)
    {
        double x = GoMath.Remainder(l, r);
        return x == 0.0 ? GoMath.Copysign(x, l) : x;
    }

    private static int ToGoInt32(double x)
    {
        long n = FloatToInt(x);
        return n > int.MaxValue ? int.MaxValue : n < int.MinValue ? int.MinValue : (int)n;
    }

    private static double FuncJn(double l, double r)
    {
        int n = ToGoInt32(l);
        double x = Math.Abs(r);
        if (0 < x && x < double.PositiveInfinity)
        {
            // |J_n(x)| <= (|x|/2)^|n| / |n|! underflows to zero, avoiding the recurrence which takes O(|n|) time
            double m = Math.Abs((double)n);
            // values below 2^-1076, half of the rounding threshold 2^-1075, round to zero
            if (m * Math.Log(x / 2) - GoMath.Lgamma(m + 1, out _) < -1076 * 0.693147180559945309417232121458176568)
            {
                if ((n & 1) != 0 && (n < 0) != (r < 0))
                {
                    return GoMath.Copysign(0, -1);
                }
                return 0;
            }
        }
        return GoMath.Jn(n, r);
    }

    private static double FuncLdexp(double l, double r)
    {
        return GoMath.Ldexp(l, ToGoInt32(r));
    }

    private static double FuncYn(double l, double r)
    {
        int n = ToGoInt32(l);
        double m = Math.Abs((double)n);
        if (0 < r && r <= m - 1)
        {
            // |Y_n(x)| >= 2/(pi x J_{n-1}(x)) overflows for 0 < x <= n-1, avoiding the long recurrence
            if (Math.Log(2 / (Math.PI * r)) + GoMath.Lgamma(m, out _) - ((m - 1) * Math.Log(r / 2)) > 710)
            {
                if (n < 0 && (n & 1) != 0)
                {
                    return double.PositiveInfinity;
                }
                return double.NegativeInfinity;
            }
        }
        return GoMath.Yn(n, r);
    }

    private static object? FuncIsnormal(object? v)
    {
        if (ToFloat64(v, out double x))
        {
            ulong e = (GoMath.Float64bits(x) & 0x7ff0000000000000) >> 52;
            return 0 < e && e < 0x7ff;
        }
        return false;
    }

    // An allocator remembers the objects and arrays created during one assignment, which may then be updated in place.
    internal sealed class Allocator
    {
        private readonly HashSet<object> _allocated = new(ReferenceComparer.Instance);

        public static bool Allocated(Allocator? a, object? v)
        {
            return a != null && v != null && a._allocated.Contains(v);
        }

        // Forgets every allocated value when v is one of them, so a value handed to a query is copied on update again.
        public void Release(object? v)
        {
            if (v is Dictionary<string, object?> or List<object?> && Allocated(this, v))
            {
                _allocated.Clear();
            }
        }

        public static Dictionary<string, object?> MakeObject(Allocator? a, Dictionary<string, object?>? from)
        {
            Dictionary<string, object?> v = from == null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(from);
            a?._allocated.Add(v);
            return v;
        }

        public static List<object?> MakeArray(Allocator? a, int capacity)
        {
            List<object?> v = new(capacity);
            a?._allocated.Add(v);
            return v;
        }
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

    internal static object? FuncAllocator(object? v, object?[] args)
    {
        return new Allocator();
    }

    internal static object? FuncSetpathWithAllocator(object? v, object?[] args)
    {
        return Setpath(v, args[0], args[1], (Allocator)args[2]!);
    }

    // The update function may store the value anywhere, so it must not be modified in place afterwards.
    internal static object? FuncGetpathWithAllocator(object? v, object?[] args)
    {
        object? w = FuncGetpath(v, args[0]);
        ((Allocator)args[1]!).Release(w);
        return w;
    }

    internal static object? FuncDelpathsWithAllocator(object? v, object?[] args)
    {
        return Delpaths(v, args[0], (Allocator)args[1]!);
    }

    private static object? Setpath(object? v, object? p, object? n, Allocator? a)
    {
        if (p is not List<object?> path)
        {
            return new Func1TypeError("setpath", v, p);
        }
        object? u = Update(v, path, 0, n, a);
        if (u is JqException err)
        {
            return new Func2WrapError("setpath", v, p, n, err);
        }
        return u;
    }

    private static object? Delpaths(object? v, object? p, Allocator a)
    {
        if (p is not List<object?> paths)
        {
            return new Func1TypeError("delpaths", v, p);
        }
        if (paths.Count == 0)
        {
            return v;
        }
        // Sorts the paths to delete ancestor before descendant paths.
        bool sorted = true;
        for (int i = 1; i < paths.Count && sorted; i++)
        {
            sorted = Compare(paths[i - 1], paths[i]) <= 0;
        }
        if (!sorted)
        {
            paths = StableSort(paths, Compare);
        }
        // Fills the paths with an empty value and then deletes them, so that array indices do not shift.
        object? u = v;
        foreach (object? q in paths)
        {
            if (q is not List<object?> path)
            {
                return new Func1WrapError("delpaths", v, p, new ExpectedArrayError(q));
            }
            u = Update(u, path, 0, Empty, a);
            if (u is JqException err)
            {
                return new Func1WrapError("delpaths", v, p, err);
            }
        }
        return DeleteEmpty(u);
    }

    // Returns the updated value or a JqException.
    private static object? Update(object? v, List<object?> path, int depth, object? n, Allocator? a)
    {
        if (depth == path.Count)
        {
            return n;
        }
        object? p = path[depth];
        switch (p)
        {
            case string s:
                return v switch
                {
                    null => UpdateObject(null, s, path, depth + 1, n, a),
                    Dictionary<string, object?> m => UpdateObject(m, s, path, depth + 1, n, a),
                    _ when ReferenceEquals(v, Empty) => v,
                    _ => new ExpectedObjectError(v),
                };
            case long or double or BigInteger or JsonNumber:
            {
                ToInt(p, out long i);
                return v switch
                {
                    null => UpdateArrayIndex(null, i, path, depth + 1, n, a),
                    List<object?> l => UpdateArrayIndex(l, i, path, depth + 1, n, a),
                    _ when ReferenceEquals(v, Empty) => v,
                    _ => new ExpectedArrayError(v),
                };
            }
            case Dictionary<string, object?> m:
                return v switch
                {
                    null => UpdateArraySlice(null, m, path, depth + 1, n, a),
                    List<object?> l => UpdateArraySlice(l, m, path, depth + 1, n, a),
                    _ when ReferenceEquals(v, Empty) => v,
                    _ => new ExpectedArrayError(v),
                };
            default:
                return v is List<object?> ? new ArrayIndexNotNumberError(p) : new ObjectKeyNotStringError(p);
        }
    }

    private static object? UpdateObject(Dictionary<string, object?>? v, string k, List<object?> path, int depth, object? n, Allocator? a)
    {
        object? x = null;
        bool ok = v != null && v.TryGetValue(k, out x);
        if (!ok && ReferenceEquals(n, Empty))
        {
            return v;
        }
        object? u = Update(x, path, depth, n, a);
        if (u is JqException)
        {
            return u;
        }
        if (Allocator.Allocated(a, v))
        {
            v![k] = u;
            return v;
        }
        Dictionary<string, object?> w = Allocator.MakeObject(a, v);
        w[k] = u;
        return w;
    }

    private static object? UpdateArrayIndex(List<object?>? v, long i, List<object?> path, int depth, object? n, Allocator? a)
    {
        object? x = null;
        int len = v?.Count ?? 0;
        long j = ClampIndex(i, -1, len);
        if (j < 0)
        {
            if (ReferenceEquals(n, Empty))
            {
                return v;
            }
            return new ArrayIndexNegativeError(i);
        }
        if (j < len)
        {
            i = j;
            x = v![(int)i];
        }
        else
        {
            if (ReferenceEquals(n, Empty))
            {
                return v;
            }
            if (i >= 0x20000000)
            {
                return new ArrayIndexTooLargeError(i);
            }
        }
        object? u = Update(x, path, depth, n, a);
        if (u is JqException)
        {
            return u;
        }
        List<object?> w;
        if (Allocator.Allocated(a, v))
        {
            w = v!;
        }
        else
        {
            w = Allocator.MakeArray(a, (int)Math.Max(len, i + 1));
            if (v != null)
            {
                w.AddRange(v);
            }
        }
        while (w.Count <= i)
        {
            w.Add(null);
        }
        w[(int)i] = u;
        return w;
    }

    private static object? UpdateArraySlice(List<object?>? v, Dictionary<string, object?> m, List<object?> path, int depth, object? n, Allocator? a)
    {
        if (!m.TryGetValue("start", out object? s))
        {
            return new ExpectedStartEndError(m);
        }
        if (!m.TryGetValue("end", out object? e))
        {
            return new ExpectedStartEndError(m);
        }
        int len = v?.Count ?? 0;
        long start = 0;
        long end;
        if (s != null)
        {
            if (!ToIntFloor(s, out long i))
            {
                return new ArrayIndexNotNumberError(s);
            }
            start = ClampIndex(i, 0, len);
        }
        if (e != null)
        {
            if (!ToIntCeil(e, out long i))
            {
                return new ArrayIndexNotNumberError(e);
            }
            end = ClampIndex(i, start, len);
        }
        else
        {
            end = len;
        }
        if (start == end && ReferenceEquals(n, Empty))
        {
            return v;
        }
        List<object?> part = v == null ? new List<object?>() : v.GetRange((int)start, (int)(end - start));
        object? u = Update(part, path, depth, n, a);
        switch (u)
        {
            case JqException:
                return u;
            case List<object?> ul:
            {
                if (ul.Count == end - start && Allocator.Allocated(a, v))
                {
                    for (int k = 0; k < ul.Count; k++)
                    {
                        v![(int)start + k] = ul[k];
                    }
                    return v;
                }
                List<object?> w = Allocator.MakeArray(a, len - (int)(end - start) + ul.Count);
                if (v != null)
                {
                    w.AddRange(v.GetRange(0, (int)start));
                }
                w.AddRange(ul);
                if (v != null)
                {
                    w.AddRange(v.GetRange((int)end, len - (int)end));
                }
                return w;
            }
            default:
                if (ReferenceEquals(u, Empty))
                {
                    List<object?> w;
                    if (Allocator.Allocated(a, v))
                    {
                        w = v!;
                    }
                    else
                    {
                        w = Allocator.MakeArray(a, len);
                        if (v != null)
                        {
                            w.AddRange(v);
                        }
                    }
                    for (long k = start; k < end; k++)
                    {
                        w[(int)k] = u;
                    }
                    return w;
                }
                return new ExpectedArrayError(u);
        }
    }

    // Removes the Empty placeholders; they only live in containers the allocator created, so input values are untouched.
    private static object? DeleteEmpty(object? v)
    {
        if (ReferenceEquals(v, Empty))
        {
            return null;
        }
        switch (v)
        {
            case Dictionary<string, object?> m:
            {
                List<string>? removed = null;
                foreach (KeyValuePair<string, object?> kv in m)
                {
                    if (ReferenceEquals(kv.Value, Empty))
                    {
                        (removed ??= new List<string>()).Add(kv.Key);
                    }
                    else
                    {
                        DeleteEmpty(kv.Value);
                    }
                }
                if (removed != null)
                {
                    foreach (string k in removed)
                    {
                        m.Remove(k);
                    }
                }
                return m;
            }
            case List<object?> l:
            {
                bool hasEmpty = false;
                foreach (object? w in l)
                {
                    if (ReferenceEquals(w, Empty))
                    {
                        hasEmpty = true;
                    }
                    else
                    {
                        DeleteEmpty(w);
                    }
                }
                if (hasEmpty)
                {
                    l.RemoveAll(static w => ReferenceEquals(w, Empty));
                }
                return l;
            }
            default:
                return v;
        }
    }

    internal static object? FuncGetpath(object? v, object? p)
    {
        if (p is not List<object?> path)
        {
            return new Func1TypeError("getpath", v, p);
        }
        object? u = v;
        foreach (object? x in path)
        {
            v = FuncIndex2(v, x);
            if (v is JqException err)
            {
                return new Func1WrapError("getpath", u, p, err);
            }
        }
        return v;
    }

    private static object? FuncTranspose(object? v)
    {
        if (v is not List<object?> vss)
        {
            return new Func0TypeError("transpose", v);
        }
        if (vss.Count == 0)
        {
            return new List<object?>();
        }
        int l = 0;
        foreach (object? vs in vss)
        {
            if (vs is not List<object?> a)
            {
                return new Func0TypeError("transpose", v);
            }
            l = Math.Max(l, a.Count);
        }
        List<object?> xs = new(l);
        for (int i = 0; i < l; i++)
        {
            List<object?> s = new(vss.Count);
            for (int k = 0; k < vss.Count; k++)
            {
                List<object?> a = (List<object?>)vss[k]!;
                s.Add(i < a.Count ? a[i] : null);
            }
            xs.Add(s);
        }
        return xs;
    }

    private static object? FuncBsearch(object? v, object? t)
    {
        if (v is not List<object?> vs)
        {
            return new Func1TypeError("bsearch", v, t);
        }
        int lo = 0;
        int hi = vs.Count;
        while (lo < hi)
        {
            int mid = (int)((uint)(lo + hi) >> 1);
            if (Compare(vs[mid], t) < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }
        if (lo < vs.Count && Compare(vs[lo], t) == 0)
        {
            return (long)lo;
        }
        return (long)(-lo - 1);
    }

    private static object? TimeToArrayFunc(string name, object? v, GoTime.Location? loc)
    {
        if (!ToFloat64(v, out double w))
        {
            return new Func0TypeError(name, v);
        }
        object? t = EpochToTime(w, loc);
        if (t is JqException err)
        {
            return new Func0WrapError(name, v, err);
        }
        return TimeToArray((GoTime)t!);
    }

    private static object EpochToTime(double v, GoTime.Location? loc)
    {
        double i = Math.Floor(v);
        double f = v - i;
        if (!(-TwoPow63 <= i && i < TwoPow63))
        {
            return new TimeEpochError();
        }
        GoTime t = GoTime.UnixIn((long)i, (long)(f * 1e9), loc);
        if (i < 0 && t.Year > 1970)
        {
            return new TimeEpochError();
        }
        return t;
    }

    private static List<object?> TimeToArray(GoTime t)
    {
        return new List<object?>
        {
            t.Year,
            (long)t.Month - 1,
            (long)t.Day,
            (long)t.Hour,
            (long)t.Minute,
            t.Second + (t.Nanosecond / 1e9),
            (long)t.Weekday,
            (long)t.YearDay - 1,
        };
    }

    private static object? FuncMktime(object? v)
    {
        if (v is not List<object?> a)
        {
            return new Func0TypeError("mktime", v);
        }
        object t = ArrayToTime(a, null);
        if (t is JqException err)
        {
            return new Func0WrapError("mktime", v, err);
        }
        return TimeToEpoch((GoTime)t);
    }

    private static double TimeToEpoch(GoTime t)
    {
        return t.Unix() + (t.Nanosecond / 1e9);
    }

    private static object? FormatTimeFunc(string name, object? v, object? x, GoTime.Location? loc)
    {
        if (x is not string format)
        {
            return new Func1TypeError(name, v, x);
        }
        object t;
        if (ToFloat64(v, out double w))
        {
            t = EpochToTime(w, loc);
        }
        else if (v is List<object?> a)
        {
            t = ArrayToTime(a, loc);
        }
        else
        {
            return new Func1TypeError(name, v, x);
        }
        if (t is JqException err)
        {
            return new Func1WrapError(name, v, x, err);
        }
        return TimeFmt.Format((GoTime)t, format);
    }

    private static object? FuncStrptime(object? v, object? x)
    {
        if (v is not string s || x is not string format)
        {
            return new Func1TypeError("strptime", v, x);
        }
        GoTime t;
        try
        {
            t = TimeFmt.Parse(s, format);
        }
        catch (FormatException err)
        {
            return new Func1WrapError("strptime", v, x, err);
        }
        if (t.IsZero)
        {
            return new Func1TypeError("strptime", v, x);
        }
        return TimeToArray(t.UTC());
    }

    private static object ArrayToTime(List<object?> a, GoTime.Location? loc)
    {
        long[] fields = new long[8]; // year, month, day, hour, minute, second, weekday, yearday
        long nanosecond = 0;
        for (int i = 0; i < fields.Length && i < a.Count; i++)
        {
            if (!ToFloat64(a[i], out double v))
            {
                return new TimeArrayError();
            }
            if (!(-TwoPow63 <= v && v < TwoPow63))
            {
                return new TimeNumberError();
            }
            if (i == 5)
            {
                fields[i] = (long)Math.Floor(v);
                nanosecond = (long)((v - Math.Floor(v)) * 1e9);
            }
            else
            {
                ToInt(a[i], out fields[i]);
            }
        }
        return GoTime.DateIn(fields[0], fields[1] + 1, fields[2], fields[3], fields[4], fields[5], nanosecond, loc);
    }

    internal static object? FuncMatch(object? v, object? re, object? fs, object? testing, ConcurrentDictionary<(string, string), RegexMatcher> cache)
    {
        string name = testing is true ? "test" : "match";
        string flags = "";
        if (fs != null)
        {
            if (fs is not string f)
            {
                return new Func2TypeError(name, v, re, fs);
            }
            flags = f;
        }
        if (v is not string s || re is not string restr)
        {
            return new Func2TypeError(name, v, re, fs);
        }
        object r = CompileRegexp(restr, flags, cache);
        if (r is JqException)
        {
            return r;
        }
        RegexMatcher m = (RegexMatcher)r;
        if (testing is true)
        {
            return m.Regex.IsMatch(s);
        }
        List<object?> res = new();
        Match match = m.Regex.Match(s);
        bool global = flags.IndexOf('g') >= 0;
        while (match.Success)
        {
            List<object?> captures = new(m.Names.Count);
            for (int j = 1; j <= m.Names.Count; j++)
            {
                Group g = match.Groups[j];
                object? gname = m.Names[j - 1];
                if (!g.Success)
                {
                    captures.Add(new Dictionary<string, object?> { ["name"] = gname, ["offset"] = -1L, ["length"] = 0L, ["string"] = null });
                    continue;
                }
                captures.Add(MatchObject(s, g.Index, g.Length, gname, null));
            }
            res.Add(MatchObject(s, match.Index, match.Length, null, captures));
            if (!global)
            {
                break;
            }
            match = match.NextMatch();
        }
        return res;
    }

    private static Dictionary<string, object?> MatchObject(string s, int index, int length, object? name, List<object?>? captures)
    {
        long offset = CodePointCount(s.Substring(0, index));
        Dictionary<string, object?> m = new()
        {
            ["offset"] = offset,
            ["length"] = (long)CodePointCount(s.Substring(index, length)),
            ["string"] = s.Substring(index, length),
        };
        if (captures != null)
        {
            m["captures"] = captures;
        }
        else
        {
            m["name"] = name;
        }
        return m;
    }

    // A compiled regular expression with its capture names in Go's group order.
    internal sealed class RegexMatcher(Regex regex, List<object?> names)
    {
        public Regex Regex { get; } = regex;

        public List<object?> Names { get; } = names;
    }

    private static object CompileRegexp(string re, string flags, ConcurrentDictionary<(string, string), RegexMatcher> cache)
    {
        if (cache.TryGetValue((re, flags), out RegexMatcher? cached))
        {
            return cached;
        }
        foreach (char c in flags)
        {
            if (c != 'g' && c != 'i' && c != 'm')
            {
                return new SimpleError("unsupported regular expression flag: " + Encoder.Marshal(flags));
            }
        }
        string pattern = re;
        if (flags.IndexOf('i') >= 0)
        {
            pattern = "(?i)" + pattern;
        }
        if (flags.IndexOf('m') >= 0)
        {
            pattern = "(?s)" + pattern;
        }
        Regex regex;
        List<object?> names = new();
        try
        {
            regex = new Regex(TranslateGroups(pattern, names), RegexOptions.CultureInvariant);
        }
        catch (ArgumentException err)
        {
            return new SimpleError("invalid regular expression " + Encoder.Marshal(pattern) + ": " + err.Message);
        }
        RegexMatcher m = new(regex, names);
        cache[(re, flags)] = m;
        return m;
    }

    // .NET numbers named groups after the unnamed ones, so every capture becomes unnamed and the names are kept
    // in pattern order, as Go numbers them. (?P<name>...) is Go syntax for (?<name>...).
    private static string TranslateGroups(string pattern, List<object?> names)
    {
        StringBuilder sb = new(pattern.Length);
        bool inClass = false;
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                sb.Append(c).Append(pattern[++i]);
                continue;
            }
            if (inClass)
            {
                if (c == ']')
                {
                    inClass = false;
                }
                sb.Append(c);
                continue;
            }
            if (c == '[')
            {
                inClass = true;
                sb.Append(c);
                if (i + 1 < pattern.Length && pattern[i + 1] == '^')
                {
                    sb.Append(pattern[++i]);
                }
                if (i + 1 < pattern.Length && pattern[i + 1] == ']')
                {
                    sb.Append(pattern[++i]);
                }
                continue;
            }
            if (c != '(')
            {
                sb.Append(c);
                continue;
            }
            if (i + 1 < pattern.Length && pattern[i + 1] == '?')
            {
                int nameStart = -1;
                if (i + 3 < pattern.Length && pattern[i + 2] == 'P' && pattern[i + 3] == '<')
                {
                    nameStart = i + 4;
                }
                else if (i + 2 < pattern.Length && pattern[i + 2] == '<' && i + 3 < pattern.Length && pattern[i + 3] != '=' && pattern[i + 3] != '!')
                {
                    nameStart = i + 3;
                }
                int nameEnd = nameStart < 0 ? -1 : pattern.IndexOf('>', nameStart);
                if (nameEnd < 0)
                {
                    sb.Append(c);
                    continue;
                }
                names.Add(pattern.Substring(nameStart, nameEnd - nameStart));
                sb.Append('(');
                i = nameEnd;
                continue;
            }
            names.Add(null);
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static object? FuncCaptures(object? v)
    {
        if (v is not List<object?> captures)
        {
            return new ExpectedArrayError(v);
        }
        Dictionary<string, object?> w = new(captures.Count);
        foreach (object? capture in captures)
        {
            if (capture is Dictionary<string, object?> c && c.TryGetValue("name", out object? name) && name is string n)
            {
                w[n] = c.TryGetValue("string", out object? s) ? s : null;
            }
        }
        return w;
    }

    private static object? FuncHaltError(object? v, object?[] args)
    {
        long code = 5;
        if (args.Length > 0 && !ToInt(args[0], out code))
        {
            return new Func0TypeError("halt_error", args[0]);
        }
        return new JqHaltException(v, (int)code);
    }

    private static bool ToIntCeil(object? x, out long i)
    {
        if (x is JsonNumber n)
        {
            x = ParseNumber(n);
        }
        if (x is double f)
        {
            if ((-1 < f && f < 0) || double.IsNaN(f))
            {
                i = long.MaxValue;
                return true;
            }
            x = Math.Ceiling(f);
        }
        return ToInt(x, out i);
    }

    private static bool ToIntFloor(object? x, out long i)
    {
        if (x is JsonNumber n)
        {
            x = ParseNumber(n);
        }
        if (x is double f)
        {
            x = Math.Floor(f);
        }
        return ToInt(x, out i);
    }
}

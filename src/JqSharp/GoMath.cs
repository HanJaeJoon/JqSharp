// Ported from the Go math package (https://github.com/golang/go/tree/master/src/math).
// Copyright 2009 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license
// that can be found in the Go LICENSE file.
//
// Pure-Go fallback implementations are used so results match gojq. Constant
// expressions that Go folds with exact arithmetic (e.g. 2/Pi) are pre-rounded.

namespace JqSharp;

internal static class GoMath
{
    private const ulong UvNaN = 0x7FF8000000000001;
    private const ulong UvInf = 0x7FF0000000000000;
    private const int Mask = 0x7FF;
    private const int Shift = 64 - 11 - 1;
    private const int Bias = 1023;
    private const ulong SignMask = 1UL << 63;
    private const ulong FracMask = (1UL << Shift) - 1;

    private const double Pi = 3.141592653589793;
    private const double Ln2 = 0.6931471805599453;
    private const double Sqrt2 = 1.4142135623730951;
    private const double InvSqrtPi = 0.5641895835477563;
    private const double TwoOverPi = 0.6366197723675814;
    private const double FourOverPi = 1.2732395447351628;
    private const double InvLn2 = 1.4426950408889634;
    private const double MaxFloat64 = double.MaxValue;

    private static readonly double NaN = Float64frombits(UvNaN);

    internal static ulong Float64bits(double f)
    {
        return unchecked((ulong)BitConverter.DoubleToInt64Bits(f));
    }

    internal static double Float64frombits(ulong b)
    {
        return BitConverter.Int64BitsToDouble(unchecked((long)b));
    }

    private static bool Signbit(double x)
    {
        return (Float64bits(x) & SignMask) != 0;
    }

    private static double Normalize(double x, out int exp)
    {
        const double SmallestNormal = 2.2250738585072014e-308;
        if (Math.Abs(x) < SmallestNormal)
        {
            exp = -52;
            return x * (1L << 52);
        }
        exp = 0;
        return x;
    }

    // --- Go-style shifts and 128-bit helpers (Go yields 0 for shift counts >= 64) ---

    private static ulong ShlG(ulong v, uint n)
    {
        return n >= 64 ? 0 : v << (int)n;
    }

    private static ulong ShrG(ulong v, uint n)
    {
        return n >= 64 ? 0 : v >> (int)n;
    }

    private static int LeadingZeros64(ulong x)
    {
        if (x == 0)
        {
            return 64;
        }
        int n = 0;
        if ((x & 0xFFFFFFFF00000000) == 0) { n += 32; x <<= 32; }
        if ((x & 0xFFFF000000000000) == 0) { n += 16; x <<= 16; }
        if ((x & 0xFF00000000000000) == 0) { n += 8; x <<= 8; }
        if ((x & 0xF000000000000000) == 0) { n += 4; x <<= 4; }
        if ((x & 0xC000000000000000) == 0) { n += 2; x <<= 2; }
        if ((x & 0x8000000000000000) == 0) { n += 1; }
        return n;
    }

    private static ulong Mul64(ulong x, ulong y, out ulong lo)
    {
        unchecked
        {
            ulong x0 = x & 0xFFFFFFFF, x1 = x >> 32;
            ulong y0 = y & 0xFFFFFFFF, y1 = y >> 32;
            ulong w0 = x0 * y0;
            ulong t = x1 * y0 + (w0 >> 32);
            ulong w1 = t & 0xFFFFFFFF;
            ulong w2 = t >> 32;
            w1 += x0 * y1;
            lo = x * y;
            return x1 * y1 + w2 + (w1 >> 32);
        }
    }

    private static ulong Add64(ulong x, ulong y, ulong carry, out ulong carryOut)
    {
        unchecked
        {
            ulong sum = x + y + carry;
            carryOut = ((x & y) | ((x | y) & ~sum)) >> 63;
            return sum;
        }
    }

    private static ulong Sub64(ulong x, ulong y, ulong borrow, out ulong borrowOut)
    {
        unchecked
        {
            ulong diff = x - y - borrow;
            borrowOut = ((~x & y) | (~(x ^ y) & diff)) >> 63;
            return diff;
        }
    }

    // --- Basic functions ---

    internal static double Copysign(double f, double sign)
    {
        return Float64frombits((Float64bits(f) & ~SignMask) | (Float64bits(sign) & SignMask));
    }

    internal static double Dim(double x, double y)
    {
        double v = x - y;
        if (v <= 0)
        {
            return 0;
        }
        return v;
    }

    internal static double Frexp(double f, out int exp)
    {
        exp = 0;
        if (f == 0 || double.IsInfinity(f) || double.IsNaN(f))
        {
            return f;
        }
        f = Normalize(f, out exp);
        ulong x = Float64bits(f);
        exp += (int)((x >> Shift) & Mask) - Bias + 1;
        x &= ~((ulong)Mask << Shift);
        x |= (ulong)(-1 + Bias) << Shift;
        return Float64frombits(x);
    }

    internal static double Ldexp(double frac, int exp)
    {
        if (frac == 0 || double.IsInfinity(frac) || double.IsNaN(frac))
        {
            return frac;
        }
        frac = Normalize(frac, out int e);
        exp += e;
        ulong x = Float64bits(frac);
        exp += ((int)(x >> Shift) & Mask) - Bias;
        if (exp < -1075)
        {
            return Copysign(0, frac);
        }
        if (exp > 1023)
        {
            return frac < 0 ? double.NegativeInfinity : double.PositiveInfinity;
        }
        double m = 1;
        if (exp < -1022)
        {
            exp += 53;
            m = 1.0 / (1L << 53);
        }
        x &= ~((ulong)Mask << Shift);
        x |= (ulong)(exp + Bias) << Shift;
        return m * Float64frombits(x);
    }

    internal static double Modf(double x, out double frac)
    {
        double integer = Math.Truncate(x);
        frac = Copysign(x - integer, x);
        return integer;
    }

    internal static double Nextafter(double x, double y)
    {
        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return NaN;
        }
        if (x == y)
        {
            return x;
        }
        if (x == 0)
        {
            return Copysign(Float64frombits(1), y);
        }
        if ((y > x) == (x > 0))
        {
            return Float64frombits(Float64bits(x) + 1);
        }
        return Float64frombits(Float64bits(x) - 1);
    }

    internal static double Remainder(double x, double y)
    {
        const double Tiny = 4.45014771701440276618e-308;
        const double HalfMax = MaxFloat64 / 2;
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || y == 0)
        {
            return NaN;
        }
        if (double.IsInfinity(y))
        {
            return x;
        }
        bool sign = false;
        if (x < 0)
        {
            x = -x;
            sign = true;
        }
        if (y < 0)
        {
            y = -y;
        }
        if (x == y)
        {
            return sign ? -0.0 : 0.0;
        }
        if (y <= HalfMax)
        {
            x %= y + y; // fmod is exact, same as Go's Mod here (finite operands)
        }
        if (y < Tiny)
        {
            if (x + x > y)
            {
                x -= y;
                if (x + x >= y)
                {
                    x -= y;
                }
            }
        }
        else
        {
            double yHalf = 0.5 * y;
            if (x > yHalf)
            {
                x -= y;
                if (x >= yHalf)
                {
                    x -= y;
                }
            }
        }
        return sign ? -x : x;
    }

    internal static double Hypot(double p, double q)
    {
        p = Math.Abs(p);
        q = Math.Abs(q);
        if (double.IsPositiveInfinity(p) || double.IsPositiveInfinity(q))
        {
            return double.PositiveInfinity;
        }
        if (double.IsNaN(p) || double.IsNaN(q))
        {
            return NaN;
        }
        if (p < q)
        {
            double t = p;
            p = q;
            q = t;
        }
        if (p == 0)
        {
            return 0;
        }
        q /= p;
        return p * Math.Sqrt(1 + q * q);
    }

    internal static double Logb(double x)
    {
        if (x == 0)
        {
            return double.NegativeInfinity;
        }
        if (double.IsInfinity(x))
        {
            return double.PositiveInfinity;
        }
        if (double.IsNaN(x))
        {
            return x;
        }
        x = Normalize(x, out int exp);
        return (int)((Float64bits(x) >> Shift) & Mask) - Bias + exp;
    }

    // --- Exponentials and logarithms ---

    private static double Exp(double x)
    {
        const double Ln2Hi = 6.93147180369123816490e-01;
        const double Ln2Lo = 1.90821492927058770002e-10;
        const double Log2e = 1.44269504088896338700e+00;
        const double Overflow = 7.09782712893383973096e+02;
        const double Underflow = -7.45133219101941108420e+02;
        const double NearZero = 1.0 / (1 << 28);
        if (double.IsNaN(x))
        {
            return x;
        }
        if (x > Overflow)
        {
            return double.PositiveInfinity;
        }
        if (x < Underflow)
        {
            return 0;
        }
        if (-NearZero < x && x < NearZero)
        {
            return 1 + x;
        }
        int k = 0;
        if (x < 0)
        {
            k = (int)(Log2e * x - 0.5);
        }
        else if (x > 0)
        {
            k = (int)(Log2e * x + 0.5);
        }
        double hi = x - k * Ln2Hi;
        double lo = k * Ln2Lo;
        return ExpMulti(hi, lo, k);
    }

    internal static double Exp2(double x)
    {
        const double Ln2Hi = 6.93147180369123816490e-01;
        const double Ln2Lo = 1.90821492927058770002e-10;
        const double Overflow = 1.0239999999999999e+03;
        const double Underflow = -1.0740e+03;
        if (double.IsNaN(x))
        {
            return x;
        }
        if (x > Overflow)
        {
            return double.PositiveInfinity;
        }
        if (x < Underflow)
        {
            return 0;
        }
        int k = 0;
        if (x > 0)
        {
            k = (int)(x + 0.5);
        }
        else if (x < 0)
        {
            k = (int)(x - 0.5);
        }
        double t = x - k;
        double hi = t * Ln2Hi;
        double lo = -t * Ln2Lo;
        return ExpMulti(hi, lo, k);
    }

    private static double ExpMulti(double hi, double lo, int k)
    {
        const double P1 = 1.66666666666666657415e-01;
        const double P2 = -2.77777777770155933842e-03;
        const double P3 = 6.61375632143793436117e-05;
        const double P4 = -1.65339022054652515390e-06;
        const double P5 = 4.13813679705723846039e-08;
        double r = hi - lo;
        double t = r * r;
        double c = r - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));
        double y = 1 - ((lo - (r * c) / (2 - c)) - hi);
        return Ldexp(y, k);
    }

    internal static double Expm1(double x)
    {
        const double Othreshold = 7.09782712893383973096e+02;
        const double Ln2X56 = 3.88162421113569373274e+01;
        const double Ln2HalfX3 = 1.03972077083991796413e+00;
        const double Ln2Half = 3.46573590279972654709e-01;
        const double Ln2Hi = 6.93147180369123816490e-01;
        const double Ln2Lo = 1.90821492927058770002e-10;
        const double InvLn2Expm1 = 1.44269504088896338700e+00;
        const double Tiny = 1.0 / (1L << 54);
        const double Q1 = -3.33333333333331316428e-02;
        const double Q2 = 1.58730158725481460165e-03;
        const double Q3 = -7.93650757867487942473e-05;
        const double Q4 = 4.00821782732936239552e-06;
        const double Q5 = -2.01099218183624371326e-07;

        if (double.IsPositiveInfinity(x) || double.IsNaN(x))
        {
            return x;
        }
        if (double.IsNegativeInfinity(x))
        {
            return -1;
        }
        double absx = x;
        bool sign = false;
        if (x < 0)
        {
            absx = -absx;
            sign = true;
        }
        if (absx >= Ln2X56)
        {
            if (sign)
            {
                return -1;
            }
            if (absx >= Othreshold)
            {
                return double.PositiveInfinity;
            }
        }
        double c = 0;
        int k;
        if (absx > Ln2Half)
        {
            double hi, lo;
            if (absx < Ln2HalfX3)
            {
                if (!sign)
                {
                    hi = x - Ln2Hi;
                    lo = Ln2Lo;
                    k = 1;
                }
                else
                {
                    hi = x + Ln2Hi;
                    lo = -Ln2Lo;
                    k = -1;
                }
            }
            else
            {
                k = !sign ? (int)(InvLn2Expm1 * x + 0.5) : (int)(InvLn2Expm1 * x - 0.5);
                double tk = k;
                hi = x - tk * Ln2Hi;
                lo = tk * Ln2Lo;
            }
            x = hi - lo;
            c = (hi - x) - lo;
        }
        else if (absx < Tiny)
        {
            return x;
        }
        else
        {
            k = 0;
        }
        double hfx = 0.5 * x;
        double hxs = x * hfx;
        double r1 = 1 + hxs * (Q1 + hxs * (Q2 + hxs * (Q3 + hxs * (Q4 + hxs * Q5))));
        double t = 3 - r1 * hfx;
        double e = hxs * ((r1 - t) / (6.0 - x * t));
        if (k == 0)
        {
            return x - (x * e - hxs);
        }
        e = x * (e - c) - c;
        e -= hxs;
        ulong kShifted = unchecked((ulong)(long)k << 52);
        if (k == -1)
        {
            return 0.5 * (x - e) - 0.5;
        }
        if (k == 1)
        {
            if (x < -0.25)
            {
                return -2 * (e - (x + 0.5));
            }
            return 1 + 2 * (x - e);
        }
        if (k <= -2 || k > 56)
        {
            double y = 1 - (e - x);
            y = Float64frombits(unchecked(Float64bits(y) + kShifted));
            return y - 1;
        }
        if (k < 20)
        {
            double tt = Float64frombits(0x3ff0000000000000 - (0x20000000000000UL >> k));
            double y = tt - (e - x);
            return Float64frombits(unchecked(Float64bits(y) + kShifted));
        }
        {
            t = Float64frombits((ulong)(0x3ff - k) << 52);
            double y = x - (e + t);
            y++;
            return Float64frombits(unchecked(Float64bits(y) + kShifted));
        }
    }

    private static double Log(double x)
    {
        const double Ln2Hi = 6.93147180369123816490e-01;
        const double Ln2Lo = 1.90821492927058770002e-10;
        const double L1 = 6.666666666666735130e-01;
        const double L2 = 3.999999999940941908e-01;
        const double L3 = 2.857142874366239149e-01;
        const double L4 = 2.222219843214978396e-01;
        const double L5 = 1.818357216161805012e-01;
        const double L6 = 1.531383769920937332e-01;
        const double L7 = 1.479819860511658591e-01;
        if (double.IsNaN(x) || double.IsPositiveInfinity(x))
        {
            return x;
        }
        if (x < 0)
        {
            return NaN;
        }
        if (x == 0)
        {
            return double.NegativeInfinity;
        }
        double f1 = Frexp(x, out int ki);
        if (f1 < Sqrt2 / 2)
        {
            f1 *= 2;
            ki--;
        }
        double f = f1 - 1;
        double k = ki;
        double s = f / (2 + f);
        double s2 = s * s;
        double s4 = s2 * s2;
        double t1 = s2 * (L1 + s4 * (L3 + s4 * (L5 + s4 * L7)));
        double t2 = s4 * (L2 + s4 * (L4 + s4 * L6));
        double r = t1 + t2;
        double hfsq = 0.5 * f * f;
        return k * Ln2Hi - ((hfsq - (s * (hfsq + r) + k * Ln2Lo)) - f);
    }

    internal static double Log1p(double x)
    {
        const double Sqrt2M1 = 4.142135623730950488017e-01;
        const double Sqrt2HalfM1 = -2.928932188134524755992e-01;
        const double Small = 1.0 / (1 << 29);
        const double Tiny = 1.0 / (1L << 54);
        const double Two53 = 1L << 53;
        const double Ln2Hi = 6.93147180369123816490e-01;
        const double Ln2Lo = 1.90821492927058770002e-10;
        const double Lp1 = 6.666666666666735130e-01;
        const double Lp2 = 3.999999999940941908e-01;
        const double Lp3 = 2.857142874366239149e-01;
        const double Lp4 = 2.222219843214978396e-01;
        const double Lp5 = 1.818357216161805012e-01;
        const double Lp6 = 1.531383769920937332e-01;
        const double Lp7 = 1.479819860511658591e-01;

        if (x < -1 || double.IsNaN(x))
        {
            return NaN;
        }
        if (x == -1)
        {
            return double.NegativeInfinity;
        }
        if (double.IsPositiveInfinity(x))
        {
            return double.PositiveInfinity;
        }
        double absx = Math.Abs(x);
        double f = 0;
        ulong iu = 0;
        int k = 1;
        if (absx < Sqrt2M1)
        {
            if (absx < Small)
            {
                if (absx < Tiny)
                {
                    return x;
                }
                return x - x * x * 0.5;
            }
            if (x > Sqrt2HalfM1)
            {
                k = 0;
                f = x;
                iu = 1;
            }
        }
        double c = 0;
        if (k != 0)
        {
            double u;
            if (absx < Two53)
            {
                u = 1.0 + x;
                iu = Float64bits(u);
                k = (int)((iu >> 52) - 1023);
                if (k > 0)
                {
                    c = 1.0 - (u - x);
                }
                else
                {
                    c = x - (u - 1.0);
                }
                c /= u;
            }
            else
            {
                u = x;
                iu = Float64bits(u);
                k = (int)((iu >> 52) - 1023);
                c = 0;
            }
            iu &= 0x000fffffffffffff;
            if (iu < 0x0006a09e667f3bcd)
            {
                u = Float64frombits(iu | 0x3ff0000000000000);
            }
            else
            {
                k++;
                u = Float64frombits(iu | 0x3fe0000000000000);
                iu = (0x0010000000000000 - iu) >> 2;
            }
            f = u - 1.0;
        }
        double hfsq = 0.5 * f * f;
        double s, r, z;
        if (iu == 0)
        {
            if (f == 0)
            {
                if (k == 0)
                {
                    return 0;
                }
                c += k * Ln2Lo;
                return k * Ln2Hi + c;
            }
            r = hfsq * (1.0 - 0.66666666666666666 * f);
            if (k == 0)
            {
                return f - r;
            }
            return k * Ln2Hi - ((r - (k * Ln2Lo + c)) - f);
        }
        s = f / (2.0 + f);
        z = s * s;
        r = z * (Lp1 + z * (Lp2 + z * (Lp3 + z * (Lp4 + z * (Lp5 + z * (Lp6 + z * Lp7))))));
        if (k == 0)
        {
            return f - (hfsq - s * (hfsq + r));
        }
        return k * Ln2Hi - ((hfsq - (s * (hfsq + r) + (k * Ln2Lo + c))) - f);
    }

    internal static double Log2(double x)
    {
        double frac = Frexp(x, out int exp);
        if (frac == 0.5)
        {
            return exp - 1;
        }
        if (exp == 0 || exp == 1)
        {
            return Log1p(x - 1) * InvLn2;
        }
        return Log(frac) * InvLn2 + exp;
    }

    internal static double Cbrt(double x)
    {
        const ulong B1 = 715094163;
        const ulong B2 = 696219795;
        const double C = 5.42857142857142815906e-01;
        const double D = -7.05306122448979611050e-01;
        const double E = 1.41428571428571436819e+00;
        const double F = 1.60714285714285720630e+00;
        const double G = 3.57142857142857150787e-01;
        const double SmallestNormal = 2.22507385850720138309e-308;

        if (x == 0 || double.IsNaN(x) || double.IsInfinity(x))
        {
            return x;
        }
        bool sign = false;
        if (x < 0)
        {
            x = -x;
            sign = true;
        }
        double t = Float64frombits(Float64bits(x) / 3 + (B1 << 32));
        if (x < SmallestNormal)
        {
            t = 1L << 54;
            t *= x;
            t = Float64frombits(Float64bits(t) / 3 + (B2 << 32));
        }
        double r = t * t / x;
        double s = C + r * t;
        t *= G + F / (s + E + D / s);
        t = Float64frombits((Float64bits(t) & (0xFFFFFFFFCUL << 28)) + (1UL << 30));
        s = t * t;
        r = x / s;
        double w = t + t;
        r = (r - t) / (w + r);
        t += t * r;
        return sign ? -t : t;
    }

    internal static double Pow(double x, double y)
    {
        if (y == 0 || x == 1)
        {
            return 1;
        }
        if (y == 1)
        {
            return x;
        }
        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return NaN;
        }
        if (x == 0)
        {
            if (y < 0)
            {
                if (Signbit(x) && IsOddInt(y))
                {
                    return double.NegativeInfinity;
                }
                return double.PositiveInfinity;
            }
            if (Signbit(x) && IsOddInt(y))
            {
                return x;
            }
            return 0;
        }
        if (double.IsInfinity(y))
        {
            if (x == -1)
            {
                return 1;
            }
            if ((Math.Abs(x) < 1) == double.IsPositiveInfinity(y))
            {
                return 0;
            }
            return double.PositiveInfinity;
        }
        if (double.IsInfinity(x))
        {
            if (double.IsNegativeInfinity(x))
            {
                return Pow(1 / x, -y);
            }
            return y < 0 ? 0 : double.PositiveInfinity;
        }
        if (y == 0.5)
        {
            return Math.Sqrt(x);
        }
        if (y == -0.5)
        {
            return 1 / Math.Sqrt(x);
        }

        double yi = Modf(Math.Abs(y), out double yf);
        if (yf != 0 && x < 0)
        {
            return NaN;
        }
        if (yi >= 9223372036854775808.0)
        {
            if (x == -1)
            {
                return 1;
            }
            if ((Math.Abs(x) < 1) == (y > 0))
            {
                return 0;
            }
            return double.PositiveInfinity;
        }

        double a1 = 1.0;
        int ae = 0;
        if (yf != 0)
        {
            if (yf > 0.5)
            {
                yf--;
                yi++;
            }
            a1 = Exp(yf * Log(x));
        }

        double x1 = Frexp(x, out int xe);
        for (long i = (long)yi; i != 0; i >>= 1)
        {
            if (xe < -(1 << 12) || (1 << 12) < xe)
            {
                ae += xe;
                break;
            }
            if ((i & 1) == 1)
            {
                a1 *= x1;
                ae += xe;
            }
            x1 *= x1;
            xe <<= 1;
            if (x1 < .5)
            {
                x1 += x1;
                xe--;
            }
        }

        if (y < 0)
        {
            a1 = 1 / a1;
            ae = -ae;
        }
        return Ldexp(a1, ae);
    }

    private static bool IsOddInt(double x)
    {
        if (Math.Abs(x) >= (1L << 53))
        {
            return false;
        }
        double xi = Modf(x, out double xf);
        return xf == 0 && ((long)xi & 1) == 1;
    }

    // --- Hyperbolic inverses ---

    internal static double Asinh(double x)
    {
        const double NearZero = 1.0 / (1 << 28);
        const double Large = 1 << 28;
        if (double.IsNaN(x) || double.IsInfinity(x))
        {
            return x;
        }
        bool sign = false;
        if (x < 0)
        {
            x = -x;
            sign = true;
        }
        double temp;
        if (x > Large)
        {
            temp = Log(x) + Ln2;
        }
        else if (x > 2)
        {
            temp = Log(2 * x + 1 / (Math.Sqrt(x * x + 1) + x));
        }
        else if (x < NearZero)
        {
            temp = x;
        }
        else
        {
            temp = Log1p(x + x * x / (1 + Math.Sqrt(1 + x * x)));
        }
        return sign ? -temp : temp;
    }

    internal static double Acosh(double x)
    {
        const double Large = 1 << 28;
        if (x < 1 || double.IsNaN(x))
        {
            return NaN;
        }
        if (x == 1)
        {
            return 0;
        }
        if (x >= Large)
        {
            return Log(x) + Ln2;
        }
        if (x > 2)
        {
            return Log(2 * x - 1 / (x + Math.Sqrt(x * x - 1)));
        }
        double t = x - 1;
        return Log1p(t + Math.Sqrt(2 * t + t * t));
    }

    internal static double Atanh(double x)
    {
        const double NearZero = 1.0 / (1 << 28);
        if (x < -1 || x > 1 || double.IsNaN(x))
        {
            return NaN;
        }
        if (x == 1)
        {
            return double.PositiveInfinity;
        }
        if (x == -1)
        {
            return double.NegativeInfinity;
        }
        bool sign = false;
        if (x < 0)
        {
            x = -x;
            sign = true;
        }
        double temp;
        if (x < NearZero)
        {
            temp = x;
        }
        else if (x < 0.5)
        {
            temp = x + x;
            temp = 0.5 * Log1p(temp + temp * x / (1 - x));
        }
        else
        {
            temp = 0.5 * Log1p((x + x) / (1 - x));
        }
        return sign ? -temp : temp;
    }

    // --- Trigonometric helpers (Go's own Sin/Cos so Gamma/Bessel match) ---

    private const double PI4A = 7.85398125648498535156e-1;
    private const double PI4B = 3.77489470793079817668e-8;
    private const double PI4C = 2.69515142907905952645e-15;
    private const double ReduceThreshold = 1 << 29;

    private static readonly double[] SinCoef =
    {
        1.58962301576546568060e-10,
        -2.50507477628578072866e-8,
        2.75573136213857245213e-6,
        -1.98412698295895385996e-4,
        8.33333333332211858878e-3,
        -1.66666666666666307295e-1,
    };

    private static readonly double[] CosCoef =
    {
        -1.13585365213876817300e-11,
        2.08757008419747316778e-9,
        -2.75573141792967388112e-7,
        2.48015872888517045348e-5,
        -1.38888888888730564116e-3,
        4.16666666666665929218e-2,
    };

    private static readonly ulong[] MPi4 =
    {
        0x0000000000000001,
        0x45f306dc9c882a53,
        0xf84eafa3ea69bb81,
        0xb6c52b3278872083,
        0xfca2c757bd778ac3,
        0x6e48dc74849ba5c0,
        0x0c925dd413a32439,
        0xfc3bd63962534e7d,
        0xd1046bea5d768909,
        0xd338e04d68befc82,
        0x7323ac7306a673e9,
        0x3908bf177bf25076,
        0x3ff12fffbc0b301f,
        0xde5e2316b414da3e,
        0xda6cfd9e4f96136e,
        0x9e8c7ecd3cbfd45a,
        0xea4f758fd7cbe2f6,
        0x7a0e73ef14a525d4,
        0xd7f6bf623f1aba10,
        0xac06608df8f6d757,
    };

    private static double TrigReduce(double x, out ulong j)
    {
        const double Pi4 = Pi / 4;
        if (x < Pi4)
        {
            j = 0;
            return x;
        }
        unchecked
        {
            ulong ix = Float64bits(x);
            int exp = (int)((ix >> Shift) & Mask) - Bias - Shift;
            ix &= ~((ulong)Mask << Shift);
            ix |= 1UL << Shift;
            uint digit = (uint)(exp + 61) / 64;
            uint bitshift = (uint)(exp + 61) % 64;
            ulong z0 = ShlG(MPi4[digit], bitshift) | ShrG(MPi4[digit + 1], 64 - bitshift);
            ulong z1 = ShlG(MPi4[digit + 1], bitshift) | ShrG(MPi4[digit + 2], 64 - bitshift);
            ulong z2 = ShlG(MPi4[digit + 2], bitshift) | ShrG(MPi4[digit + 3], 64 - bitshift);
            ulong z2hi = Mul64(z2, ix, out _);
            ulong z1hi = Mul64(z1, ix, out ulong z1lo);
            ulong z0lo = z0 * ix;
            ulong lo = Add64(z1lo, z2hi, 0, out ulong c);
            ulong hi = Add64(z0lo, z1hi, c, out _);
            j = hi >> 61;
            hi = (hi << 3) | (lo >> 61);
            uint lz = (uint)LeadingZeros64(hi);
            ulong e = (ulong)(Bias - (lz + 1));
            hi = ShlG(hi, lz + 1) | ShrG(lo, 64 - (lz + 1));
            hi >>= 64 - Shift;
            hi |= e << Shift;
            double z = Float64frombits(hi);
            if ((j & 1) == 1)
            {
                j++;
                j &= 7;
                z--;
            }
            return z * Pi4;
        }
    }

    private static double ReduceOctant(double x, out ulong j)
    {
        if (x >= ReduceThreshold)
        {
            return TrigReduce(x, out j);
        }
        j = (ulong)(x * FourOverPi);
        double y = j;
        if ((j & 1) == 1)
        {
            j++;
            y++;
        }
        j &= 7;
        return ((x - y * PI4A) - y * PI4B) - y * PI4C;
    }

    private static double SinPoly(double z, double zz)
    {
        return z + z * zz * ((((((SinCoef[0] * zz) + SinCoef[1]) * zz + SinCoef[2]) * zz + SinCoef[3]) * zz + SinCoef[4]) * zz + SinCoef[5]);
    }

    private static double CosPoly(double zz)
    {
        return 1.0 - 0.5 * zz + zz * zz * ((((((CosCoef[0] * zz) + CosCoef[1]) * zz + CosCoef[2]) * zz + CosCoef[3]) * zz + CosCoef[4]) * zz + CosCoef[5]);
    }

    private static double Cos(double x)
    {
        if (double.IsNaN(x) || double.IsInfinity(x))
        {
            return NaN;
        }
        bool sign = false;
        x = Math.Abs(x);
        double z = ReduceOctant(x, out ulong j);
        if (j > 3)
        {
            j -= 4;
            sign = !sign;
        }
        if (j > 1)
        {
            sign = !sign;
        }
        double zz = z * z;
        double y = j == 1 || j == 2 ? SinPoly(z, zz) : CosPoly(zz);
        return sign ? -y : y;
    }

    private static double Sin(double x)
    {
        if (x == 0 || double.IsNaN(x))
        {
            return x;
        }
        if (double.IsInfinity(x))
        {
            return NaN;
        }
        bool sign = false;
        if (x < 0)
        {
            x = -x;
            sign = true;
        }
        double z = ReduceOctant(x, out ulong j);
        if (j > 3)
        {
            sign = !sign;
            j -= 4;
        }
        double zz = z * z;
        double y = j == 1 || j == 2 ? CosPoly(zz) : SinPoly(z, zz);
        return sign ? -y : y;
    }

    private static double Sincos(double x, out double cos)
    {
        if (x == 0)
        {
            cos = 1;
            return x;
        }
        if (double.IsNaN(x) || double.IsInfinity(x))
        {
            cos = NaN;
            return NaN;
        }
        bool sinSign = false, cosSign = false;
        if (x < 0)
        {
            x = -x;
            sinSign = true;
        }
        double z = ReduceOctant(x, out ulong j);
        if (j > 3)
        {
            j -= 4;
            sinSign = !sinSign;
            cosSign = !cosSign;
        }
        if (j > 1)
        {
            cosSign = !cosSign;
        }
        double zz = z * z;
        cos = CosPoly(zz);
        double sin = SinPoly(z, zz);
        if (j == 1 || j == 2)
        {
            double t = sin;
            sin = cos;
            cos = t;
        }
        if (cosSign)
        {
            cos = -cos;
        }
        return sinSign ? -sin : sin;
    }

    // --- Gamma ---

    private static readonly double[] GamP =
    {
        1.60119522476751861407e-04,
        1.19135147006586384913e-03,
        1.04213797561761569935e-02,
        4.76367800457137231464e-02,
        2.07448227648435975150e-01,
        4.94214826801497100753e-01,
        9.99999999999999996796e-01,
    };

    private static readonly double[] GamQ =
    {
        -2.31581873324120129819e-05,
        5.39605580493303397842e-04,
        -4.45641913851797240494e-03,
        1.18139785222060435552e-02,
        3.58236398605498653373e-02,
        -2.34591795718243348568e-01,
        7.14304917030273074085e-02,
        1.00000000000000000320e+00,
    };

    private static readonly double[] GamS =
    {
        7.87311395793093628397e-04,
        -2.29549961613378126380e-04,
        -2.68132617805781232825e-03,
        3.47222221605458667310e-03,
        8.33333333333482257126e-02,
    };

    private static double Stirling(double x, out double y2)
    {
        if (x > 200)
        {
            y2 = 1;
            return double.PositiveInfinity;
        }
        const double SqrtTwoPi = 2.506628274631000502417;
        const double MaxStirling = 143.01608;
        double w = 1 / x;
        w = 1 + w * ((((GamS[0] * w + GamS[1]) * w + GamS[2]) * w + GamS[3]) * w + GamS[4]);
        double y1 = Exp(x);
        y2 = 1.0;
        if (x > MaxStirling)
        {
            double v = Pow(x, 0.5 * x - 0.25);
            y2 = v / y1;
            y1 = v;
        }
        else
        {
            y1 = Pow(x, x - 0.5) / y1;
        }
        y2 = SqrtTwoPi * w * y2;
        return y1;
    }

    internal static double Gamma(double x)
    {
        const double Euler = 0.57721566490153286060651209008240243104215933593992;
        if (IsNegInt(x) || double.IsNegativeInfinity(x) || double.IsNaN(x))
        {
            return NaN;
        }
        if (double.IsPositiveInfinity(x))
        {
            return double.PositiveInfinity;
        }
        if (x == 0)
        {
            return Signbit(x) ? double.NegativeInfinity : double.PositiveInfinity;
        }
        double q = Math.Abs(x);
        double p = Math.Floor(q);
        if (q > 33)
        {
            if (x >= 0)
            {
                double y1 = Stirling(x, out double y2);
                return y1 * y2;
            }
            int signgam = 1;
            if (((long)p & 1) == 0)
            {
                signgam = -1;
            }
            double zf = q - p;
            if (zf > 0.5)
            {
                p++;
                zf = q - p;
            }
            zf = q * Sin(Pi * zf);
            if (zf == 0)
            {
                return signgam >= 0 ? double.PositiveInfinity : double.NegativeInfinity;
            }
            double sq1 = Stirling(q, out double sq2);
            double absz = Math.Abs(zf);
            double d = absz * sq1 * sq2;
            if (double.IsInfinity(d))
            {
                zf = Pi / absz / sq1 / sq2;
            }
            else
            {
                zf = Pi / d;
            }
            return signgam * zf;
        }

        double z = 1.0;
        while (x >= 3)
        {
            x--;
            z *= x;
        }
        while (x < 0)
        {
            if (x > -1e-09)
            {
                goto small;
            }
            z /= x;
            x++;
        }
        while (x < 2)
        {
            if (x < 1e-09)
            {
                goto small;
            }
            z /= x;
            x++;
        }
        if (x == 2)
        {
            return z;
        }
        x -= 2;
        p = (((((x * GamP[0] + GamP[1]) * x + GamP[2]) * x + GamP[3]) * x + GamP[4]) * x + GamP[5]) * x + GamP[6];
        q = ((((((x * GamQ[0] + GamQ[1]) * x + GamQ[2]) * x + GamQ[3]) * x + GamQ[4]) * x + GamQ[5]) * x + GamQ[6]) * x + GamQ[7];
        return z * p / q;

    small:
        if (x == 0)
        {
            return double.PositiveInfinity;
        }
        return z / ((1 + Euler * x) * x);
    }

    private static bool IsNegInt(double x)
    {
        if (x < 0)
        {
            Modf(x, out double xf);
            return xf == 0;
        }
        return false;
    }

    // --- Lgamma ---

    private static readonly double[] LgamA =
    {
        7.72156649015328655494e-02,
        3.22467033424113591611e-01,
        6.73523010531292681824e-02,
        2.05808084325167332806e-02,
        7.38555086081402883957e-03,
        2.89051383673415629091e-03,
        1.19270763183362067845e-03,
        5.10069792153511336608e-04,
        2.20862790713908385557e-04,
        1.08011567247583939954e-04,
        2.52144565451257326939e-05,
        4.48640949618915160150e-05,
    };

    private static readonly double[] LgamR =
    {
        1.0,
        1.39200533467621045958e+00,
        7.21935547567138069525e-01,
        1.71933865632803078993e-01,
        1.86459191715652901344e-02,
        7.77942496381893596434e-04,
        7.32668430744625636189e-06,
    };

    private static readonly double[] LgamS =
    {
        -7.72156649015328655494e-02,
        2.14982415960608852501e-01,
        3.25778796408930981787e-01,
        1.46350472652464452805e-01,
        2.66422703033638609560e-02,
        1.84028451407337715652e-03,
        3.19475326584100867617e-05,
    };

    private static readonly double[] LgamT =
    {
        4.83836122723810047042e-01,
        -1.47587722994593911752e-01,
        6.46249402391333854778e-02,
        -3.27885410759859649565e-02,
        1.79706750811820387126e-02,
        -1.03142241298341437450e-02,
        6.10053870246291332635e-03,
        -3.68452016781138256760e-03,
        2.25964780900612472250e-03,
        -1.40346469989232843813e-03,
        8.81081882437654011382e-04,
        -5.38595305356740546715e-04,
        3.15632070903625950361e-04,
        -3.12754168375120860518e-04,
        3.35529192635519073543e-04,
    };

    private static readonly double[] LgamU =
    {
        -7.72156649015328655494e-02,
        6.32827064025093366517e-01,
        1.45492250137234768737e+00,
        9.77717527963372745603e-01,
        2.28963728064692451092e-01,
        1.33810918536787660377e-02,
    };

    private static readonly double[] LgamV =
    {
        1.0,
        2.45597793713041134822e+00,
        2.12848976379893395361e+00,
        7.69285150456672783825e-01,
        1.04222645593369134254e-01,
        3.21709242282423911810e-03,
    };

    private static readonly double[] LgamW =
    {
        4.18938533204672725052e-01,
        8.33333333333329678849e-02,
        -2.77777777728775536470e-03,
        7.93650558643019558500e-04,
        -5.95187557450339963135e-04,
        8.36339918996282139126e-04,
        -1.63092934096575273989e-03,
    };

    internal static double Lgamma(double x, out int sign)
    {
        // Go folds these with exact arithmetic; values pre-rounded.
        const double YminM1P027 = 0.7316321449683623; // Ymin - 1 + 0.27
        const double YminM1M027 = 0.19163214496836226; // Ymin - 1 - 0.27
        const double YminP027 = 1.7316321449683623; // Ymin + 0.27
        const double YminM027 = 1.1916321449683622; // Ymin - 0.27
        const double TcM1 = 0.46163214496836225; // Tc - 1
        const double Two52 = 1L << 52;
        const double Two58 = 1L << 58;
        const double Tiny = 1.0 / (1L << 62) / (1 << 8);
        const double Tc = 1.46163214496836224576e+00;
        const double Tf = -1.21486290535849611461e-01;
        const double Tt = -3.63867699703950536541e-18;

        sign = 1;
        if (double.IsNaN(x) || double.IsInfinity(x))
        {
            return x;
        }
        if (x == 0)
        {
            return double.PositiveInfinity;
        }

        bool neg = false;
        if (x < 0)
        {
            x = -x;
            neg = true;
        }

        if (x < Tiny)
        {
            if (neg)
            {
                sign = -1;
            }
            return -Log(x);
        }
        double nadj = 0;
        if (neg)
        {
            if (x >= Two52)
            {
                return double.PositiveInfinity;
            }
            double t = SinPi(x);
            if (t == 0)
            {
                return double.PositiveInfinity;
            }
            nadj = Log(Pi / Math.Abs(t * x));
            if (t < 0)
            {
                sign = -1;
            }
        }

        double lgamma;
        if (x == 1 || x == 2)
        {
            return 0;
        }
        else if (x < 2)
        {
            double y;
            int i;
            if (x <= 0.9)
            {
                lgamma = -Log(x);
                if (x >= YminM1P027)
                {
                    y = 1 - x;
                    i = 0;
                }
                else if (x >= YminM1M027)
                {
                    y = x - TcM1;
                    i = 1;
                }
                else
                {
                    y = x;
                    i = 2;
                }
            }
            else
            {
                lgamma = 0;
                if (x >= YminP027)
                {
                    y = 2 - x;
                    i = 0;
                }
                else if (x >= YminM027)
                {
                    y = x - Tc;
                    i = 1;
                }
                else
                {
                    y = x - 1;
                    i = 2;
                }
            }
            if (i == 0)
            {
                double z = y * y;
                double p1 = LgamA[0] + z * (LgamA[2] + z * (LgamA[4] + z * (LgamA[6] + z * (LgamA[8] + z * LgamA[10]))));
                double p2 = z * (LgamA[1] + z * (+LgamA[3] + z * (LgamA[5] + z * (LgamA[7] + z * (LgamA[9] + z * LgamA[11])))));
                double p = y * p1 + p2;
                lgamma += p - 0.5 * y;
            }
            else if (i == 1)
            {
                double z = y * y;
                double w = z * y;
                double p1 = LgamT[0] + w * (LgamT[3] + w * (LgamT[6] + w * (LgamT[9] + w * LgamT[12])));
                double p2 = LgamT[1] + w * (LgamT[4] + w * (LgamT[7] + w * (LgamT[10] + w * LgamT[13])));
                double p3 = LgamT[2] + w * (LgamT[5] + w * (LgamT[8] + w * (LgamT[11] + w * LgamT[14])));
                double p = z * p1 - (Tt - w * (p2 + y * p3));
                lgamma += Tf + p;
            }
            else
            {
                double p1 = y * (LgamU[0] + y * (LgamU[1] + y * (LgamU[2] + y * (LgamU[3] + y * (LgamU[4] + y * LgamU[5])))));
                double p2 = 1 + y * (LgamV[1] + y * (LgamV[2] + y * (LgamV[3] + y * (LgamV[4] + y * LgamV[5]))));
                lgamma += -0.5 * y + p1 / p2;
            }
        }
        else if (x < 8)
        {
            int i = (int)x;
            double y = x - i;
            double p = y * (LgamS[0] + y * (LgamS[1] + y * (LgamS[2] + y * (LgamS[3] + y * (LgamS[4] + y * (LgamS[5] + y * LgamS[6]))))));
            double q = 1 + y * (LgamR[1] + y * (LgamR[2] + y * (LgamR[3] + y * (LgamR[4] + y * (LgamR[5] + y * LgamR[6])))));
            lgamma = 0.5 * y + p / q;
            double z = 1.0;
            if (i >= 7)
            {
                z *= y + 6;
            }
            if (i >= 6)
            {
                z *= y + 5;
            }
            if (i >= 5)
            {
                z *= y + 4;
            }
            if (i >= 4)
            {
                z *= y + 3;
            }
            if (i >= 3)
            {
                z *= y + 2;
                lgamma += Log(z);
            }
        }
        else if (x < Two58)
        {
            double t = Log(x);
            double z = 1 / x;
            double y = z * z;
            double w = LgamW[0] + z * (LgamW[1] + y * (LgamW[2] + y * (LgamW[3] + y * (LgamW[4] + y * (LgamW[5] + y * LgamW[6])))));
            lgamma = (x - 0.5) * (t - 1) + w;
        }
        else
        {
            lgamma = x * (Log(x) - 1);
        }
        if (neg)
        {
            lgamma = nadj - lgamma;
        }
        return lgamma;
    }

    private static double SinPi(double x)
    {
        const double Two52 = 1L << 52;
        const double Two53 = 1L << 53;
        if (x < 0.25)
        {
            return -Sin(Pi * x);
        }
        double z = Math.Floor(x);
        int n;
        if (z != x)
        {
            x %= 2;
            n = (int)(x * 4);
        }
        else
        {
            if (x >= Two53)
            {
                x = 0;
                n = 0;
            }
            else
            {
                if (x < Two52)
                {
                    z = x + Two52;
                }
                n = (int)(1 & Float64bits(z));
                x = n;
                n <<= 2;
            }
        }
        switch (n)
        {
            case 0:
                x = Sin(Pi * x);
                break;
            case 1:
            case 2:
                x = Cos(Pi * (0.5 - x));
                break;
            case 3:
            case 4:
                x = Sin(Pi * (1 - x));
                break;
            case 5:
            case 6:
                x = -Cos(Pi * (x - 1.5));
                break;
            default:
                x = Sin(Pi * (x - 2));
                break;
        }
        return -x;
    }

    // --- Erf / Erfc ---

    private const double Erx = 8.45062911510467529297e-01;
    private const double Efx = 1.28379167095512586316e-01;
    private const double Efx8 = 1.02703333676410069053e+00;
    private const double Pp0 = 1.28379167095512558561e-01;
    private const double Pp1 = -3.25042107247001499370e-01;
    private const double Pp2 = -2.84817495755985104766e-02;
    private const double Pp3 = -5.77027029648944159157e-03;
    private const double Pp4 = -2.37630166566501626084e-05;
    private const double Qq1 = 3.97917223959155352819e-01;
    private const double Qq2 = 6.50222499887672944485e-02;
    private const double Qq3 = 5.08130628187576562776e-03;
    private const double Qq4 = 1.32494738004321644526e-04;
    private const double Qq5 = -3.96022827877536812320e-06;
    private const double Pa0 = -2.36211856075265944077e-03;
    private const double Pa1 = 4.14856118683748331666e-01;
    private const double Pa2 = -3.72207876035701323847e-01;
    private const double Pa3 = 3.18346619901161753674e-01;
    private const double Pa4 = -1.10894694282396677476e-01;
    private const double Pa5 = 3.54783043256182359371e-02;
    private const double Pa6 = -2.16637559486879084300e-03;
    private const double Qa1 = 1.06420880400844228286e-01;
    private const double Qa2 = 5.40397917702171048937e-01;
    private const double Qa3 = 7.18286544141962662868e-02;
    private const double Qa4 = 1.26171219808761642112e-01;
    private const double Qa5 = 1.36370839120290507362e-02;
    private const double Qa6 = 1.19844998467991074170e-02;
    private const double Ra0 = -9.86494403484714822705e-03;
    private const double Ra1 = -6.93858572707181764372e-01;
    private const double Ra2 = -1.05586262253232909814e+01;
    private const double Ra3 = -6.23753324503260060396e+01;
    private const double Ra4 = -1.62396669462573470355e+02;
    private const double Ra5 = -1.84605092906711035994e+02;
    private const double Ra6 = -8.12874355063065934246e+01;
    private const double Ra7 = -9.81432934416914548592e+00;
    private const double Sa1 = 1.96512716674392571292e+01;
    private const double Sa2 = 1.37657754143519042600e+02;
    private const double Sa3 = 4.34565877475229228821e+02;
    private const double Sa4 = 6.45387271733267880336e+02;
    private const double Sa5 = 4.29008140027567833386e+02;
    private const double Sa6 = 1.08635005541779435134e+02;
    private const double Sa7 = 6.57024977031928170135e+00;
    private const double Sa8 = -6.04244152148580987438e-02;
    private const double Rb0 = -9.86494292470009928597e-03;
    private const double Rb1 = -7.99283237680523006574e-01;
    private const double Rb2 = -1.77579549177547519889e+01;
    private const double Rb3 = -1.60636384855821916062e+02;
    private const double Rb4 = -6.37566443368389627722e+02;
    private const double Rb5 = -1.02509513161107724954e+03;
    private const double Rb6 = -4.83519191608651397019e+02;
    private const double Sb1 = 3.03380607434824582924e+01;
    private const double Sb2 = 3.25792512996573918826e+02;
    private const double Sb3 = 1.53672958608443695994e+03;
    private const double Sb4 = 3.19985821950859553908e+03;
    private const double Sb5 = 2.55305040643316442583e+03;
    private const double Sb6 = 4.74528541206955367215e+02;
    private const double Sb7 = -2.24409524465858183362e+01;
    private const double OneOver035 = 2.857142857142857; // 1/0.35

    internal static double Erf(double x)
    {
        const double VeryTiny = 2.848094538889218e-306;
        const double Small = 1.0 / (1 << 28);
        if (double.IsNaN(x))
        {
            return NaN;
        }
        if (double.IsPositiveInfinity(x))
        {
            return 1;
        }
        if (double.IsNegativeInfinity(x))
        {
            return -1;
        }
        bool sign = false;
        if (x < 0)
        {
            x = -x;
            sign = true;
        }
        if (x < 0.84375)
        {
            double temp;
            if (x < Small)
            {
                if (x < VeryTiny)
                {
                    temp = 0.125 * (8.0 * x + Efx8 * x);
                }
                else
                {
                    temp = x + Efx * x;
                }
            }
            else
            {
                double z = x * x;
                double r = Pp0 + z * (Pp1 + z * (Pp2 + z * (Pp3 + z * Pp4)));
                double s = 1 + z * (Qq1 + z * (Qq2 + z * (Qq3 + z * (Qq4 + z * Qq5))));
                double y = r / s;
                temp = x + x * y;
            }
            return sign ? -temp : temp;
        }
        if (x < 1.25)
        {
            double s = x - 1;
            double p = Pa0 + s * (Pa1 + s * (Pa2 + s * (Pa3 + s * (Pa4 + s * (Pa5 + s * Pa6)))));
            double q = 1 + s * (Qa1 + s * (Qa2 + s * (Qa3 + s * (Qa4 + s * (Qa5 + s * Qa6)))));
            if (sign)
            {
                return -Erx - p / q;
            }
            return Erx + p / q;
        }
        if (x >= 6)
        {
            return sign ? -1 : 1;
        }
        double rr = ErfTail(x);
        if (sign)
        {
            return rr / x - 1;
        }
        return 1 - rr / x;
    }

    private static double ErfTail(double x)
    {
        double s = 1 / (x * x);
        double r, ss;
        if (x < OneOver035)
        {
            r = Ra0 + s * (Ra1 + s * (Ra2 + s * (Ra3 + s * (Ra4 + s * (Ra5 + s * (Ra6 + s * Ra7))))));
            ss = 1 + s * (Sa1 + s * (Sa2 + s * (Sa3 + s * (Sa4 + s * (Sa5 + s * (Sa6 + s * (Sa7 + s * Sa8)))))));
        }
        else
        {
            r = Rb0 + s * (Rb1 + s * (Rb2 + s * (Rb3 + s * (Rb4 + s * (Rb5 + s * Rb6)))));
            ss = 1 + s * (Sb1 + s * (Sb2 + s * (Sb3 + s * (Sb4 + s * (Sb5 + s * (Sb6 + s * Sb7))))));
        }
        double z = Float64frombits(Float64bits(x) & 0xffffffff00000000);
        return Exp(-z * z - 0.5625) * Exp((z - x) * (z + x) + r / ss);
    }

    internal static double Erfc(double x)
    {
        const double Tiny = 1.0 / (1L << 56);
        if (double.IsNaN(x))
        {
            return NaN;
        }
        if (double.IsPositiveInfinity(x))
        {
            return 0;
        }
        if (double.IsNegativeInfinity(x))
        {
            return 2;
        }
        bool sign = false;
        if (x < 0)
        {
            x = -x;
            sign = true;
        }
        if (x < 0.84375)
        {
            double temp;
            if (x < Tiny)
            {
                temp = x;
            }
            else
            {
                double z = x * x;
                double r = Pp0 + z * (Pp1 + z * (Pp2 + z * (Pp3 + z * Pp4)));
                double s = 1 + z * (Qq1 + z * (Qq2 + z * (Qq3 + z * (Qq4 + z * Qq5))));
                double y = r / s;
                if (x < 0.25)
                {
                    temp = x + x * y;
                }
                else
                {
                    temp = 0.5 + (x * y + (x - 0.5));
                }
            }
            return sign ? 1 + temp : 1 - temp;
        }
        if (x < 1.25)
        {
            double s = x - 1;
            double p = Pa0 + s * (Pa1 + s * (Pa2 + s * (Pa3 + s * (Pa4 + s * (Pa5 + s * Pa6)))));
            double q = 1 + s * (Qa1 + s * (Qa2 + s * (Qa3 + s * (Qa4 + s * (Qa5 + s * Qa6)))));
            if (sign)
            {
                return 1 + Erx + p / q;
            }
            return 1 - Erx - p / q;
        }
        if (x < 28)
        {
            if (sign && x > 6 && x >= OneOver035)
            {
                return 2;
            }
            double r = ErfTail(x);
            if (sign)
            {
                return 2 - r / x;
            }
            return r / x;
        }
        return sign ? 2 : 0;
    }

    // --- Bessel functions ---

    private const double Two129 = 6.80564733841877e+38;
    private const double Two302 = 8.148143905337944e+90;

    // Shared asymptotic form for x >= 2: returns (u*cc - v*ss) style combination inputs.
    private static void BesselPhase(double x, bool order1, out double ss, out double cc)
    {
        double s = Sincos(x, out double c);
        if (!order1)
        {
            ss = s - c;
            cc = s + c;
            if (x < MaxFloat64 / 2)
            {
                double z = -Cos(x + x);
                if (s * c < 0)
                {
                    cc = z / ss;
                }
                else
                {
                    ss = z / cc;
                }
            }
        }
        else
        {
            ss = -s - c;
            cc = s - c;
            if (x < MaxFloat64 / 2)
            {
                double z = Cos(x + x);
                if (s * c > 0)
                {
                    cc = z / ss;
                }
                else
                {
                    ss = z / cc;
                }
            }
        }
    }

    internal static double J0(double x)
    {
        const double TwoM27 = 1.0 / (1 << 27);
        const double TwoM13 = 1.0 / (1 << 13);
        const double R02 = 1.56249999999999947958e-02;
        const double R03 = -1.89979294238854721751e-04;
        const double R04 = 1.82954049532700665670e-06;
        const double R05 = -4.61832688532103189199e-09;
        const double S01 = 1.56191029464890010492e-02;
        const double S02 = 1.16926784663337450260e-04;
        const double S03 = 5.13546550207318111446e-07;
        const double S04 = 1.16614003333790000205e-09;
        if (double.IsNaN(x))
        {
            return x;
        }
        if (double.IsInfinity(x))
        {
            return 0;
        }
        if (x == 0)
        {
            return 1;
        }
        x = Math.Abs(x);
        if (x >= 2)
        {
            BesselPhase(x, false, out double ss, out double cc);
            if (x > Two129)
            {
                return InvSqrtPi * cc / Math.Sqrt(x);
            }
            double u = PZero(x);
            double v = QZero(x);
            return InvSqrtPi * (u * cc - v * ss) / Math.Sqrt(x);
        }
        if (x < TwoM13)
        {
            if (x < TwoM27)
            {
                return 1;
            }
            return 1 - 0.25 * x * x;
        }
        double zz = x * x;
        double r = zz * (R02 + zz * (R03 + zz * (R04 + zz * R05)));
        double s = 1 + zz * (S01 + zz * (S02 + zz * (S03 + zz * S04)));
        if (x < 1)
        {
            return 1 + zz * (-0.25 + (r / s));
        }
        double uu = 0.5 * x;
        return (1 + uu) * (1 - uu) + zz * (r / s);
    }

    internal static double Y0(double x)
    {
        const double TwoM27 = 1.0 / (1 << 27);
        const double U00 = -7.38042951086872317523e-02;
        const double U01 = 1.76666452509181115538e-01;
        const double U02 = -1.38185671945596898896e-02;
        const double U03 = 3.47453432093683650238e-04;
        const double U04 = -3.81407053724364161125e-06;
        const double U05 = 1.95590137035022920206e-08;
        const double U06 = -3.98205194132103398453e-11;
        const double V01 = 1.27304834834123699328e-02;
        const double V02 = 7.60068627350353253702e-05;
        const double V03 = 2.59150851840457805467e-07;
        const double V04 = 4.41110311332675467403e-10;
        if (x < 0 || double.IsNaN(x))
        {
            return NaN;
        }
        if (double.IsPositiveInfinity(x))
        {
            return 0;
        }
        if (x == 0)
        {
            return double.NegativeInfinity;
        }
        if (x >= 2)
        {
            BesselPhase(x, false, out double ss, out double cc);
            if (x > Two129)
            {
                return InvSqrtPi * ss / Math.Sqrt(x);
            }
            double pu = PZero(x);
            double pv = QZero(x);
            return InvSqrtPi * (pu * ss + pv * cc) / Math.Sqrt(x);
        }
        if (x <= TwoM27)
        {
            return U00 + TwoOverPi * Log(x);
        }
        double z = x * x;
        double u = U00 + z * (U01 + z * (U02 + z * (U03 + z * (U04 + z * (U05 + z * U06)))));
        double v = 1 + z * (V01 + z * (V02 + z * (V03 + z * V04)));
        return u / v + TwoOverPi * J0(x) * Log(x);
    }

    internal static double J1(double x)
    {
        const double TwoM27 = 1.0 / (1 << 27);
        const double R00 = -6.25000000000000000000e-02;
        const double R01 = 1.40705666955189706048e-03;
        const double R02 = -1.59955631084035597520e-05;
        const double R03 = 4.96727999609584448412e-08;
        const double S01 = 1.91537599538363460805e-02;
        const double S02 = 1.85946785588630915560e-04;
        const double S03 = 1.17718464042623683263e-06;
        const double S04 = 5.04636257076217042715e-09;
        const double S05 = 1.23542274426137913908e-11;
        if (double.IsNaN(x))
        {
            return x;
        }
        if (double.IsInfinity(x) || x == 0)
        {
            return 0;
        }
        bool sign = false;
        if (x < 0)
        {
            x = -x;
            sign = true;
        }
        double z;
        if (x >= 2)
        {
            BesselPhase(x, true, out double ss, out double cc);
            if (x > Two129)
            {
                z = InvSqrtPi * cc / Math.Sqrt(x);
            }
            else
            {
                double u = POne(x);
                double v = QOne(x);
                z = InvSqrtPi * (u * cc - v * ss) / Math.Sqrt(x);
            }
            return sign ? -z : z;
        }
        if (x < TwoM27)
        {
            return 0.5 * x;
        }
        z = x * x;
        double r = z * (R00 + z * (R01 + z * (R02 + z * R03)));
        double s = 1.0 + z * (S01 + z * (S02 + z * (S03 + z * (S04 + z * S05))));
        r *= x;
        z = 0.5 * x + r / s;
        return sign ? -z : z;
    }

    internal static double Y1(double x)
    {
        const double TwoM54 = 1.0 / (1L << 54);
        const double U00 = -1.96057090646238940668e-01;
        const double U01 = 5.04438716639811282616e-02;
        const double U02 = -1.91256895875763547298e-03;
        const double U03 = 2.35252600561610495928e-05;
        const double U04 = -9.19099158039878874504e-08;
        const double V00 = 1.99167318236649903973e-02;
        const double V01 = 2.02552581025135171496e-04;
        const double V02 = 1.35608801097516229404e-06;
        const double V03 = 6.22741452364621501295e-09;
        const double V04 = 1.66559246207992079114e-11;
        if (x < 0 || double.IsNaN(x))
        {
            return NaN;
        }
        if (double.IsPositiveInfinity(x))
        {
            return 0;
        }
        if (x == 0)
        {
            return double.NegativeInfinity;
        }
        if (x >= 2)
        {
            BesselPhase(x, true, out double ss, out double cc);
            if (x > Two129)
            {
                return InvSqrtPi * ss / Math.Sqrt(x);
            }
            double pu = POne(x);
            double pv = QOne(x);
            return InvSqrtPi * (pu * ss + pv * cc) / Math.Sqrt(x);
        }
        if (x <= TwoM54)
        {
            return -TwoOverPi / x;
        }
        double z = x * x;
        double u = U00 + z * (U01 + z * (U02 + z * (U03 + z * U04)));
        double v = 1 + z * (V00 + z * (V01 + z * (V02 + z * (V03 + z * V04))));
        return x * (u / v) + TwoOverPi * (J1(x) * Log(x) - 1 / x);
    }

    private static readonly double[] P0R8 = { 0.00000000000000000000e+00, -7.03124999999900357484e-02, -8.08167041275349795626e+00, -2.57063105679704847262e+02, -2.48521641009428822144e+03, -5.25304380490729545272e+03 };
    private static readonly double[] P0S8 = { 1.16534364619668181717e+02, 3.83374475364121826715e+03, 4.05978572648472545552e+04, 1.16752972564375915681e+05, 4.76277284146730962675e+04 };
    private static readonly double[] P0R5 = { -1.14125464691894502584e-11, -7.03124940873599280078e-02, -4.15961064470587782438e+00, -6.76747652265167261021e+01, -3.31231299649172967747e+02, -3.46433388365604912451e+02 };
    private static readonly double[] P0S5 = { 6.07539382692300335975e+01, 1.05125230595704579173e+03, 5.97897094333855784498e+03, 9.62544514357774460223e+03, 2.40605815922939109441e+03 };
    private static readonly double[] P0R3 = { -2.54704601771951915620e-09, -7.03119616381481654654e-02, -2.40903221549529611423e+00, -2.19659774734883086467e+01, -5.80791704701737572236e+01, -3.14479470594888503854e+01 };
    private static readonly double[] P0S3 = { 3.58560338055209726349e+01, 3.61513983050303863820e+02, 1.19360783792111533330e+03, 1.12799679856907414432e+03, 1.73580930813335754692e+02 };
    private static readonly double[] P0R2 = { -8.87534333032526411254e-08, -7.03030995483624743247e-02, -1.45073846780952986357e+00, -7.63569613823527770791e+00, -1.11931668860356747786e+01, -3.23364579351335335033e+00 };
    private static readonly double[] P0S2 = { 2.22202997532088808441e+01, 1.36206794218215208048e+02, 2.70470278658083486789e+02, 1.53875394208320329881e+02, 1.46576176948256193810e+01 };

    private static readonly double[] Q0R8 = { 0.00000000000000000000e+00, 7.32421874999935051953e-02, 1.17682064682252693899e+01, 5.57673380256401856059e+02, 8.85919720756468632317e+03, 3.70146267776887834771e+04 };
    private static readonly double[] Q0S8 = { 1.63776026895689824414e+02, 8.09834494656449805916e+03, 1.42538291419120476348e+05, 8.03309257119514397345e+05, 8.40501579819060512818e+05, -3.43899293537866615225e+05 };
    private static readonly double[] Q0R5 = { 1.84085963594515531381e-11, 7.32421766612684765896e-02, 5.83563508962056953777e+00, 1.35111577286449829671e+02, 1.02724376596164097464e+03, 1.98997785864605384631e+03 };
    private static readonly double[] Q0S5 = { 8.27766102236537761883e+01, 2.07781416421392987104e+03, 1.88472887785718085070e+04, 5.67511122894947329769e+04, 3.59767538425114471465e+04, -5.35434275601944773371e+03 };
    private static readonly double[] Q0R3 = { 4.37741014089738620906e-09, 7.32411180042911447163e-02, 3.34423137516170720929e+00, 4.26218440745412650017e+01, 1.70808091340565596283e+02, 1.66733948696651168575e+02 };
    private static readonly double[] Q0S3 = { 4.87588729724587182091e+01, 7.09689221056606015736e+02, 3.70414822620111362994e+03, 6.46042516752568917582e+03, 2.51633368920368957333e+03, -1.49247451836156386662e+02 };
    private static readonly double[] Q0R2 = { 1.50444444886983272379e-07, 7.32234265963079278272e-02, 1.99819174093815998816e+00, 1.44956029347885735348e+01, 3.16662317504781540833e+01, 1.62527075710929267416e+01 };
    private static readonly double[] Q0S2 = { 3.03655848355219184498e+01, 2.69348118608049844624e+02, 8.44783757595320139444e+02, 8.82935845112488550512e+02, 2.12666388511798828631e+02, -5.31095493882666946917e+00 };

    private static readonly double[] P1R8 = { 0.00000000000000000000e+00, 1.17187499999988647970e-01, 1.32394806593073575129e+01, 4.12051854307378562225e+02, 3.87474538913960532227e+03, 7.91447954031891731574e+03 };
    private static readonly double[] P1S8 = { 1.14207370375678408436e+02, 3.65093083420853463394e+03, 3.69562060269033463555e+04, 9.76027935934950801311e+04, 3.08042720627888811578e+04 };
    private static readonly double[] P1R5 = { 1.31990519556243522749e-11, 1.17187493190614097638e-01, 6.80275127868432871736e+00, 1.08308182990189109773e+02, 5.17636139533199752805e+02, 5.28715201363337541807e+02 };
    private static readonly double[] P1S5 = { 5.92805987221131331921e+01, 9.91401418733614377743e+02, 5.35326695291487976647e+03, 7.84469031749551231769e+03, 1.50404688810361062679e+03 };
    private static readonly double[] P1R3 = { 3.02503916137373618024e-09, 1.17186865567253592491e-01, 3.93297750033315640650e+00, 3.51194035591636932736e+01, 9.10550110750781271918e+01, 4.85590685197364919645e+01 };
    private static readonly double[] P1S3 = { 3.47913095001251519989e+01, 3.36762458747825746741e+02, 1.04687139975775130551e+03, 8.90811346398256432622e+02, 1.03787932439639277504e+02 };
    private static readonly double[] P1R2 = { 1.07710830106873743082e-07, 1.17176219462683348094e-01, 2.36851496667608785174e+00, 1.22426109148261232917e+01, 1.76939711271687727390e+01, 5.07352312588818499250e+00 };
    private static readonly double[] P1S2 = { 2.14364859363821409488e+01, 1.25290227168402751090e+02, 2.32276469057162813669e+02, 1.17679373287147100768e+02, 8.36463893371618283368e+00 };

    private static readonly double[] Q1R8 = { 0.00000000000000000000e+00, -1.02539062499992714161e-01, -1.62717534544589987888e+01, -7.59601722513950107896e+02, -1.18498066702429587167e+04, -4.84385124285750353010e+04 };
    private static readonly double[] Q1S8 = { 1.61395369700722909556e+02, 7.82538599923348465381e+03, 1.33875336287249578163e+05, 7.19657723683240939863e+05, 6.66601232617776375264e+05, -2.94490264303834643215e+05 };
    private static readonly double[] Q1R5 = { -2.08979931141764104297e-11, -1.02539050241375426231e-01, -8.05644828123936029840e+00, -1.83669607474888380239e+02, -1.37319376065508163265e+03, -2.61244440453215656817e+03 };
    private static readonly double[] Q1S5 = { 8.12765501384335777857e+01, 1.99179873460485964642e+03, 1.74684851924908907677e+04, 4.98514270910352279316e+04, 2.79480751638918118260e+04, -4.71918354795128470869e+03 };
    private static readonly double[] Q1R3 = { -5.07831226461766561369e-09, -1.02537829820837089745e-01, -4.61011581139473403113e+00, -5.78472216562783643212e+01, -2.28244540737631695038e+02, -2.19210128478909325622e+02 };
    private static readonly double[] Q1S3 = { 4.76651550323729509273e+01, 6.73865112676699709482e+02, 3.38015286679526343505e+03, 5.54772909720722782367e+03, 1.90311919338810798763e+03, -1.35201191444307340817e+02 };
    private static readonly double[] Q1R2 = { -1.78381727510958865572e-07, -1.02517042607985553460e-01, -2.75220568278187460720e+00, -1.96636162643703720221e+01, -4.23253133372830490089e+01, -2.13719211703704061733e+01 };
    private static readonly double[] Q1S2 = { 2.95333629060523854548e+01, 2.52981549982190529136e+02, 7.57502834868645436472e+02, 7.39393205320467245656e+02, 1.55949003336666123687e+02, -4.95949898822628210127e+00 };

    private static void SelectTable(double x, double[] r8, double[] s8, double[] r5, double[] s5, double[] r3, double[] s3, double[] r2, double[] s2, out double[] p, out double[] q)
    {
        if (x >= 8)
        {
            p = r8;
            q = s8;
        }
        else if (x >= 4.5454)
        {
            p = r5;
            q = s5;
        }
        else if (x >= 2.8571)
        {
            p = r3;
            q = s3;
        }
        else
        {
            p = r2;
            q = s2;
        }
    }

    private static double PZero(double x)
    {
        SelectTable(x, P0R8, P0S8, P0R5, P0S5, P0R3, P0S3, P0R2, P0S2, out double[] p, out double[] q);
        double z = 1 / (x * x);
        double r = p[0] + z * (p[1] + z * (p[2] + z * (p[3] + z * (p[4] + z * p[5]))));
        double s = 1 + z * (q[0] + z * (q[1] + z * (q[2] + z * (q[3] + z * q[4]))));
        return 1 + r / s;
    }

    private static double QZero(double x)
    {
        SelectTable(x, Q0R8, Q0S8, Q0R5, Q0S5, Q0R3, Q0S3, Q0R2, Q0S2, out double[] p, out double[] q);
        double z = 1 / (x * x);
        double r = p[0] + z * (p[1] + z * (p[2] + z * (p[3] + z * (p[4] + z * p[5]))));
        double s = 1 + z * (q[0] + z * (q[1] + z * (q[2] + z * (q[3] + z * (q[4] + z * q[5])))));
        return (-0.125 + r / s) / x;
    }

    private static double POne(double x)
    {
        SelectTable(x, P1R8, P1S8, P1R5, P1S5, P1R3, P1S3, P1R2, P1S2, out double[] p, out double[] q);
        double z = 1 / (x * x);
        double r = p[0] + z * (p[1] + z * (p[2] + z * (p[3] + z * (p[4] + z * p[5]))));
        double s = 1.0 + z * (q[0] + z * (q[1] + z * (q[2] + z * (q[3] + z * q[4]))));
        return 1 + r / s;
    }

    private static double QOne(double x)
    {
        SelectTable(x, Q1R8, Q1S8, Q1R5, Q1S5, Q1R3, Q1S3, Q1R2, Q1S2, out double[] p, out double[] q);
        double z = 1 / (x * x);
        double r = p[0] + z * (p[1] + z * (p[2] + z * (p[3] + z * (p[4] + z * p[5]))));
        double s = 1 + z * (q[0] + z * (q[1] + z * (q[2] + z * (q[3] + z * (q[4] + z * q[5])))));
        return (0.375 + r / s) / x;
    }

    internal static double Jn(int n, double x)
    {
        const double TwoM29 = 1.0 / (1 << 29);
        if (double.IsNaN(x))
        {
            return x;
        }
        if (double.IsInfinity(x))
        {
            return 0;
        }
        if (n == 0)
        {
            return J0(x);
        }
        if (x == 0)
        {
            return 0;
        }
        if (n < 0)
        {
            n = -n;
            x = -x;
        }
        if (n == 1)
        {
            return J1(x);
        }
        bool sign = false;
        if (x < 0)
        {
            x = -x;
            if ((n & 1) == 1)
            {
                sign = true;
            }
        }
        double b;
        if (n <= x)
        {
            if (x >= Two302)
            {
                double s = Sincos(x, out double c);
                double temp = (n & 3) switch
                {
                    0 => c + s,
                    1 => -c + s,
                    2 => -c - s,
                    _ => c - s,
                };
                b = InvSqrtPi * temp / Math.Sqrt(x);
            }
            else
            {
                b = J1(x);
                double a = J0(x);
                for (int i = 1; i < n; i++)
                {
                    double t = b;
                    b = b * ((double)(i + i) / x) - a;
                    a = t;
                }
            }
        }
        else
        {
            if (x < TwoM29)
            {
                if (n > 33)
                {
                    b = 0;
                }
                else
                {
                    double temp = x * 0.5;
                    b = temp;
                    double a = 1.0;
                    for (int i = 2; i <= n; i++)
                    {
                        a *= i;
                        b *= temp;
                    }
                    b /= a;
                }
            }
            else
            {
                double w = (double)(n + n) / x;
                double h = 2 / x;
                double q0 = w;
                double z = w + h;
                double q1 = w * z - 1;
                int k = 1;
                while (q1 < 1e9)
                {
                    k++;
                    z += h;
                    double tq = q1;
                    q1 = z * q1 - q0;
                    q0 = tq;
                }
                int m = n + n;
                double t = 0.0;
                for (int i = 2 * (n + k); i >= m; i -= 2)
                {
                    t = 1 / (i / x - t);
                }
                double a = t;
                b = 1;
                double tmp = n;
                double v = 2 / x;
                tmp *= Log(Math.Abs(v * tmp));
                if (tmp < 7.09782712893383973096e+02)
                {
                    for (int i = n - 1; i > 0; i--)
                    {
                        double di = i + i;
                        double tb = b;
                        b = b * di / x - a;
                        a = tb;
                    }
                }
                else
                {
                    for (int i = n - 1; i > 0; i--)
                    {
                        double di = i + i;
                        double tb = b;
                        b = b * di / x - a;
                        a = tb;
                        if (b > 1e100)
                        {
                            a /= b;
                            t /= b;
                            b = 1;
                        }
                    }
                }
                b = t * J0(x) / b;
            }
        }
        return sign ? -b : b;
    }

    internal static double Yn(int n, double x)
    {
        if (x < 0 || double.IsNaN(x))
        {
            return NaN;
        }
        if (double.IsPositiveInfinity(x))
        {
            return 0;
        }
        if (n == 0)
        {
            return Y0(x);
        }
        if (x == 0)
        {
            if (n < 0 && (n & 1) == 1)
            {
                return double.PositiveInfinity;
            }
            return double.NegativeInfinity;
        }
        bool sign = false;
        if (n < 0)
        {
            n = -n;
            if ((n & 1) == 1)
            {
                sign = true;
            }
        }
        if (n == 1)
        {
            return sign ? -Y1(x) : Y1(x);
        }
        double b;
        if (x >= Two302)
        {
            double s = Sincos(x, out double c);
            double temp = (n & 3) switch
            {
                0 => s - c,
                1 => -s - c,
                2 => -s + c,
                _ => s + c,
            };
            b = InvSqrtPi * temp / Math.Sqrt(x);
        }
        else
        {
            double a = Y0(x);
            b = Y1(x);
            for (int i = 1; i < n && !double.IsNegativeInfinity(b); i++)
            {
                double t = b;
                b = (double)(i + i) / x * b - a;
                a = t;
            }
        }
        return sign ? -b : b;
    }

    // --- Fused multiply-add (software, exact) ---

    private static ulong Zero(ulong x)
    {
        return x == 0 ? 1UL : 0UL;
    }

    private static ulong Nonzero(ulong x)
    {
        return x != 0 ? 1UL : 0UL;
    }

    private static void Shl128(ulong u1, ulong u2, uint n, out ulong r1, out ulong r2)
    {
        unchecked
        {
            r1 = ShlG(u1, n) | ShrG(u2, 64 - n) | ShlG(u2, n - 64);
            r2 = ShlG(u2, n);
        }
    }

    private static void Shr128(ulong u1, ulong u2, uint n, out ulong r1, out ulong r2)
    {
        unchecked
        {
            r2 = ShrG(u2, n) | ShlG(u1, 64 - n) | ShrG(u1, n - 64);
            r1 = ShrG(u1, n);
        }
    }

    private static void ShrCompress(ulong u1, ulong u2, uint n, out ulong r1, out ulong r2)
    {
        unchecked
        {
            if (n == 0)
            {
                r1 = u1;
                r2 = u2;
            }
            else if (n == 64)
            {
                r1 = 0;
                r2 = u1 | Nonzero(u2);
            }
            else if (n >= 128)
            {
                r1 = 0;
                r2 = Nonzero(u1 | u2);
            }
            else if (n < 64)
            {
                Shr128(u1, u2, n, out r1, out r2);
                r2 |= Nonzero(u2 & ((1UL << (int)n) - 1));
            }
            else
            {
                Shr128(u1, u2, n, out r1, out r2);
                r2 |= Nonzero((u1 & ((1UL << (int)(n - 64)) - 1)) | u2);
            }
        }
    }

    private static int Lz(ulong u1, ulong u2)
    {
        int l = LeadingZeros64(u1);
        if (l == 64)
        {
            l += LeadingZeros64(u2);
        }
        return l;
    }

    private static void Split(ulong b, out uint sign, out int exp, out ulong mantissa)
    {
        sign = (uint)(b >> 63);
        exp = (int)(b >> 52) & Mask;
        mantissa = b & FracMask;
        if (exp == 0)
        {
            int shift = LeadingZeros64(mantissa) - 11;
            mantissa <<= shift;
            exp = 1 - shift;
        }
        else
        {
            mantissa |= 1UL << 52;
        }
    }

    internal static double FMA(double x, double y, double z)
    {
        ulong bx = Float64bits(x), by = Float64bits(y), bz = Float64bits(z);

        if (x == 0.0 || y == 0.0 || (bx & UvInf) == UvInf || (by & UvInf) == UvInf)
        {
            return x * y + z;
        }
        if (z == 0.0)
        {
            return x * y;
        }
        if ((bz & UvInf) == UvInf)
        {
            return z;
        }

        unchecked
        {
            Split(bx, out uint xs, out int xe, out ulong xm);
            Split(by, out uint ys, out int ye, out ulong ym);
            Split(bz, out uint zs, out int ze, out ulong zm);

            int pe = xe + ye - Bias + 1;
            ulong pm1 = Mul64(xm << 10, ym << 11, out ulong pm2);
            ulong zm1 = zm << 10, zm2 = 0;
            uint ps = xs ^ ys;

            uint is62zero = (uint)((~pm1 >> 62) & 1);
            Shl128(pm1, pm2, is62zero, out pm1, out pm2);
            pe -= (int)is62zero;

            if (pe < ze || pe == ze && pm1 < zm1)
            {
                uint ts = ps; ps = zs; zs = ts;
                int te = pe; pe = ze; ze = te;
                ulong t1 = pm1; pm1 = zm1; zm1 = t1;
                ulong t2 = pm2; pm2 = zm2; zm2 = t2;
            }

            if (ps != zs && pe == ze && pm1 == zm1 && pm2 == zm2)
            {
                return 0;
            }

            ShrCompress(zm1, zm2, (uint)(pe - ze), out zm1, out zm2);

            ulong m;
            if (ps == zs)
            {
                pm2 = Add64(pm2, zm2, 0, out ulong c);
                pm1 = Add64(pm1, zm1, c, out _);
                pe -= (int)(~pm1 >> 63);
                ShrCompress(pm1, pm2, (uint)(64 + (pm1 >> 63)), out pm1, out m);
            }
            else
            {
                pm2 = Sub64(pm2, zm2, 0, out ulong c);
                pm1 = Sub64(pm1, zm1, c, out _);
                int nz = Lz(pm1, pm2);
                pe -= nz;
                Shl128(pm1, pm2, (uint)(nz - 1), out m, out pm2);
                m |= Nonzero(pm2);
            }

            if (pe > 1022 + Bias || pe == 1022 + Bias && ((m + (1UL << 9)) >> 63) == 1)
            {
                return Float64frombits(((ulong)ps << 63) | UvInf);
            }
            if (pe < 0)
            {
                uint n = (uint)(-pe);
                m = ShrG(m, n) | Nonzero(m & (ShlG(1, n) - 1));
                pe = 0;
            }
            m = ((m + (1UL << 9)) >> 10) & ~Zero((m & ((1UL << 10) - 1)) ^ (1UL << 9));
            pe &= -(int)Nonzero(m);
            return Float64frombits(((ulong)ps << 63) + ((ulong)pe << 52) + m);
        }
    }
}

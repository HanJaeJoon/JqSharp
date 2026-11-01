// Ported from itchyny/timefmt-go v0.1.9 (https://github.com/itchyny/timefmt-go).
// The MIT License (MIT), Copyright (c) 2020-2026 itchyny.
// GoTime emulates the subset of Go's time.Time used by gojq, including Go's
// wrapping 64-bit calendar arithmetic (src/time/time.go, Go 1.24).

using System.Globalization;
using System.Text;

namespace JqSharp;

internal readonly struct GoTime
{
    private const long SecondsPerDay = 86400;
    private const ulong AbsoluteYears = 292277022400;
    private const int MarchThruDecember = 306;
    private const long UnixToInternal = (1969 * 365 + 1969 / 4 - 1969 / 100 + 1969 / 400) * SecondsPerDay;
    private const long InternalToUnix = -UnixToInternal;
    private const long AbsoluteToInternal = -((long)(AbsoluteYears / 400) * 146097 + MarchThruDecember) * SecondsPerDay;
    private const long InternalToAbsolute = -AbsoluteToInternal;
    private const long AbsoluteToUnix = AbsoluteToInternal + InternalToUnix;
    private const long UnixToAbsolute = UnixToInternal + InternalToAbsolute;

    // Seconds since January 1, year 1 UTC, so that default(GoTime) is Go's zero Time.
    private readonly long ext;
    private readonly int nsec;
    private readonly Location? loc;

    private GoTime(long ext, int nsec, Location? loc)
    {
        this.ext = ext;
        this.nsec = nsec;
        this.loc = loc == Location.Utc ? null : loc;
    }

    public static GoTime Unix(long sec, long nsec, TimeZoneInfo? loc)
    {
        return UnixIn(sec, nsec, loc == null ? null : Location.FromZone(loc));
    }

    internal static GoTime UnixIn(long sec, long nsec, Location? loc)
    {
        unchecked
        {
            if (nsec < 0 || nsec >= 1000000000)
            {
                long n = nsec / 1000000000;
                sec += n;
                nsec -= n * 1000000000;
                if (nsec < 0)
                {
                    nsec += 1000000000;
                    sec--;
                }
            }
            return new GoTime(sec + UnixToInternal, (int)nsec, loc);
        }
    }

    public static GoTime Date(long year, long month, long day, long hour, long min, long sec, long nsec, TimeZoneInfo? loc)
    {
        return DateIn(year, month, day, hour, min, sec, nsec, loc == null ? null : Location.FromZone(loc));
    }

    internal static GoTime DateIn(long year, long month, long day, long hour, long min, long sec, long nsec, Location? loc)
    {
        unchecked
        {
            long m = month - 1;
            Norm(ref year, ref m, 12);
            month = m + 1;
            Norm(ref sec, ref nsec, 1000000000);
            Norm(ref min, ref sec, 60);
            Norm(ref hour, ref min, 60);
            Norm(ref day, ref hour, 24);
            long unix = (long)DateToAbsDays(year, (uint)month, day) * SecondsPerDay
                + (hour * 3600 + min * 60 + sec) + AbsoluteToUnix;
            if (loc != null)
            {
                // Equivalent to Go's two-step lookup using the zone bounds.
                int offset = loc.Lookup(unix, out _);
                if (offset != 0)
                {
                    offset = loc.Lookup(unix - offset, out _);
                    unix -= offset;
                }
            }
            return new GoTime(unix + UnixToInternal, (int)nsec, loc);
        }
    }

    // Go's time.Local; tests replace it to pin the local zone.
    internal static Location Local { get; set; } = Location.FromZone(TimeZoneInfo.Local);

    public static GoTime Now()
    {
        long ticks = DateTime.UtcNow.Ticks - 621355968000000000L;
        return UnixIn(ticks / 10000000, ticks % 10000000 * 100, Local);
    }

    public long Year
    {
        get
        {
            Split(AbsSec() / SecondsPerDay, out ulong century, out int cyear, out int ayday);
            return CenturyYear(century, cyear, ayday >= MarchThruDecember ? 1 : 0);
        }
    }

    public int Month
    {
        get
        {
            GetDate(out _, out int month, out _);
            return month;
        }
    }

    public int Day
    {
        get
        {
            GetDate(out _, out _, out int day);
            return day;
        }
    }

    public int Hour => (int)(AbsSec() % SecondsPerDay) / 3600;

    public int Minute => (int)(AbsSec() % 3600) / 60;

    public int Second => (int)(AbsSec() % 60);

    public int Nanosecond => nsec;

    public int Weekday => WeekdayOf(AbsSec() / SecondsPerDay);

    public int YearDay
    {
        get
        {
            YearYday(AbsSec() / SecondsPerDay, out _, out int yday);
            return yday;
        }
    }

    public bool IsZero => ext == 0 && nsec == 0;

    public int OffsetSeconds
    {
        get
        {
            Zone(out _, out int offset);
            return offset;
        }
    }

    public string ZoneName
    {
        get
        {
            Zone(out string name, out _);
            return name;
        }
    }

    public long Unix()
    {
        return unchecked(ext + InternalToUnix);
    }

    public GoTime UTC()
    {
        return new GoTime(ext, nsec, null);
    }

    internal GoTime In(Location? location)
    {
        return new GoTime(ext, nsec, location);
    }

    internal void Zone(out string name, out int offset)
    {
        if (loc == null)
        {
            name = "UTC";
            offset = 0;
            return;
        }
        offset = loc.Lookup(Unix(), out name);
    }

    internal void GetDate(out long year, out int month, out int day)
    {
        Split(AbsSec() / SecondsPerDay, out ulong century, out int cyear, out int ayday);
        uint d = unchecked(2141 * (uint)ayday + 197913);
        int janFeb = ayday >= MarchThruDecember ? 1 : 0;
        year = CenturyYear(century, cyear, janFeb);
        month = (int)(d >> 16) - janFeb * 12;
        day = 1 + (int)((d & 0xFFFF) / 2141);
    }

    internal void ISOWeek(out long year, out int week)
    {
        unchecked
        {
            ulong days = AbsSec() / SecondsPerDay;
            ulong thu = days + (ulong)(long)(4 - (WeekdayOf(days - 1) + 1));
            YearYday(thu, out year, out int yday);
            week = (yday - 1) / 7 + 1;
        }
    }

    private ulong AbsSec()
    {
        unchecked
        {
            long sec = ext + InternalToUnix;
            if (loc != null)
            {
                sec += loc.Lookup(sec, out _);
            }
            return (ulong)(sec + UnixToAbsolute);
        }
    }

    private static void Norm(ref long hi, ref long lo, long b)
    {
        unchecked
        {
            if (lo < 0)
            {
                long n = (-lo - 1) / b + 1;
                hi -= n;
                lo += n * b;
            }
            if (lo >= b)
            {
                long n = lo / b;
                hi += n;
                lo -= n * b;
            }
        }
    }

    private static ulong DateToAbsDays(long year, uint month, long day)
    {
        unchecked
        {
            uint amonth = month;
            uint janFeb = amonth < 3 ? 1u : 0u;
            amonth += 12 * janFeb;
            ulong y = (ulong)year - janFeb + AbsoluteYears;
            uint ayday = (979 * amonth - 2919) >> 5;
            ulong century = y / 100;
            uint cyear = (uint)(y % 100);
            uint cday = 1461 * cyear / 4;
            ulong centurydays = 146097 * century / 4;
            return centurydays + (ulong)((long)(cday + ayday) + day - 1);
        }
    }

    private static void Split(ulong days, out ulong century, out int cyear, out int ayday)
    {
        unchecked
        {
            ulong d = 4 * days + 3;
            century = d / 146097;
            uint cd = (uint)(d % 146097) | 3;
            ulong product = 2939745UL * cd;
            cyear = (int)(product >> 32);
            ayday = (int)((uint)product / 2939745 / 4);
        }
    }

    private static long CenturyYear(ulong century, int cyear, int janFeb)
    {
        return unchecked((long)(century * 100 - AbsoluteYears) + cyear + janFeb);
    }

    private static void YearYday(ulong days, out long year, out int yday)
    {
        Split(days, out ulong century, out int cyear, out int ayday);
        int janFeb = ayday >= MarchThruDecember ? 1 : 0;
        year = CenturyYear(century, cyear, janFeb);
        int leap = cyear % 4 == 0 && (cyear != 0 || century % 4 == 0) ? 1 : 0;
        yday = ayday + (1 + 31 + 28) + (leap & ~janFeb) - 365 * janFeb;
    }

    private static int WeekdayOf(ulong days)
    {
        return (int)(unchecked(days + 3) % 7);
    }

    // Go's *time.Location: UTC, a fixed zone, or a TimeZoneInfo-backed zone.
    internal sealed class Location
    {
        internal static readonly Location Utc = new Location(null, "UTC", 0);

        private const long MinDateTimeUnix = -62135596800;
        private const long MaxDateTimeUnix = 253402300799;

        private readonly TimeZoneInfo? tz;
        private readonly string name;
        private readonly int offset;
        private readonly bool utcLike;

        private Location(TimeZoneInfo? tz, string name, int offset)
        {
            this.tz = tz;
            this.name = name;
            this.offset = offset;
            utcLike = tz != null && tz.BaseUtcOffset == TimeSpan.Zero && tz.GetAdjustmentRules().Length == 0;
        }

        internal static Location Fixed(string name, int offset)
        {
            return new Location(null, name, offset);
        }

        internal static Location FromZone(TimeZoneInfo tz)
        {
            return new Location(tz, "", 0);
        }

        internal int Lookup(long unixSec, out string zoneName)
        {
            if (tz == null)
            {
                zoneName = name;
                return offset;
            }
            long sec = unixSec < MinDateTimeUnix ? MinDateTimeUnix : unixSec > MaxDateTimeUnix ? MaxDateTimeUnix : unixSec;
            DateTime utc = new DateTime((sec - MinDateTimeUnix) * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
            int result = (int)(tz.GetUtcOffset(utc).Ticks / TimeSpan.TicksPerSecond);
            zoneName = Abbreviation(result);
            return result;
        }

        // Emulates Location.lookupName: the zone in effect at the instant first, then any zone of the location.
        internal bool LookupName(string zoneName, long unixSec, out int result)
        {
            result = Lookup(unixSec, out string current);
            if (current == zoneName)
            {
                return true;
            }
            if (tz == null)
            {
                return false;
            }
            int baseOffset = (int)(tz.BaseUtcOffset.Ticks / TimeSpan.TicksPerSecond);
            if (Abbreviation(baseOffset) == zoneName)
            {
                result = baseOffset;
                return true;
            }
            foreach (TimeZoneInfo.AdjustmentRule rule in tz.GetAdjustmentRules())
            {
                int candidate = baseOffset + (int)(rule.DaylightDelta.Ticks / TimeSpan.TicksPerSecond);
                if (Abbreviation(candidate) == zoneName)
                {
                    result = candidate;
                    return true;
                }
            }
            return false;
        }

        // shortcut: TimeZoneInfo exposes no zone abbreviations, so use tzdata's numeric style ("+09", "-0330")
        // like Go does for unnamed zones; upgrade with a Windows-name/IANA abbreviation table if needed.
        private string Abbreviation(int seconds)
        {
            if (seconds == 0 && utcLike)
            {
                return "UTC";
            }
            StringBuilder sb = new StringBuilder(6);
            sb.Append(seconds < 0 ? '-' : '+');
            int abs = Math.Abs(seconds);
            sb.Append((abs / 3600).ToString("00", CultureInfo.InvariantCulture));
            int rest = abs % 3600;
            if (rest != 0)
            {
                sb.Append((rest / 60).ToString("00", CultureInfo.InvariantCulture));
                if (rest % 60 != 0)
                {
                    sb.Append((rest % 60).ToString("00", CultureInfo.InvariantCulture));
                }
            }
            return sb.ToString();
        }
    }
}

internal static class TimeFmt
{
    private const int PaddingMask = 0x7F;
    private const int NoPadding = 0x80;

    private static readonly string[] LongMonthNames =
    {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    };

    private static readonly string[] ShortMonthNames =
    {
        "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
    };

    private static readonly string[] LongWeekNames =
    {
        "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday",
    };

    private static readonly string[] ShortWeekNames =
    {
        "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat",
    };

    private static readonly string[] AmPm = { "AM", "PM" };

    public static string Format(GoTime t, string format)
    {
        StringBuilder buf = new StringBuilder(64);
        t.GetDate(out long year, out int month, out int day);
        int hour = t.Hour;
        int minute = t.Minute;
        int second = t.Second;
        int width = 0;
        int colons = 0;
        int padding = '0';
        string pending = "";
        bool upper = false;
        bool swap = false;
        for (int i = 0; i < format.Length; i++)
        {
            char b = format[i];
            if (b != '%')
            {
                buf.Append(b);
                continue;
            }
            if (++i == format.Length)
            {
                buf.Append('%');
                break;
            }
            b = format[i];
            width = 0;
            padding = '0';
            upper = false;
            swap = false;
        L:
            switch (b)
            {
                case '-':
                    if (pending.Length != 0)
                    {
                        buf.Append('-');
                        break;
                    }
                    if (++i == format.Length)
                    {
                        goto K;
                    }
                    padding = NoPadding;
                    b = format[i];
                    goto L;
                case '_':
                    if (++i == format.Length)
                    {
                        goto K;
                    }
                    padding = ' ' | NoPadding;
                    b = format[i];
                    goto L;
                case '^':
                    if (++i == format.Length)
                    {
                        goto K;
                    }
                    upper = true;
                    b = format[i];
                    goto L;
                case '#':
                    if (++i == format.Length)
                    {
                        goto K;
                    }
                    swap = true;
                    b = format[i];
                    goto L;
                case '0':
                    if (++i == format.Length)
                    {
                        goto K;
                    }
                    padding = '0' | NoPadding;
                    b = format[i];
                    goto L;
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    width = b & 0x0F;
                    for (i++; i < format.Length; i++)
                    {
                        b = format[i];
                        if (b <= '9' && '0' <= b)
                        {
                            width = Math.Min(width * 10 + (b & 0x0F), 1024);
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (padding == NoPadding)
                    {
                        padding = ' ' | NoPadding;
                    }
                    if (i == format.Length)
                    {
                        goto K;
                    }
                    goto L;
                case 'Y':
                    AppendInt(buf, year, Or(width, 4), padding);
                    break;
                case 'y':
                    AppendInt(buf, Math.Abs(year % 100), Math.Max(width, 2), padding);
                    break;
                case 'C':
                    {
                        long c = year / 100;
                        bool z = year < 0 && c == 0;
                        if (z)
                        {
                            c = -1;
                        }
                        AppendInt(buf, c, Math.Max(width, 2), padding);
                        if (z)
                        {
                            buf[buf.Length - 1] = '0';
                        }
                        break;
                    }
                case 'g':
                    {
                        t.ISOWeek(out long isoYear, out _);
                        AppendInt(buf, Math.Abs(isoYear % 100), Math.Max(width, 2), padding);
                        break;
                    }
                case 'G':
                    {
                        t.ISOWeek(out long isoYear, out _);
                        AppendInt(buf, isoYear, Or(width, 4), padding);
                        break;
                    }
                case 'm':
                    AppendInt(buf, month, Math.Max(width, 2), padding);
                    break;
                case 'B':
                    AppendString(buf, LongMonthNames[month - 1], width, padding, upper, swap);
                    break;
                case 'b':
                case 'h':
                    AppendString(buf, ShortMonthNames[month - 1], width, padding, upper, swap);
                    break;
                case 'A':
                    AppendString(buf, LongWeekNames[t.Weekday], width, padding, upper, swap);
                    break;
                case 'a':
                    AppendString(buf, ShortWeekNames[t.Weekday], width, padding, upper, swap);
                    break;
                case 'w':
                    AppendInt(buf, t.Weekday, width, padding);
                    break;
                case 'u':
                    AppendInt(buf, Or(t.Weekday, 7), width, padding);
                    break;
                case 'V':
                    {
                        t.ISOWeek(out _, out int week);
                        AppendInt(buf, week, Math.Max(width, 2), padding);
                        break;
                    }
                case 'U':
                    AppendInt(buf, (t.YearDay + 6 - t.Weekday) / 7, Math.Max(width, 2), padding);
                    break;
                case 'W':
                    {
                        int week = t.YearDay;
                        int weekday = t.Weekday;
                        if (weekday > 0)
                        {
                            week -= weekday - 7;
                        }
                        week /= 7;
                        AppendInt(buf, week, Math.Max(width, 2), padding);
                        break;
                    }
                case 'e':
                    if (padding < NoPadding)
                    {
                        padding = ' ';
                    }
                    goto case 'd';
                case 'd':
                    AppendInt(buf, day, Math.Max(width, 2), padding);
                    break;
                case 'j':
                    AppendInt(buf, t.YearDay, Math.Max(width, 3), padding);
                    break;
                case 'k':
                    if (padding < NoPadding)
                    {
                        padding = ' ';
                    }
                    goto case 'H';
                case 'H':
                    AppendInt(buf, hour, Math.Max(width, 2), padding);
                    break;
                case 'l':
                    if (padding < NoPadding)
                    {
                        padding = ' ';
                    }
                    goto case 'I';
                case 'I':
                    AppendInt(buf, Or(hour % 12, 12), Math.Max(width, 2), padding);
                    break;
                case 'P':
                    swap = !upper && !swap;
                    goto case 'p';
                case 'p':
                    AppendString(buf, hour < 12 ? "AM" : "PM", width, padding, upper, swap);
                    break;
                case 'M':
                    AppendInt(buf, minute, Math.Max(width, 2), padding);
                    break;
                case 'S':
                    AppendInt(buf, second, Math.Max(width, 2), padding);
                    break;
                case 's':
                    if (padding < NoPadding)
                    {
                        padding = ' ';
                    }
                    AppendInt64(buf, t.Unix(), width, padding);
                    break;
                case 'f':
                    AppendInt(buf, t.Nanosecond / 1000, Or(width, 6), padding);
                    break;
                case 'Z':
                case 'z':
                    {
                        t.Zone(out string name, out int offset);
                        if (b == 'Z' && name.Length != 0)
                        {
                            AppendString(buf, name, width, padding, upper, swap);
                            break;
                        }
                        int start = buf.Length;
                        if (padding != NoPadding)
                        {
                            for (; width > 1; width--)
                            {
                                buf.Append((char)(padding & PaddingMask));
                            }
                        }
                        int signAt = buf.Length;
                        if (offset < 0)
                        {
                            buf.Append('-');
                            offset = -offset;
                        }
                        else
                        {
                            buf.Append('+');
                        }
                        int hourAt = buf.Length;
                        AppendInt(buf, offset / 3600, 2, padding);
                        if (buf[hourAt] == ' ')
                        {
                            buf[hourAt] = buf[hourAt - 1];
                            buf[hourAt - 1] = ' ';
                        }
                        offset %= 3600;
                        if (colons <= 2 || offset != 0)
                        {
                            if (colons != 0)
                            {
                                buf.Append(':');
                            }
                            AppendInt(buf, offset / 60, 2, '0');
                            offset %= 60;
                            if (colons == 2 || colons == 3 && offset != 0)
                            {
                                buf.Append(':');
                                AppendInt(buf, offset, 2, '0');
                            }
                        }
                        colons = 0;
                        int k = Math.Min(buf.Length - signAt - 1, signAt - start);
                        if (k > 0)
                        {
                            buf.Remove(signAt - k, k);
                            if ((padding & PaddingMask) == '0')
                            {
                                char c = buf[start];
                                buf[start] = buf[signAt - k];
                                buf[signAt - k] = c;
                            }
                        }
                        break;
                    }
                case ':':
                    if (pending.Length != 0)
                    {
                        buf.Append(':');
                    }
                    else
                    {
                        colons = 1;
                        for (i++; i < format.Length; i++)
                        {
                            char c = format[i];
                            if (c == ':')
                            {
                                colons++;
                            }
                            else if (c == 'z')
                            {
                                if (colons > 3)
                                {
                                    i++;
                                    break;
                                }
                                b = 'z';
                                goto L;
                            }
                            else
                            {
                                break;
                            }
                        }
                        AppendLast(buf, format, i, width, padding);
                        i--;
                        colons = 0;
                    }
                    break;
                case 't':
                    AppendString(buf, "\t", width, padding, false, false);
                    break;
                case 'n':
                    AppendString(buf, "\n", width, padding, false, false);
                    break;
                case '%':
                    AppendString(buf, "%", width, padding, false, false);
                    break;
                case 'c':
                    pending = "a b e H:M:S Y";
                    swap = false;
                    break;
                case '+':
                    pending = "a b e H:M:S Z Y";
                    swap = false;
                    break;
                case 'v':
                    pending = "e-b-Y";
                    swap = false;
                    break;
                case 'r':
                    pending = "I:M:S p";
                    swap = false;
                    break;
                case 'F':
                    pending = "Y-m-d";
                    break;
                case 'D':
                case 'x':
                    pending = "m/d/y";
                    break;
                case 'T':
                case 'X':
                    pending = "H:M:S";
                    break;
                case 'R':
                    pending = "H:M";
                    break;
                default:
                    if (pending.Length == 0)
                    {
                        AppendLast(buf, format, i, width - 1, padding);
                    }
                    buf.Append(b);
                    break;
            }
            if (pending.Length != 0)
            {
                b = pending[0];
                pending = pending.Substring(1);
                width = 0;
                padding = '0';
                goto L;
            }
        }
        return buf.ToString();
    K:
        AppendLast(buf, format, format.Length, width, padding);
        return buf.ToString();
    }

    private static void AppendInt(StringBuilder buf, long num, int width, int padding)
    {
        if (num < 0 || num >= 10000)
        {
            AppendInt64(buf, num, width, padding);
            return;
        }
        int digits = num < 10 ? 1 : num < 100 ? 2 : num < 1000 ? 3 : 4;
        if (padding != NoPadding)
        {
            char p = (char)(padding & PaddingMask);
            for (; width > digits; width--)
            {
                buf.Append(p);
            }
        }
        buf.Append(num.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendInt64(StringBuilder buf, long num, int width, int padding)
    {
        string digits = num.ToString(CultureInfo.InvariantCulture);
        if (padding == NoPadding)
        {
            buf.Append(digits);
            return;
        }
        char p = (char)(padding & PaddingMask);
        int i = buf.Length;
        for (; width > 1; width--)
        {
            buf.Append(p);
        }
        int j = buf.Length;
        buf.Append(digits);
        int k = Math.Min(buf.Length - j - 1, j - i);
        if (k > 0)
        {
            buf.Remove(j - k, k);
            j -= k;
            if (buf[i] == '0' && buf[j] == '-')
            {
                buf[i] = '-';
                buf[j] = '0';
            }
        }
    }

    private static void AppendString(StringBuilder buf, string str, int width, int padding, bool upper, bool swap)
    {
        if (width > str.Length && padding != NoPadding)
        {
            char p = padding < NoPadding ? ' ' : (char)(padding & PaddingMask);
            for (width -= str.Length; width > 0; width--)
            {
                buf.Append(p);
            }
        }
        if (swap && str[Math.Min(1, str.Length - 1)] < 'a')
        {
            foreach (char c in str)
            {
                buf.Append('A' <= c && c <= 'Z' ? (char)(c + 0x20) : c);
            }
        }
        else if (swap || upper)
        {
            foreach (char c in str)
            {
                buf.Append('a' <= c && c <= 'z' ? (char)(c - 0x20) : c);
            }
        }
        else
        {
            buf.Append(str);
        }
    }

    // Appends format[last '%' before end, end) as a string.
    private static void AppendLast(StringBuilder buf, string format, int end, int width, int padding)
    {
        int i = end > 0 ? format.LastIndexOf('%', end - 1, end) : -1;
        if (i >= 0)
        {
            AppendString(buf, format.Substring(i, end - i), width, padding, false, false);
        }
    }

    private static int Or(int x, int y)
    {
        return x != 0 ? x : y;
    }

    public static GoTime Parse(string source, string format)
    {
        return Parse(source, format, GoTime.Local);
    }

    private static GoTime Parse(string source, string format, GoTime.Location baseLoc)
    {
        string? err = ParseTime(source, format, GoTime.Location.Utc, baseLoc, out GoTime t);
        if (err != null)
        {
            throw new FormatException("failed to parse " + Quote(source, '"') + " with " + Quote(format, '"') + ": " + err);
        }
        return t;
    }

    private static string? ParseTime(string source, string format, GoTime.Location loc, GoTime.Location baseLoc, out GoTime result)
    {
        result = default;
        long year = 1900;
        int month = 1, day = 0, hour = 0, minute = 0, second = 0, nanosecond = 0;
        int j = 0, week = 0, weekday = 0, yday = 0, ampm = 0, colons = 0, sign;
        int century = -1, weekstart = -1;
        bool hasISOYear = false, hasUnix = false, hasZoneName = false, hasZoneOffset = false;
        string pending = "";
        int l = source.Length;
        for (int i = 0; i < format.Length; i++)
        {
            char b = format[i];
            if (b != '%')
            {
                if (j >= l || source[j] != b)
                {
                    return "expected " + Quote(RuneAt(format, i), '\'');
                }
                j++;
                continue;
            }
            if (++i == format.Length)
            {
                return "stray \"%\"";
            }
            b = format[i];
        L:
            switch (b)
            {
                case 'G':
                    hasISOYear = true;
                    goto case 'Y';
                case 'Y':
                    {
                        j = ParseSign(source, j, out sign);
                        if (!ParseInt(source, j, 4, 0, 9999, out int value, out j))
                        {
                            return CannotParse(b);
                        }
                        year = (long)value * sign;
                        break;
                    }
                case 'g':
                    hasISOYear = true;
                    goto case 'y';
                case 'y':
                    {
                        if (!ParseInt(source, j, 2, 0, 99, out int value, out j))
                        {
                            return CannotParse(b);
                        }
                        year = value < 69 ? value + 2000 : value + 1900;
                        break;
                    }
                case 'C':
                    j = ParseSign(source, j, out sign);
                    if (sign < 0)
                    {
                        return "negative century is not supported for \"%C\"";
                    }
                    if (!ParseInt(source, j, 2, 0, 99, out century, out j))
                    {
                        return CannotParse('C');
                    }
                    break;
                case 'm':
                    if (!ParseInt(source, j, 2, 1, 12, out month, out j))
                    {
                        return CannotParse('m');
                    }
                    break;
                case 'B':
                    if (!ParseAny(source, j, LongMonthNames, out month, out j))
                    {
                        return CannotParse('B');
                    }
                    break;
                case 'b':
                case 'h':
                    if (!ParseAny(source, j, ShortMonthNames, out month, out j))
                    {
                        return CannotParse(b);
                    }
                    break;
                case 'A':
                    if (!ParseAny(source, j, LongWeekNames, out weekday, out j))
                    {
                        return CannotParse('A');
                    }
                    break;
                case 'a':
                    if (!ParseAny(source, j, ShortWeekNames, out weekday, out j))
                    {
                        return CannotParse('a');
                    }
                    break;
                case 'w':
                    if (!ParseInt(source, j, 1, 0, 6, out weekday, out j))
                    {
                        return CannotParse('w');
                    }
                    weekday++;
                    break;
                case 'u':
                    if (!ParseInt(source, j, 1, 1, 7, out weekday, out j))
                    {
                        return CannotParse('u');
                    }
                    weekday = weekday % 7 + 1;
                    break;
                case 'V':
                    if (!ParseInt(source, j, 2, 1, 53, out week, out j))
                    {
                        return CannotParse(b);
                    }
                    weekstart = 4;
                    weekday = Or(weekday, 2);
                    break;
                case 'U':
                    if (!ParseInt(source, j, 2, 0, 53, out week, out j))
                    {
                        return CannotParse(b);
                    }
                    weekstart = 0;
                    weekday = Or(weekday, 1);
                    break;
                case 'W':
                    if (!ParseInt(source, j, 2, 0, 53, out week, out j))
                    {
                        return CannotParse(b);
                    }
                    weekstart = 1;
                    weekday = Or(weekday, 2);
                    break;
                case 'e':
                    if (j < l && source[j] == ' ')
                    {
                        j++;
                    }
                    goto case 'd';
                case 'd':
                    if (!ParseInt(source, j, 2, 1, 31, out day, out j))
                    {
                        return CannotParse(b);
                    }
                    break;
                case 'j':
                    if (!ParseInt(source, j, 3, 1, 366, out yday, out j))
                    {
                        return CannotParse('j');
                    }
                    break;
                case 'k':
                    if (j < l && source[j] == ' ')
                    {
                        j++;
                    }
                    goto case 'H';
                case 'H':
                    if (!ParseInt(source, j, 2, 0, 23, out hour, out j))
                    {
                        return CannotParse(b);
                    }
                    break;
                case 'l':
                    if (j < l && source[j] == ' ')
                    {
                        j++;
                    }
                    goto case 'I';
                case 'I':
                    if (!ParseInt(source, j, 2, 1, 12, out hour, out j))
                    {
                        return CannotParse(b);
                    }
                    if (hour == 12)
                    {
                        hour = 0;
                    }
                    break;
                case 'P':
                case 'p':
                    if (!ParseAny(source, j, AmPm, out ampm, out j))
                    {
                        return CannotParse(b);
                    }
                    break;
                case 'M':
                    if (!ParseInt(source, j, 2, 0, 59, out minute, out j))
                    {
                        return CannotParse('M');
                    }
                    break;
                case 'S':
                    if (!ParseInt(source, j, 2, 0, 60, out second, out j))
                    {
                        return CannotParse('S');
                    }
                    break;
                case 's':
                    {
                        j = ParseSign(source, j, out sign);
                        // The last second of year 2147483647, or the negated first second of year -2147483648.
                        long maxUnix = sign < 0 ? 67768100567971200 : 67767976233532799;
                        if (!ParseInt64(source, j, maxUnix, out long unix, out j))
                        {
                            return CannotParse('s');
                        }
                        GoTime t = GoTime.UnixIn(sign * unix, 0, null);
                        t.GetDate(out year, out month, out day);
                        hour = t.Hour;
                        minute = t.Minute;
                        second = t.Second;
                        hasUnix = true;
                        break;
                    }
                case 'f':
                    {
                        int start = j;
                        if (!ParseInt(source, j, 6, 0, 999999, out int microsecond, out j))
                        {
                            return CannotParse('f');
                        }
                        for (int n = j - start; n < 6; n++)
                        {
                            microsecond *= 10;
                        }
                        nanosecond = microsecond * 1000;
                        break;
                    }
                case 'Z':
                    {
                        if (j >= l)
                        {
                            return CannotParse('Z');
                        }
                        int start = j;
                        if (source[j] == '+' || source[j] == '-')
                        {
                            for (j++; j < l; j++)
                            {
                                if ((uint)(source[j] - '0') >= 10)
                                {
                                    break;
                                }
                            }
                        }
                        else if (j + 4 <= l && (string.CompareOrdinal(source, j, "ChST", 0, 4) == 0
                            || string.CompareOrdinal(source, j, "MeST", 0, 4) == 0))
                        {
                            j += 4;
                        }
                        else
                        {
                            for (; j < l; j++)
                            {
                                if ((uint)(source[j] - 'A') > 'Z' - 'A')
                                {
                                    break;
                                }
                            }
                        }
                        string name = source.Substring(start, j - start);
                        GoTime.Location? zone = ParseZoneName(name, baseLoc);
                        if (zone == null)
                        {
                            return CannotParse('Z');
                        }
                        if (hasZoneOffset)
                        {
                            LocationZone(loc, out _, out int offset);
                            loc = GoTime.Location.Fixed(name, offset);
                        }
                        else
                        {
                            loc = zone;
                        }
                        hasZoneName = true;
                        break;
                    }
                case 'z':
                    if (j >= l)
                    {
                        return CannotParseZ(colons);
                    }
                    sign = 1;
                    switch (source[j])
                    {
                        case '-':
                        case '+':
                            {
                                if (source[j] == '-')
                                {
                                    sign = -1;
                                }
                                int zoneMinute, zoneSecond = 0;
                                int start = j + 1;
                                ParseInt(source, start, 2, 0, 23, out int zoneHour, out j);
                                if (j != start + 2)
                                {
                                    return CannotParseZ(colons);
                                }
                                if (j >= l || source[j] != ':')
                                {
                                    if (colons > 0 && colons < 3)
                                    {
                                        return ExpectedColonForZ(colons);
                                    }
                                }
                                else
                                {
                                    j++;
                                    if (colons == 0)
                                    {
                                        colons = 4;
                                    }
                                }
                                start = j;
                                ParseInt(source, start, 2, 0, 59, out zoneMinute, out j);
                                if (j != start + 2)
                                {
                                    if (colons > 0 && colons != 3)
                                    {
                                        return CannotParseZ(colons & 3);
                                    }
                                    j = start;
                                }
                                else if (colons > 1)
                                {
                                    if (j >= l || source[j] != ':')
                                    {
                                        if (colons < 3)
                                        {
                                            return ExpectedColonForZ(colons);
                                        }
                                    }
                                    else
                                    {
                                        start = j + 1;
                                        ParseInt(source, start, 2, 0, 59, out zoneSecond, out j);
                                        if (j != start + 2)
                                        {
                                            if (colons < 3)
                                            {
                                                return CannotParseZ(colons);
                                            }
                                            j = start - 1;
                                        }
                                    }
                                }
                                string name = "";
                                if (hasZoneName)
                                {
                                    LocationZone(loc, out name, out _);
                                }
                                loc = GoTime.Location.Fixed(name, sign * ((zoneHour * 60 + zoneMinute) * 60 + zoneSecond));
                                colons = 0;
                                hasZoneOffset = true;
                                break;
                            }
                        case 'Z':
                            loc = GoTime.Location.Utc;
                            colons = 0;
                            j++;
                            break;
                        default:
                            return CannotParseZ(colons);
                    }
                    break;
                case ':':
                    if (pending.Length != 0)
                    {
                        if (j >= l || source[j] != b)
                        {
                            return "expected " + Quote(b.ToString(), '\'');
                        }
                        j++;
                    }
                    else
                    {
                        for (colons = 1; colons <= 3; colons++)
                        {
                            if (++i == format.Length)
                            {
                                break;
                            }
                            else if ((b = format[i]) == 'z')
                            {
                                goto L;
                            }
                            else if (b != ':' || colons == 3)
                            {
                                break;
                            }
                        }
                        return "expected 'z' after \"%" + new string(':', colons) + "\"";
                    }
                    break;
                case 't':
                case 'n':
                    {
                        int start = j;
                        for (; j < l; j++)
                        {
                            char c = source[j];
                            if (c != ' ' && c != '\t' && c != '\n' && c != '\v' && c != '\f' && c != '\r')
                            {
                                break;
                            }
                        }
                        if (start == j)
                        {
                            return "expected a space for \"%" + b + "\"";
                        }
                        break;
                    }
                case '%':
                    if (j >= l || source[j] != b)
                    {
                        return "expected " + Quote(b.ToString(), '\'');
                    }
                    j++;
                    break;
                case 'c':
                    pending = "a b e H:M:S Y";
                    break;
                case '+':
                    pending = "a b e H:M:S Z Y";
                    break;
                case 'v':
                    pending = "e-b-Y";
                    break;
                case 'r':
                    pending = "I:M:S p";
                    break;
                case 'F':
                    pending = "Y-m-d";
                    break;
                case 'D':
                case 'x':
                    pending = "m/d/y";
                    break;
                case 'T':
                case 'X':
                    pending = "H:M:S";
                    break;
                case 'R':
                    pending = "H:M";
                    break;
                default:
                    if (pending.Length == 0)
                    {
                        return "unexpected format \"%" + RuneAt(format, i) + "\"";
                    }
                    if (j >= l || source[j] != b)
                    {
                        return "expected " + Quote(b.ToString(), '\'');
                    }
                    j++;
                    break;
            }
            if (pending.Length != 0)
            {
                b = pending[0];
                pending = pending.Substring(1);
                goto L;
            }
        }
        if (j < l)
        {
            return "unparsed string " + Quote(source.Substring(j), '"');
        }
        if (ampm > 0)
        {
            hour = hour % 12 + (ampm - 1) * 12;
        }
        if (century >= 0)
        {
            year = century * 100 + year % 100;
        }
        if (day <= 0 && yday <= 0 && !hasISOYear && weekstart < 0)
        {
            day = 1;
        }
        if (day > 0)
        {
            if (hasISOYear)
            {
                return "use \"%Y\" to parse non-ISO year";
            }
            result = hasUnix
                ? GoTime.DateIn(year, month, day, hour, minute, second, nanosecond, null).In(loc)
                : GoTime.DateIn(year, month, day, hour, minute, second, nanosecond, loc);
            return null;
        }
        if (yday > 0)
        {
            if (hasISOYear)
            {
                return "use \"%Y\" to parse non-ISO year for \"%j\"";
            }
            result = GoTime.DateIn(year, 1, yday, hour, minute, second, nanosecond, loc);
            return null;
        }
        if (hasISOYear && weekstart < 0)
        {
            weekstart = 4;
            week = 1;
            weekday = Or(weekday, 2);
        }
        if ((weekstart == 4) != hasISOYear)
        {
            return hasISOYear
                ? "use \"%Y\" to parse non-ISO year for \"%U\" or \"%W\""
                : "use \"%G\" to parse ISO year for \"%V\"";
        }
        if (weekstart > 0 && weekday == 1)
        {
            week++;
        }
        GoTime t0 = GoTime.DateIn(year, 1, -weekstart, 0, 0, 0, 0, null);
        result = GoTime.DateIn(year, 1, (long)week * 7 - weekstart - t0.Weekday + weekday - 1,
            hour, minute, second, nanosecond, loc);
        return null;
    }

    private static void LocationZone(GoTime.Location loc, out string name, out int offset)
    {
        GoTime.DateIn(2000, 1, 1, 0, 0, 0, 0, loc).Zone(out name, out offset);
    }

    // Emulates time.ParseInLocation("MST", name, baseLoc).Location(); null on failure.
    private static GoTime.Location? ParseZoneName(string name, GoTime.Location baseLoc)
    {
        if (name.StartsWith("UTC", StringComparison.Ordinal))
        {
            return name.Length == 3 ? GoTime.Location.Utc : null;
        }
        int length = ParseTimeZone(name);
        if (length == 0 || length != name.Length)
        {
            return null;
        }
        long unix = GoTime.DateIn(0, 1, 1, 0, 0, 0, 0, null).Unix();
        if (baseLoc.LookupName(name, unix, out _))
        {
            return baseLoc;
        }
        int offset = 0;
        if (name.Length > 3 && name.StartsWith("GMT", StringComparison.Ordinal))
        {
            offset = int.Parse(name.Substring(3), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) * 3600;
        }
        return GoTime.Location.Fixed(name, offset);
    }

    // Go's time.parseTimeZone: the length of the zone name at the start of value, or 0.
    private static int ParseTimeZone(string value)
    {
        if (value.Length < 3)
        {
            return 0;
        }
        if (value.Length >= 4 && (value.StartsWith("ChST", StringComparison.Ordinal) || value.StartsWith("MeST", StringComparison.Ordinal)))
        {
            return 4;
        }
        if (value.StartsWith("GMT", StringComparison.Ordinal))
        {
            return 3 + ParseSignedOffset(value.Substring(3));
        }
        if (value[0] == '+' || value[0] == '-')
        {
            return ParseSignedOffset(value);
        }
        int upper = 0;
        while (upper < 6 && upper < value.Length && value[upper] >= 'A' && value[upper] <= 'Z')
        {
            upper++;
        }
        switch (upper)
        {
            case 3:
                return 3;
            case 4:
                return value[3] == 'T' || value.StartsWith("WITA", StringComparison.Ordinal) ? 4 : 0;
            case 5:
                return value[4] == 'T' ? 5 : 0;
            default:
                return 0;
        }
    }

    private static int ParseSignedOffset(string value)
    {
        if (value.Length == 0 || value[0] != '-' && value[0] != '+')
        {
            return 0;
        }
        int i = 1;
        long x = 0;
        for (; i < value.Length && (uint)(value[i] - '0') < 10; i++)
        {
            x = x * 10 + (value[i] - '0');
            if (x > int.MaxValue)
            {
                return 0;
            }
        }
        return i == 1 || x > 23 ? 0 : i;
    }

    private static int ParseSign(string source, int index, out int sign)
    {
        if (index < source.Length && source[index] == '-')
        {
            sign = -1;
            return index + 1;
        }
        sign = 1;
        return index;
    }

    // On failure value and next are both 0, as in timefmt-go (the %z parser relies on it).
    private static bool ParseInt(string source, int index, int size, int minimum, int maximum, out int value, out int next)
    {
        value = 0;
        int i = index;
        for (int l = Math.Min(index + size, source.Length); i < l; i++)
        {
            uint d = (uint)(source[i] - '0');
            if (d >= 10)
            {
                break;
            }
            value = value * 10 + (int)d;
        }
        if (i == index || value < minimum || maximum < value)
        {
            value = 0;
            next = 0;
            return false;
        }
        next = i;
        return true;
    }

    private static bool ParseInt64(string source, int index, long maximum, out long value, out int next)
    {
        value = 0;
        next = 0;
        int i = index;
        for (; i < source.Length; i++)
        {
            uint d = (uint)(source[i] - '0');
            if (d >= 10)
            {
                break;
            }
            value = value * 10 + d;
            if (value > maximum)
            {
                value = 0;
                return false;
            }
        }
        if (i == index)
        {
            return false;
        }
        next = i;
        return true;
    }

    private static bool ParseAny(string source, int index, string[] candidates, out int value, out int next)
    {
        for (int i = 0; i < candidates.Length; i++)
        {
            string xs = candidates[i];
            int j = index;
            bool matched = true;
            for (int k = 0; k < xs.Length; k++, j++)
            {
                if (j >= source.Length)
                {
                    matched = false;
                    break;
                }
                char x = xs[k];
                char y = source[j];
                if (x != y && (x | 0x20) != (y | 0x20))
                {
                    matched = false;
                    break;
                }
            }
            if (matched)
            {
                value = i + 1;
                next = j;
                return true;
            }
        }
        value = 0;
        next = 0;
        return false;
    }

    private static string CannotParse(char format)
    {
        return "cannot parse \"%" + format + "\"";
    }

    private static string CannotParseZ(int colons)
    {
        return "cannot parse \"%" + new string(':', colons) + "z\"";
    }

    private static string ExpectedColonForZ(int colons)
    {
        return "expected ':' for \"%" + new string(':', colons) + "z\"";
    }

    // The rune (one or two UTF-16 code units) at index i.
    private static string RuneAt(string s, int i)
    {
        return char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])
            ? s.Substring(i, 2)
            : s.Substring(i, 1);
    }

    // Go's strconv.Quote (quote '"') and strconv.QuoteRune (quote '\'').
    private static string Quote(string s, char quote)
    {
        StringBuilder sb = new StringBuilder(s.Length + 2);
        sb.Append(quote);
        for (int i = 0; i < s.Length; i++)
        {
            int r = s[i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                r = char.ConvertToUtf32(s[i], s[i + 1]);
            }
            else if (char.IsSurrogate(s[i]))
            {
                r = 0xFFFD;
            }
            if (r == quote || r == '\\')
            {
                sb.Append('\\').Append((char)r);
            }
            else if (IsPrint(s, i, r))
            {
                if (r > 0xFFFF)
                {
                    sb.Append(s, i, 2);
                }
                else
                {
                    sb.Append((char)r);
                }
            }
            else
            {
                switch (r)
                {
                    case '\a':
                        sb.Append("\\a");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    case '\v':
                        sb.Append("\\v");
                        break;
                    default:
                        if (r < ' ' || r == 0x7F)
                        {
                            sb.Append("\\x").Append(r.ToString("x2", CultureInfo.InvariantCulture));
                        }
                        else if (r < 0x10000)
                        {
                            sb.Append("\\u").Append(r.ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append("\\U").Append(r.ToString("x8", CultureInfo.InvariantCulture));
                        }
                        break;
                }
            }
            if (r > 0xFFFF)
            {
                i++;
            }
        }
        sb.Append(quote);
        return sb.ToString();
    }

    // Approximates Go's unicode.IsPrint: letters, marks, numbers, punctuation, symbols and ASCII space.
    private static bool IsPrint(string s, int i, int r)
    {
        if (r < 0x80)
        {
            return r >= 0x20 && r < 0x7F;
        }
        if (r == 0xFFFD)
        {
            return true;
        }
        switch (CharUnicodeInfo.GetUnicodeCategory(s, i))
        {
            case UnicodeCategory.SpaceSeparator:
            case UnicodeCategory.LineSeparator:
            case UnicodeCategory.ParagraphSeparator:
            case UnicodeCategory.Control:
            case UnicodeCategory.Format:
            case UnicodeCategory.Surrogate:
            case UnicodeCategory.PrivateUse:
            case UnicodeCategory.OtherNotAssigned:
                return false;
            default:
                return true;
        }
    }
}

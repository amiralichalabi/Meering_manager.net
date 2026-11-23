using System.Text;

namespace MeetingManager.Core;

/// <summary>Jalali calendar helpers: a port of backend/jalali.py (jalaali algorithm). Dates are 'YYYY/MM/DD' with ASCII digits.</summary>
public static class Jalali
{
    static readonly int[] Breaks = { -61, 9, 38, 199, 426, 686, 756, 818, 1111, 1181, 1210, 1635, 2060, 2097, 2192, 2262, 2324, 2394, 2456, 3178 };
    const string Fa = "۰۱۲۳۴۵۶۷۸۹";
    const string Ar = "٠١٢٣٤٥٦٧٨٩";

    // C# '/' and '%' truncate toward zero, exactly what the reference algorithm expects.
    static (int gy, int march, int leap) JalCal(int jy, bool withoutLeap = false)
    {
        if (jy < Breaks[0] || jy >= Breaks[^1]) throw new ArgumentException($"Invalid Jalali year {jy}");
        int gy = jy + 621, leapJ = -14, jp = Breaks[0], jump = 0;
        for (int i = 1; i < Breaks.Length; i++)
        {
            int jm = Breaks[i];
            jump = jm - jp;
            if (jy < jm) break;
            leapJ += jump / 33 * 8 + jump % 33 / 4;
            jp = jm;
        }
        int n = jy - jp;
        leapJ += n / 33 * 8 + (n % 33 + 3) / 4;
        if (jump % 33 == 4 && jump - n == 4) leapJ += 1;
        int leapG = gy / 4 - (gy / 100 + 1) * 3 / 4 - 150;
        int march = 20 + leapJ - leapG;
        if (withoutLeap) return (gy, march, 0);
        if (jump - n < 6) n = n - jump + (jump + 4) / 33 * 33;
        int leap = ((n + 1) % 33 - 1) % 4;
        if (leap == -1) leap = 4;
        return (gy, march, leap);
    }

    static int G2D(int gy, int gm, int gd)
    {
        int t = (gm - 8) / 6;
        int d = (gy + t + 100100) * 1461 / 4 + (153 * ((gm + 9) % 12) + 2) / 5 + gd - 34840408;
        return d - (gy + 100100 + t) / 100 * 3 / 4 + 752;
    }

    static (int, int, int) D2G(int jdn)
    {
        int j = 4 * jdn + 139361631;
        j += (4 * jdn + 183187720) / 146097 * 3 / 4 * 4 - 3908;
        int i = j % 1461 / 4 * 5 + 308;
        int gd = i % 153 / 5 + 1;
        int gm = i / 153 % 12 + 1;
        int gy = j / 1461 - 100100 + (8 - gm) / 6;
        return (gy, gm, gd);
    }

    static int J2D(int jy, int jm, int jd)
    {
        var (gy, march, _) = JalCal(jy, true);
        return G2D(gy, 3, march) + (jm - 1) * 31 - jm / 7 * (jm - 7) + jd - 1;
    }

    static (int, int, int) D2J(int jdn)
    {
        var (gy, _, _) = D2G(jdn);
        int jy = gy - 621;
        var (_, march, leap) = JalCal(jy);
        int jdn1f = G2D(gy, 3, march);
        int k = jdn - jdn1f;
        if (k >= 0)
        {
            if (k <= 185) return (jy, 1 + k / 31, k % 31 + 1);
            k -= 186;
        }
        else
        {
            jy -= 1; k += 179;
            if (leap == 1) k += 1;
        }
        return (jy, 7 + k / 30, k % 30 + 1);
    }

    public static bool IsLeap(int jy) => JalCal(jy).leap == 0;
    public static int DaysInMonth(int jy, int jm) => jm <= 6 ? 31 : jm <= 11 ? 30 : (IsLeap(jy) ? 30 : 29);
    public static bool IsValid(int jy, int jm, int jd)
    {
        try { return jm >= 1 && jm <= 12 && jd >= 1 && jd <= DaysInMonth(jy, jm); } catch (ArgumentException) { return false; }
    }
    public static (int y, int m, int d) ToJalali(DateOnly g) { var (y, m, d) = D2J(G2D(g.Year, g.Month, g.Day)); return (y, m, d); }
    public static DateOnly ToGregorian(int jy, int jm, int jd) { var (y, m, d) = D2G(J2D(jy, jm, jd)); return new DateOnly(y, m, d); }
    public static string Format(int y, int m, int d) => $"{y:D4}/{m:D2}/{d:D2}";
    public static string Today(DateOnly? now = null) { var (y, m, d) = ToJalali(now ?? DateOnly.FromDateTime(DateTime.Now)); return Format(y, m, d); }
    public static string FromDateTime(DateTime dt) { var (y, m, d) = ToJalali(DateOnly.FromDateTime(dt)); return Format(y, m, d); }

    public static string AsciiDigits(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            int i = Fa.IndexOf(c); if (i < 0) i = Ar.IndexOf(c);
            sb.Append(i >= 0 ? (char)('0' + i) : c);
        }
        return sb.ToString();
    }
    public static string FaDigits(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var c in text) sb.Append(c >= '0' && c <= '9' ? Fa[c - '0'] : c);
        return sb.ToString();
    }

    /// <summary>Canonical 'YYYY/MM/DD' for a user supplied Jalali date; empty stays empty; invalid throws FormatException.</summary>
    public static string Normalize(string? value)
    {
        var text = AsciiDigits(value).Trim();
        if (text == "") return "";
        foreach (var sep in new[] { "-", ".", "\\", "،" }) text = text.Replace(sep, "/");
        var parts = text.Split('/').Select(p => p.Trim()).ToArray();
        if (parts.Length != 3 || !parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)))
            throw new FormatException("تاریخ باید به صورت ۱۴۰۵/۰۷/۱۰ وارد شود.");
        if (parts.Any(p => p.Length > 6)) throw new FormatException("تاریخ واردشده در تقویم شمسی وجود ندارد.");
        int y = int.Parse(parts[0]), m = int.Parse(parts[1]), d = int.Parse(parts[2]);
        if (!IsValid(y, m, d)) throw new FormatException("تاریخ واردشده در تقویم شمسی وجود ندارد.");
        return Format(y, m, d);
    }

    public static int ToOrdinal(string value)
    {
        var p = value.Split('/').Select(int.Parse).ToArray();
        return ToGregorian(p[0], p[1], p[2]).DayNumber;
    }
}

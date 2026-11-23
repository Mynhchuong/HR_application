namespace HR_web.Helpers;

/// <summary>
/// Đổi dương lịch → âm lịch Việt Nam (thuật toán Hồ Ngọc Đức, múi giờ +7). Dùng để nhắc công nhân ăn chay đăng ký món
/// Chay vào mùng 1, 14, rằm và ngày cuối tháng âm (yêu cầu 2026-11-23, trang Menu/Today + Menu/ThisWeek).
/// </summary>
public static class LunarCalendar
{
    private const double TimeZone = 7.0;

    public readonly record struct LunarDate(int Day, int Month, int Year, bool Leap, bool IsLastDay)
    {
        /// <summary>Ngày ăn chay phổ biến: mùng 1, 14, rằm (15), ngày cuối tháng (30, hoặc 29 nếu tháng thiếu).</summary>
        public bool IsVegDay => Day == 1 || Day == 14 || Day == 15 || IsLastDay;

        /// <summary>Tên gọi ngày chay: "mùng 1", "14", "rằm", "30" (tháng thiếu: "29 – cuối tháng").</summary>
        public string VegName => Day == 1 ? "mùng 1" : Day == 15 ? "rằm" : IsLastDay && Day != 30 ? $"{Day} – cuối tháng" : Day.ToString();

        /// <summary>Nhãn ngắn trên ô ngày: "Mùng 1", "Rằm", "14 ÂL", "30 ÂL".</summary>
        public string VegShort => Day == 1 ? "Mùng 1" : Day == 15 ? "Rằm" : $"{Day} ÂL";

        public string Moon => Day == 14 || Day == 15 ? "🌕" : "🌑";

        public string Text => $"{Day}/{Month}{(Leap ? " nhuận" : "")}";
    }

    public static LunarDate FromSolar(DateTime date)
    {
        var (d, m, y, leap) = Convert(date.Day, date.Month, date.Year);
        var next = date.Date.AddDays(1);
        var (nd, _, _, _) = Convert(next.Day, next.Month, next.Year);
        return new LunarDate(d, m, y, leap, nd == 1);
    }

    private static long Int(double d) => (long)Math.Floor(d);

    private static long JdFromDate(int dd, int mm, int yy)
    {
        long a = Int((14 - mm) / 12.0), y = yy + 4800 - a, m = mm + 12 * a - 3;
        long jd = dd + Int((153 * m + 2) / 5.0) + 365 * y + Int(y / 4.0) - Int(y / 100.0) + Int(y / 400.0) - 32045;
        if (jd < 2299161) jd = dd + Int((153 * m + 2) / 5.0) + 365 * y + Int(y / 4.0) - 32083;
        return jd;
    }

    private static double NewMoon(long k)
    {
        double T = k / 1236.85, T2 = T * T, T3 = T2 * T, dr = Math.PI / 180;
        double jd1 = 2415020.75933 + 29.53058868 * k + 0.0001178 * T2 - 0.000000155 * T3;
        jd1 += 0.00033 * Math.Sin((166.56 + 132.87 * T - 0.009173 * T2) * dr);
        double M = 359.2242 + 29.10535608 * k - 0.0000333 * T2 - 0.00000347 * T3;
        double Mpr = 306.0253 + 385.81691806 * k + 0.0107306 * T2 + 0.00001236 * T3;
        double F = 21.2964 + 390.67050646 * k - 0.0016528 * T2 - 0.00000239 * T3;
        double c1 = (0.1734 - 0.000393 * T) * Math.Sin(M * dr) + 0.0021 * Math.Sin(2 * dr * M);
        c1 = c1 - 0.4068 * Math.Sin(Mpr * dr) + 0.0161 * Math.Sin(dr * 2 * Mpr);
        c1 -= 0.0004 * Math.Sin(dr * 3 * Mpr);
        c1 = c1 + 0.0104 * Math.Sin(dr * 2 * F) - 0.0051 * Math.Sin(dr * (M + Mpr));
        c1 = c1 - 0.0074 * Math.Sin(dr * (M - Mpr)) + 0.0004 * Math.Sin(dr * (2 * F + M));
        c1 = c1 - 0.0004 * Math.Sin(dr * (2 * F - M)) - 0.0006 * Math.Sin(dr * (2 * F + Mpr));
        c1 = c1 + 0.0010 * Math.Sin(dr * (2 * F - Mpr)) + 0.0005 * Math.Sin(dr * (2 * Mpr + M));
        double deltat = T < -11
            ? 0.001 + 0.000839 * T + 0.0002261 * T2 - 0.00000845 * T3 - 0.000000081 * T * T3
            : -0.000278 + 0.000265 * T + 0.000262 * T2;
        return jd1 + c1 - deltat;
    }

    private static long NewMoonDay(long k) => Int(NewMoon(k) + 0.5 + TimeZone / 24);

    private static long SunLongitude(double jdn)
    {
        double T = (jdn - 2451545.0) / 36525, T2 = T * T, dr = Math.PI / 180;
        double M = 357.52910 + 35999.05030 * T - 0.0001559 * T2 - 0.00000048 * T * T2;
        double L0 = 280.46645 + 36000.76983 * T + 0.0003032 * T2;
        double DL = (1.914600 - 0.004817 * T - 0.000014 * T2) * Math.Sin(dr * M);
        DL += (0.019993 - 0.000101 * T) * Math.Sin(dr * 2 * M) + 0.000290 * Math.Sin(dr * 3 * M);
        double L = (L0 + DL) * dr;
        L -= Math.PI * 2 * Int(L / (Math.PI * 2));
        return Int(L / Math.PI * 6);
    }

    private static long SunLongitudeOfDay(long dayNumber) => SunLongitude(dayNumber - 0.5 - TimeZone / 24);

    private static long LunarMonth11(int yy)
    {
        long off = JdFromDate(31, 12, yy) - 2415021;
        long k = Int(off / 29.530588853);
        long nm = NewMoonDay(k);
        if (SunLongitudeOfDay(nm) >= 9) nm = NewMoonDay(k - 1);
        return nm;
    }

    private static int LeapMonthOffset(long a11)
    {
        long k = Int((a11 - 2415021.076998695) / 29.530588853 + 0.5);
        long last;
        int i = 1;
        long arc = SunLongitudeOfDay(NewMoonDay(k + i));
        do
        {
            last = arc;
            i++;
            arc = SunLongitudeOfDay(NewMoonDay(k + i));
        } while (arc != last && i < 14);
        return i - 1;
    }

    private static (int Day, int Month, int Year, bool Leap) Convert(int dd, int mm, int yy)
    {
        long dayNumber = JdFromDate(dd, mm, yy);
        long k = Int((dayNumber - 2415021.076998695) / 29.530588853);
        long monthStart = NewMoonDay(k + 1);
        if (monthStart > dayNumber) monthStart = NewMoonDay(k);
        long a11 = LunarMonth11(yy), b11 = a11;
        int lunarYear;
        if (a11 >= monthStart) { lunarYear = yy; a11 = LunarMonth11(yy - 1); }
        else { lunarYear = yy + 1; b11 = LunarMonth11(yy + 1); }
        int lunarDay = (int)(dayNumber - monthStart + 1);
        int diff = (int)Int((monthStart - a11) / 29.0);
        bool leap = false;
        int lunarMonth = diff + 11;
        if (b11 - a11 > 365)
        {
            int leapDiff = LeapMonthOffset(a11);
            if (diff >= leapDiff)
            {
                lunarMonth = diff + 10;
                if (diff == leapDiff) leap = true;
            }
        }
        if (lunarMonth > 12) lunarMonth -= 12;
        if (lunarMonth >= 11 && diff < 4) lunarYear -= 1;
        return (lunarDay, lunarMonth, lunarYear, leap);
    }
}

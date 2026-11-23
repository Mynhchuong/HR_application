using HR_api.Data;
using HR_api.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

/// <summary>
/// Số suất ăn căn tin cần nấu (yêu cầu 2026-11-23) — dùng chung cho trang CanteenBread/ChangeLog và email chốt
/// số lượng hằng ngày (CanteenDailyMailService). Kèm "giờ chốt" HR đặt (HR_CANTEEN_MAIL_CFG): tới giờ chốt thì khoá
/// đổi món bữa trưa của ngày làm việc kế tiếp.
/// </summary>
public class CanteenSummaryService
{
    public static readonly string[] Foods = { "M", "N", "C", "B" };

    private readonly OracleService _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CanteenSummaryService> _log;

    public CanteenSummaryService(OracleService db, IMemoryCache cache, ILogger<CanteenSummaryService> log)
    {
        _db = db; _cache = cache; _log = log;
    }

    public sealed class MealCount
    {
        public int M { get; set; }
        public int N { get; set; }
        public int C { get; set; }
        public int B { get; set; }
        public int total => M + N + C + B;

        public int Get(string food) => food switch { "M" => M, "N" => N, "C" => C, "B" => B, _ => 0 };
        public void Add(string food, int n)
        {
            switch (food) { case "N": N += n; break; case "C": C += n; break; case "B": B += n; break; default: M += n; break; }
        }
        /// <summary>Dự kiến thực ăn = số đăng ký × tỉ lệ ăn thật của từng món.</summary>
        public MealCount Scale(IReadOnlyDictionary<string, double> ratio)
        {
            int S(string f) => (int)Math.Round(Get(f) * (ratio.TryGetValue(f, out var r) ? r : 1d));
            return new MealCount { M = S("M"), N = S("N"), C = S("C"), B = S("B") };
        }
    }

    public sealed class SlotSummary
    {
        public string slot { get; set; } = "-";
        public MealCount count { get; set; } = new();
        public MealCount? expected { get; set; }
    }

    public sealed class DaySummary
    {
        public string dat { get; set; } = "";
        public MealCount lunch { get; set; } = new();
        public MealCount? expected { get; set; }
        public List<SlotSummary> slots { get; set; } = new();
        public MealCount ot { get; set; } = new();
        public int leave { get; set; }
        public int maternity { get; set; }
        /// <summary>Ngày có "CA 2 ĐẶC BIỆT" trong thực đơn (thứ Bảy cuối tháng) — người đi ăn đông hơn ngày thường.</summary>
        public bool special { get; set; }
    }

    public sealed class EatRatio
    {
        /// <summary>Tỉ lệ ăn thật / đăng ký từng món của ngày thường (~2 tuần gần nhất, đã bỏ ngày bất thường + ngày đặc biệt).</summary>
        public Dictionary<string, double> ratio { get; set; } = new();
        /// <summary>Tỉ lệ cho ngày đặc biệt (lấy từ tối đa 3 ngày đặc biệt gần nhất); null = chưa có dữ liệu.</summary>
        public Dictionary<string, double>? special { get; set; }
        /// <summary>Tỉ lệ ăn thật / đăng ký của cả ngày (không tách món) — dùng để ra TỔNG số cần nấu.</summary>
        public double total { get; set; } = 1;
        public double? specialTotal { get; set; }
        public int days { get; set; }
        public string? from { get; set; }
        public string? to { get; set; }
        public List<string> skipped { get; set; } = new();   // ngày bị bỏ vì bất thường (vd máy quẹt thẻ mất log)
    }

    /// <summary>% dự phòng mặc định cộng vào số cần nấu — HR sửa được (HR_CANTEEN_MAIL_CFG.BUFFER_PCT).
    /// Công ty chấp nhận dư, KHÔNG chấp nhận thiếu (công nhân phải chờ). Đối chiếu 67 ngày 01/09–21/11/2026:
    /// Tổng = đăng ký × tỉ lệ chung cả ngày: +2,8% là mức thấp nhất không ngày nào thiếu (dư TB ~173); chọn +3% để có
    /// biên an toàn (dư TB ~187, nhiều nhất ~335). Cách cũ nhân tỉ lệ riêng từng món cần tới +4% (dư TB ~250).</summary>
    public const double DefaultBufferPct = 3.0;

    // ── Số suất theo đăng ký ────────────────────────────────────────────────────────────────────
    //   Bữa trưa: NV đang làm (đã vào làm, chưa nghỉ việc, không nghỉ thai sản — MATERNITY_LIST) và CÓ lịch ca ngày đó
    //             (EBM300 — log ăn thật cho thấy người không có ca, vd 32 NV EXPERT, không bao giờ ăn), TRỪ người nghỉ:
    //             đơn nghỉ trên app đã duyệt (APPROVED) / được quản lý sắp lịch (ASSIGNED), cộng ngày nghỉ HR nhập
    //             thẳng ERP (EFM410). Không dùng riêng EFM410 vì đơn ngày tương lai chưa có trong ERP.
    //             Món = dòng LUNCH trong CANTEEN_ORDER, không có = Mặn. Đăng ký dài hạn đã ghi sẵn từng ngày.
    //             Chia theo giờ nghỉ trưa của lịch ca (EBM300.T_LUNCH) để căn tin nấu theo giờ.
    //   Tăng ca : NV có OT đã xác nhận ngày đó (HR_OT_REQUEST), cũng trừ người nghỉ. Món = dòng OT, không có = Mặn.
    //   Bỏ Chủ nhật và ngày lễ công ty (EAM800). Tối đa 31 ngày/lần.
    public async Task<List<DaySummary>> GetAsync(DateTime df, DateTime dt,
        string? deptcd = null, string? linecd = null, string? workcd = null, string? clerkEmpcd = null)
    {
        // Gồm cả NV đã nghỉ việc SAU ngày đầu khoảng (từng ngày vẫn lọc RETDAT > ngày đó bên dưới) — nếu chỉ lấy
        // JEAJIKGB='Y' thì ngày cũ thiếu người đã nghỉ việc sau đó dù hôm đó họ vẫn đi ăn (03/10/2026: 110 người),
        // làm sai tỉ lệ ăn thật của 2 tuần gần nhất.
        var empWhere = new List<string> { "(ec.JEAJIKGB = 'Y' OR ec.RETDAT > :RET_FROM)" };
        var ps = new List<OracleParameter> { new("RET_FROM", df.ToString("yyyyMMdd")) };
        if (!string.IsNullOrEmpty(deptcd)) { empWhere.Add("ec.DEPTCD = :DEPTCD"); ps.Add(new OracleParameter("DEPTCD", deptcd)); }
        if (!string.IsNullOrEmpty(linecd)) { empWhere.Add("ec.LINECD = :LINECD"); ps.Add(new OracleParameter("LINECD", linecd)); }
        if (!string.IsNullOrEmpty(workcd)) { empWhere.Add("ec.WORKCD = :WORKCD"); ps.Add(new OracleParameter("WORKCD", workcd)); }
        string scopeSql = "";
        if (!string.IsNullOrEmpty(clerkEmpcd))
        {
            var sc = OTScopeFilterHelper.ForScopeByTuple(clerkEmpcd, empAlias: "ec", prefix: "CK");
            scopeSql = sc.SqlClause;
            ps.AddRange(sc.Params);
        }

        var sql = $@"
            WITH D AS (
                SELECT DD, TO_CHAR(DD,'YYYYMMDD') DAT FROM (
                    SELECT :DF + LEVEL - 1 DD FROM DUAL CONNECT BY LEVEL <= :NDAYS)
                WHERE TO_CHAR(DD,'DY','NLS_DATE_LANGUAGE=AMERICAN') <> 'SUN'
                  AND NOT EXISTS (SELECT 1 FROM HRMS.EAM800 h WHERE TRUNC(h.HUILDAY) = DD)
            ),
            EMP AS (
                SELECT ec.EMPCD, ec.RETDAT, ec.IGENTDAT FROM HRMS.ECM100 ec
                WHERE {string.Join(" AND ", empWhere)} {scopeSql}
            ),
            LV AS (
                SELECT x.EMPCD, x.FR_DAT FROM HRMS.EFM410 x
                WHERE x.FR_DAT BETWEEN :DF2 AND :DT2
                UNION
                SELECT l.EMPCD, d.DD FROM D d
                JOIN HRMS.HR_LEAVE_REQUEST l ON d.DD BETWEEN TRUNC(l.FROM_DATE) AND TRUNC(l.TO_DATE)
                JOIN HRMS.HR_REQUEST r ON r.REQUEST_ID = l.REQUEST_ID
                WHERE r.STATUS IN ('APPROVED','ASSIGNED')
                  AND l.FROM_DATE <= :DT4 + 1 AND l.TO_DATE >= :DF4
            ),
            CO AS (
                SELECT co.EMPCD, co.DAT, co.TYPE_MEAL, co.TYPE_OF_FOOD FROM HRMS.CANTEEN_ORDER co
                WHERE co.DAT BETWEEN :DFS AND :DTS
            ),
            OT AS (
                SELECT DISTINCT o.EMPCD, o.WORK_DATE FROM HRMS.HR_OT_REQUEST o
                WHERE o.WORK_DATE BETWEEN :DF3 AND :DT3 AND o.CONFIRM_STATUS = 'CONFIRMED'
            ),
            MT AS (
                SELECT DISTINCT d.DD, m.EMPCD FROM D d
                JOIN HRMS.MATERNITY_LIST m ON d.DD BETWEEN m.LEAVE_DAT AND m.RETURN_DAT - 1
            ),
            SH AS (
                SELECT s.EMPCD, s.DAT, s.T_LUNCH FROM HRMS.EBM300 s
                WHERE s.DAT BETWEEN :DF5 AND :DT5
            ),
            -- NV còn làm, đã vào làm, không nghỉ thai sản tới ngày đó
            ED AS (
                SELECT d.DD, d.DAT, e.EMPCD FROM D d CROSS JOIN EMP e
                WHERE (e.RETDAT IS NULL OR e.RETDAT > d.DAT)
                  AND (e.IGENTDAT IS NULL OR e.IGENTDAT <= d.DAT)
                  AND NOT EXISTS (SELECT 1 FROM MT m WHERE m.EMPCD = e.EMPCD AND m.DD = d.DD)
            )
            SELECT DAT, MEAL, FOOD, SLOT, COUNT(*) CNT FROM (
                SELECT x.DAT, 'LUNCH' MEAL, NVL(c.TYPE_OF_FOOD,'M') FOOD, NVL(sh.T_LUNCH,'-') SLOT
                FROM ED x
                LEFT JOIN CO c ON c.EMPCD = x.EMPCD AND c.DAT = x.DAT AND c.TYPE_MEAL = 'LUNCH'
                LEFT JOIN LV l ON l.EMPCD = x.EMPCD AND l.FR_DAT = x.DD
                JOIN SH sh ON sh.EMPCD = x.EMPCD AND sh.DAT = x.DD   -- không có lịch ca ngày đó = không đi làm (vd EXPERT)
                WHERE l.EMPCD IS NULL
                UNION ALL
                SELECT d.DAT, 'OT', NVL(c.TYPE_OF_FOOD,'M'), '-'
                FROM D d JOIN OT o ON o.WORK_DATE = d.DD
                JOIN EMP e ON e.EMPCD = o.EMPCD
                LEFT JOIN CO c ON c.EMPCD = e.EMPCD AND c.DAT = d.DAT AND c.TYPE_MEAL = 'OT'
                LEFT JOIN LV l ON l.EMPCD = e.EMPCD AND l.FR_DAT = d.DD
                WHERE l.EMPCD IS NULL
                UNION ALL
                -- Số người nghỉ (đã trừ) để căn tin đối chiếu
                SELECT x.DAT, 'LEAVE', '-', '-'
                FROM ED x JOIN LV l ON l.EMPCD = x.EMPCD AND l.FR_DAT = x.DD
                UNION ALL
                SELECT d.DAT, 'MATERNITY', '-', '-'
                FROM D d JOIN MT m ON m.DD = d.DD
                JOIN EMP e ON e.EMPCD = m.EMPCD
                WHERE (e.RETDAT IS NULL OR e.RETDAT > d.DAT)
            ) GROUP BY DAT, MEAL, FOOD, SLOT";

        OracleParameter Dp(string n, DateTime v) => new OracleParameter { ParameterName = n, OracleDbType = OracleDbType.Date, Value = v };
        ps.Add(Dp("DF", df));
        ps.Add(new OracleParameter("NDAYS", (int)(dt - df).TotalDays + 1));
        ps.Add(Dp("DF2", df)); ps.Add(Dp("DT2", dt));
        ps.Add(new OracleParameter("DFS", df.ToString("yyyyMMdd"))); ps.Add(new OracleParameter("DTS", dt.ToString("yyyyMMdd")));
        ps.Add(Dp("DF3", df)); ps.Add(Dp("DT3", dt));
        ps.Add(Dp("DF4", df)); ps.Add(Dp("DT4", dt));
        ps.Add(Dp("DF5", df)); ps.Add(Dp("DT5", dt));

        var rows = await _db.ExecuteQueryAsync(sql,
            r => (Dat: r["DAT"]?.ToString() ?? "", Meal: r["MEAL"]?.ToString() ?? "", Food: r["FOOD"]?.ToString() ?? "",
                  Slot: r["SLOT"]?.ToString() ?? "-", Cnt: Convert.ToInt32(r["CNT"])),
            ps.ToArray());

        MealCount Count(IEnumerable<(string Dat, string Meal, string Food, string Slot, int Cnt)> src)
        {
            var m = new MealCount();
            foreach (var x in src) m.Add(x.Food, x.Cnt);
            return m;
        }

        return rows.GroupBy(x => x.Dat).OrderBy(g => g.Key).Select(g => new DaySummary
        {
            dat   = g.Key,
            lunch = Count(g.Where(x => x.Meal == "LUNCH")),
            // Theo giờ nghỉ trưa trong lịch ca (EBM300.T_LUNCH, vd "1115-1215")
            slots = g.Where(x => x.Meal == "LUNCH").GroupBy(x => x.Slot)
                     .OrderBy(s => s.Key == "-" ? 1 : 0).ThenBy(s => s.Key)
                     .Select(s => new SlotSummary { slot = s.Key, count = Count(s) }).ToList(),
            ot        = Count(g.Where(x => x.Meal == "OT")),
            leave     = g.Where(x => x.Meal == "LEAVE").Sum(x => x.Cnt),
            maternity = g.Where(x => x.Meal == "MATERNITY").Sum(x => x.Cnt)
        }).ToList();
    }

    // ── Tỉ lệ ăn thật ───────────────────────────────────────────────────────────────────────────
    // Đối chiếu log quẹt thẻ (CANTEEN_DAILY_LOG) 01/09–21/11/2026: số theo đăng ký luôn DƯ ~8% (người có ca nhưng hôm
    // đó không ăn, mỗi ngày một nhóm khác nhau). TỔNG cần nấu = đăng ký × tỉ lệ ăn thật / đăng ký của cả ngày (~12 ngày làm
    // việc gần nhất) × (1 + % dự phòng HR đặt, mặc định 3%); rồi mới chia ra món / khung giờ theo tỉ lệ từng món. Tính
    // tổng bằng 1 tỉ lệ chung chính xác hơn cộng dồn tỉ lệ từng món (sai số các món cộng dồn). Ba trường hợp đặc biệt đã gặp trên dữ liệu thật:
    //   1. Ngày máy quẹt thẻ mất log (24/09: 2.848 người thay vì ~6.900) → bỏ khỏi cửa sổ, không thì thiếu 5–7% suốt 2 tuần.
    //   2. Thứ Bảy cuối tháng "CA 2 ĐẶC BIỆT" (thực đơn có chữ ĐẶC BIỆT): đi ăn đông hơn, Mặn ~95% thay vì ~87% → dùng
    //      tỉ lệ của các ngày đặc biệt trước; ngày đặc biệt cũng bị bỏ khỏi cửa sổ ngày thường.
    //   3. Người đã nghỉ việc vẫn được tính cho những ngày trước khi nghỉ (xem GetAsync).
    // Kết quả (chưa cộng dự phòng): lệch trung bình 0,9%, không còn ngày thiếu quá 3,5% (trước khi xử lý: thiếu tới 11%). Tính cho toàn công ty
    // (không theo bộ lọc phòng), cache theo ngày.
    /// <param name="asOf">Tính như vào ngày này (mặc định hôm nay) — dùng để đối chiếu ngược với log ăn thật.</param>
    public async Task<EatRatio> GetEatRatioAsync(DateTime? asOf = null)
    {
        var today = (asOf ?? DateTime.Today).Date;
        var key = "canteen-eat-ratio-" + today.ToString("yyyyMMdd");
        if (_cache.TryGetValue(key, out EatRatio? cached) && cached != null) return cached;

        var to = today.AddDays(-1);                   // hôm nay chưa hết ngày → chưa tính
        var result = new EatRatio();
        bool ok = false;
        try
        {
            var specialDays = await GetSpecialDaysAsync(today.AddDays(-150), to);
            List<DaySummary> useReg = new();
            Dictionary<string, Dictionary<string, int>> act = new();
            // 14 ngày gần nhất; nếu không đủ 5 ngày dùng được (vd ngay sau Tết nghỉ dài) thì lùi xa hơn
            foreach (var span in new[] { 14, 28, 42 })
            {
                var from = today.AddDays(-span);
                var reg = (await GetAsync(from, to)).Where(d => d.lunch.total > 0).ToList();
                act = await GetActualAsync(from, to);

                // Ngày dùng được: có log ăn, không phải ngày đặc biệt, tỉ lệ không lệch quá 8 điểm so với trung vị
                var cand = reg.Where(d => act.ContainsKey(d.dat) && !specialDays.Contains(d.dat)).ToList();
                var dayRatio = cand.ToDictionary(d => d.dat, d => act[d.dat].Values.Sum() / (double)d.lunch.total);
                result.skipped.Clear();
                if (dayRatio.Count > 0)
                {
                    var med = Median(dayRatio.Values);
                    foreach (var kv in dayRatio.Where(kv => Math.Abs(kv.Value - med) > 0.08)) result.skipped.Add(kv.Key);
                }
                useReg = cand.Where(d => !result.skipped.Contains(d.dat)).ToList();
                if (useReg.Count >= 5) break;
            }

            if (useReg.Count > 0)
            {
                result.ratio = FoodRatio(useReg, act);
                double rt = useReg.Sum(d => d.lunch.total), at = useReg.Sum(d => act[d.dat].Values.Sum());
                result.total = rt > 0 ? at / rt : 1d;
                result.days = useReg.Count;
                result.from = useReg.Min(d => d.dat);
                result.to = useReg.Max(d => d.dat);
            }

            // Ngày đặc biệt: tối đa 3 ngày gần nhất có log
            var prevSpecial = specialDays.Where(d => string.CompareOrdinal(d, to.ToString("yyyyMMdd")) <= 0)
                                         .OrderByDescending(d => d).Take(3).ToList();
            var spReg = new List<DaySummary>();
            var spAct = new Dictionary<string, Dictionary<string, int>>();
            foreach (var sd in prevSpecial)
            {
                var day = DateTime.ParseExact(sd, "yyyyMMdd", null);
                var r = (await GetAsync(day, day)).FirstOrDefault();
                var a = await GetActualAsync(day, day);
                if (r != null && r.lunch.total > 0 && a.TryGetValue(sd, out var av) && av.Values.Sum() > r.lunch.total * 0.5)
                {
                    spReg.Add(r); spAct[sd] = av;
                }
            }
            if (spReg.Count > 0)
            {
                var sp = FoodRatio(spReg, spAct);
                // Ngày đặc biệt thường không phát bánh (đăng ký Bánh = 0) → Bánh lấy theo tỉ lệ Mặn
                if (spReg.Sum(d => d.lunch.B) < 50) sp["B"] = sp["M"];
                result.special = sp;
                result.specialTotal = spAct.Values.Sum(a => a.Values.Sum()) / (double)spReg.Sum(d => d.lunch.total);
            }
            ok = true;
        }
        catch (Exception ex) { _log.LogWarning(ex, "Canteen eat ratio failed"); }

        // Lỗi DB → tỉ lệ 100% (nấu theo đăng ký, thừa chứ không thiếu), chỉ giữ 5 phút rồi tính lại
        foreach (var f in Foods) if (!result.ratio.ContainsKey(f)) result.ratio[f] = 1d;
        _cache.Set(key, result, ok ? TimeSpan.FromHours(3) : TimeSpan.FromMinutes(5));
        return result;
    }

    private static Dictionary<string, double> FoodRatio(List<DaySummary> reg, Dictionary<string, Dictionary<string, int>> act)
    {
        var res = new Dictionary<string, double>();
        foreach (var f in Foods)
        {
            double r = reg.Sum(d => d.lunch.Get(f));
            double a = reg.Sum(d => act[d.dat].TryGetValue(f, out var n) ? n : 0);
            res[f] = r > 0 ? a / r : 1d;
        }
        return res;
    }

    private static double Median(IEnumerable<double> xs)
    {
        var a = xs.OrderBy(x => x).ToArray();
        return a.Length == 0 ? 0 : a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2;
    }

    /// <summary>Số người thật sự ăn trưa theo ngày + món (log quẹt thẻ): yyyyMMdd → món → số người.</summary>
    private async Task<Dictionary<string, Dictionary<string, int>>> GetActualAsync(DateTime from, DateTime to)
    {
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT TO_CHAR(DAT,'YYYYMMDD') D, FOOD, COUNT(DISTINCT EMPCD) N
              FROM HRMS.CANTEEN_DAILY_LOG
             WHERE TYPE_MEAL = 'LUNCH' AND DAT BETWEEN :F AND :T
             GROUP BY TO_CHAR(DAT,'YYYYMMDD'), FOOD",
            r => (D: r["D"]?.ToString() ?? "", Food: r["FOOD"]?.ToString() ?? "M", N: Convert.ToInt32(r["N"])),
            new OracleParameter("F", OracleDbType.Date) { Value = from.Date },
            new OracleParameter("T", OracleDbType.Date) { Value = to.Date });
        var res = new Dictionary<string, Dictionary<string, int>>();
        foreach (var x in rows)
        {
            if (!res.TryGetValue(x.D, out var m)) res[x.D] = m = new Dictionary<string, int>();
            m[x.Food] = (m.TryGetValue(x.Food, out var n) ? n : 0) + x.N;
        }
        return res;
    }

    /// <summary>
    /// Ngày "đặc biệt" theo thực đơn tuần (HR_MENU_*, mọi trạng thái — tuần cũ chuyển về DRAFT): có món chứa chữ
    /// "ĐẶC BIỆT" (vd "CA 2 ĐẶC BIỆT" thứ Bảy cuối tháng). DAY_NO: thứ Hai = 2 … thứ Bảy = 7, Chủ nhật = 0.
    /// </summary>
    public async Task<HashSet<string>> GetSpecialDaysAsync(DateTime from, DateTime to)
    {
        var rows = await _db.ExecuteQueryAsync(@"
            WITH D AS (SELECT :F + LEVEL - 1 DD FROM DUAL CONNECT BY LEVEL <= :N)
            SELECT TO_CHAR(d.DD,'YYYYMMDD') X FROM D d WHERE EXISTS (
                SELECT 1 FROM HRMS.HR_MENU_WEEK W
                JOIN HRMS.HR_MENU_DETAIL MD ON MD.WEEK_ID = W.ID
                JOIN HRMS.HR_MENU_FOOD F ON F.ID = MD.FOOD_ID
                WHERE d.DD BETWEEN TRUNC(W.FROM_DATE) AND TRUNC(W.TO_DATE)
                  AND MD.DAY_NO = CASE WHEN d.DD - TRUNC(d.DD,'IW') = 6 THEN 0 ELSE d.DD - TRUNC(d.DD,'IW') + 2 END
                  AND (UPPER(F.FOOD_NAME) LIKE N'%ĐẶC BIỆT%' OR UPPER(F.FOOD_NAME) LIKE '%DAC BIET%'))",
            r => r["X"]?.ToString() ?? "",
            new OracleParameter("F", OracleDbType.Date) { Value = from.Date },
            new OracleParameter("N", (int)(to.Date - from.Date).TotalDays + 1));
        return rows.ToHashSet();
    }

    /// <summary>
    /// Gắn số "cần nấu" cho từng ngày và từng khung giờ:
    ///   TỔNG = làm tròn lên (đăng ký × tỉ lệ chung cả ngày × (1 + dự phòng %)),
    ///   rồi chia tổng đó ra (khung giờ × món) theo tỉ trọng "đăng ký × tỉ lệ từng món" (chia phần dư lớn nhất để cộng lại
    ///   đúng bằng tổng). Ngày đặc biệt dùng tỉ lệ của các ngày đặc biệt trước và dồn Bánh sang Mặn.
    /// </summary>
    public static void ApplyExpected(IEnumerable<DaySummary> days, EatRatio ratio, ISet<string>? specialDays, double bufferPct)
    {
        var k = 1 + bufferPct / 100d;
        Dictionary<string, double> spFood;
        double spTotal;
        if (ratio.special != null && ratio.specialTotal != null) { spFood = ratio.special; spTotal = ratio.specialTotal.Value; }
        else
        {
            // Chưa có ngày đặc biệt nào trước đó: tỉ lệ chung + 2,5 điểm (mức chênh đã gặp), tối đa 100%
            spTotal = Math.Min(1d, ratio.total + 0.025);
            spFood = Foods.ToDictionary(f => f, _ => spTotal);
        }

        foreach (var d in days)
        {
            d.special = specialDays?.Contains(d.dat) == true;
            var food  = d.special ? spFood : ratio.ratio;
            var total = d.special ? spTotal : ratio.total;
            int target = (int)Math.Ceiling(d.lunch.total * total * k - 1e-9);

            // Tỉ trọng từng ô (khung giờ × món)
            var cells = new List<(SlotSummary S, string F, double W)>();
            foreach (var s in d.slots)
                foreach (var f in Foods)
                {
                    var w = s.count.Get(f) * (food.TryGetValue(f, out var r) ? r : 1d);
                    if (w > 0) cells.Add((s, f, w));
                }
            double sumW = cells.Sum(c => c.W);
            foreach (var s in d.slots) s.expected = new MealCount();
            if (sumW > 0 && target > 0)
            {
                var alloc = cells.Select(c => (c, exact: target * c.W / sumW)).ToList();
                var fl = alloc.Select(x => (x.c, n: (int)Math.Floor(x.exact), rem: x.exact - Math.Floor(x.exact))).ToList();
                int left = target - fl.Sum(x => x.n);
                foreach (var x in fl.OrderByDescending(x => x.rem).Take(left)) x.c.S.expected!.Add(x.c.F, 1);
                foreach (var x in fl) x.c.S.expected!.Add(x.c.F, x.n);
            }
            // Ngày đặc biệt không phát bánh (29/08, 26/09, 31/10/2026: không ai ăn Bánh) → người đăng ký Bánh ăn Mặn
            if (d.special) foreach (var s in d.slots) { s.expected!.M += s.expected.B; s.expected.B = 0; }

            var e = new MealCount();
            foreach (var s in d.slots) foreach (var f in Foods) e.Add(f, s.expected!.Get(f));
            d.expected = e;
        }
    }

    // ── Giờ chốt số lượng (HR_CANTEEN_MAIL_CFG) ─────────────────────────────────────────────────
    private static volatile bool _ready;
    private static DateTime _nextProbeUtc = DateTime.MinValue;

    public async Task<bool> TablesReadyAsync()
    {
        if (_ready) return true;
        if (DateTime.UtcNow < _nextProbeUtc) return false;
        bool ok;
        try
        {
            var n = (await _db.ExecuteQueryAsync(@"
                SELECT COUNT(*) AS C FROM ALL_TABLES WHERE OWNER = 'HRMS'
                   AND TABLE_NAME IN ('HR_CANTEEN_MAIL_CFG', 'HR_CANTEEN_MAIL_TO', 'HR_CANTEEN_MAIL_LOG')",
                r => Convert.ToInt32(r["C"]))).FirstOrDefault();
            ok = n == 3;
        }
        catch { ok = false; }
        _ready = ok;
        _nextProbeUtc = ok ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(60);
        return ok;
    }

    public sealed record CutoffConfig(bool Active, int Hour, int Minute, double BufferPct = DefaultBufferPct)
    {
        public string Time => $"{Hour:D2}:{Minute:D2}";
    }

    /// <summary>Cấu hình giờ chốt; null = chưa chạy script tạo bảng (chức năng coi như tắt).</summary>
    public async Task<CutoffConfig?> GetCutoffConfigAsync()
    {
        if (!await TablesReadyAsync()) return null;
        var row = (await _db.ExecuteQueryAsync(
            "SELECT SEND_TIME, IS_ACTIVE, BUFFER_PCT FROM HRMS.HR_CANTEEN_MAIL_CFG WHERE ID = 1",
            r => (Time: r["SEND_TIME"]?.ToString() ?? "21:00", Active: (r["IS_ACTIVE"]?.ToString() ?? "N") == "Y",
                  Buffer: r["BUFFER_PCT"] == DBNull.Value ? DefaultBufferPct : Convert.ToDouble(r["BUFFER_PCT"])))).FirstOrDefault();
        if (row.Time == null) return new CutoffConfig(false, 21, 0);
        return TryParseTime(row.Time, out var h, out var m)
            ? new CutoffConfig(row.Active, h, m, row.Buffer) : new CutoffConfig(row.Active, 21, 0, row.Buffer);
    }

    /// <summary>% dự phòng hiện hành (chưa tạo bảng → mặc định).</summary>
    public async Task<double> GetBufferPctAsync() => (await GetCutoffConfigAsync())?.BufferPct ?? DefaultBufferPct;

    public static bool TryParseTime(string? s, out int hour, out int minute)
    {
        hour = minute = 0;
        var p = (s ?? "").Trim().Split(':');
        return p.Length == 2 && int.TryParse(p[0], out hour) && int.TryParse(p[1], out minute)
            && hour is >= 0 and <= 23 && minute is >= 0 and <= 59;
    }

    public async Task<HashSet<DateTime>> GetHolidaysAsync(DateTime from, DateTime to)
    {
        var rows = await _db.ExecuteQueryAsync(
            "SELECT TRUNC(HUILDAY) D FROM HRMS.EAM800 WHERE HUILDAY BETWEEN :F AND :T",
            r => Convert.ToDateTime(r["D"]).Date,
            new OracleParameter("F", OracleDbType.Date) { Value = from.Date },
            new OracleParameter("T", OracleDbType.Date) { Value = to.Date.AddDays(1) });
        return rows.ToHashSet();
    }

    /// <summary>Số ngày tra ngày lễ lùi về trước khi tìm "ngày làm việc liền trước" — đủ dài cho Tết (~10 ngày nghỉ).</summary>
    public const int HolidayLookbackDays = 40;

    public static bool IsWorkingDay(DateTime d, HashSet<DateTime> holidays) =>
        d.DayOfWeek != DayOfWeek.Sunday && !holidays.Contains(d.Date);

    public static DateTime NextWorkingDay(DateTime d, HashSet<DateTime> holidays)
    {
        var x = d.Date.AddDays(1);
        while (!IsWorkingDay(x, holidays)) x = x.AddDays(1);
        return x;
    }

    /// <summary>
    /// Thời điểm chốt bữa trưa của ngày <paramref name="day"/> = giờ chốt vào ngày làm việc liền trước nó
    /// (vd thứ Hai chốt 21:00 thứ Bảy; ngày sau lễ chốt vào ngày làm việc trước lễ). Tới thời điểm này email đã gửi
    /// số của ngày đó nên khoá đổi món. Ngày nghỉ (CN/lễ) không có suất → trả null.
    /// </summary>
    public static DateTime? LunchCutoffFor(DateTime day, CutoffConfig cfg, HashSet<DateTime> holidays)
    {
        if (!cfg.Active || !IsWorkingDay(day, holidays)) return null;
        var prev = day.Date.AddDays(-1);
        while (!IsWorkingDay(prev, holidays)) prev = prev.AddDays(-1);
        return prev.AddHours(cfg.Hour).AddMinutes(cfg.Minute);
    }

    /// <summary>Đổi món bữa trưa ngày <paramref name="day"/> đã bị chốt chưa (null = chưa chốt / chức năng tắt).</summary>
    public async Task<DateTime?> LunchLockedSinceAsync(DateTime day)
    {
        var cfg = await GetCutoffConfigAsync();
        if (cfg == null || !cfg.Active) return null;
        var hol = await GetHolidaysAsync(day.AddDays(-HolidayLookbackDays), day);
        var cut = LunchCutoffFor(day, cfg, hol);
        return cut != null && DateTime.Now >= cut ? cut : null;
    }
}

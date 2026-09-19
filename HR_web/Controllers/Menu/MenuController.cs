using ClosedXML.Excel;
using HR_web.API.Service;
using HR_web.Helpers;
using HR_web.Models.Menu;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Globalization;
using System.Net;

namespace HR_web.Controllers.Menu;

[Authorize]
public class MenuController : BaseController
{
    private readonly MenuService _svc;
    private readonly IWebHostEnvironment _env;

    private const string CantinFolder = HR_web.Controllers.ImageController.ShareRoot + @"\MY_SAMHO_CANTIN";

// Cấu trúc template Excel: row bắt đầu data của mỗi ca
    private static readonly (string Shift, int DataStartRow)[] Sections =
    {
        ("CA1", 7),
        ("CA2", 16),
        ("CA3", 25),
    };

    // Cột → MEAL_TYPE (1-based index trong ClosedXML)
    private static readonly Dictionary<int, string> MealCols = new()
    {
        { 3, "MAN" }, { 4, "DU_KIEN" }, { 5, "NHE" }, { 6, "CHAY" }, { 7, "BANH" }
    };

    private static readonly Dictionary<string, string> MealLabel = new()
    {
        { "MAN", "Món mặn" }, { "DU_KIEN", "Món dự kiến" },
        { "NHE", "Món nhẹ" }, { "CHAY", "Món chay" }, { "BANH", "Món bánh (2 món)" }
    };

    private static readonly string[] DayLabels = { "Thứ 2", "Thứ 3", "Thứ 4", "Thứ 5", "Thứ 6", "Thứ 7" };

    public MenuController(MenuService svc, IWebHostEnvironment env)
    {
        _svc = svc;
        _env = env;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // QUẢN LÝ TUẦN
    // ═══════════════════════════════════════════════════════════════════════════

    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> Manage()
    {
        var weeks = await _svc.GetWeekListAsync();
        ViewBag.BanhFixed = await _svc.GetBanhFixedAsync();
        var allFoods = await _svc.GetFoodListAsync();
        ViewBag.BanhFoods = allFoods.Where(f => f.FOOD_TYPE == "BANH" && f.IS_ACTIVE == 1)
                                     .OrderBy(f => f.FOOD_NAME).ToList();
        return View(weeks);
    }

    // AJAX — đổi món ở 1 trong 2 slot bánh cố định (Menu/Today)
    [HttpPost]
    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> SaveBanhFixed(int slot, int? foodId)
    {
        var (success, msg) = await _svc.SaveBanhFixedAsync(slot, foodId, CurrentUser!.EmpCd);
        return Json(new { success, message = msg });
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> CreateWeek(SaveWeekRequest model)
    {
        model.LOGIN_USER = CurrentUser!.EmpCd;
        model.WEEK_NAME  = $"Tuần {model.FROM_DATE:dd/MM} – {model.TO_DATE:dd/MM/yyyy}";
        var (success, msg) = await _svc.SaveWeekAsync(model);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = msg;
        return RedirectToAction("Manage");
    }

    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> EditWeek(int id)
    {
        var (week, details) = await _svc.GetWeekByIdAsync(id);
        if (week == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy tuần thực đơn";
            return RedirectToAction("Manage");
        }
        var foods = await _svc.GetActiveFoodsAsync();
        return View(new MenuWeekDetailViewModel { Week = week, Details = details, Foods = foods });
    }

    // AJAX — lưu grid thực đơn
    [HttpPost]
    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> SaveGrid([FromBody] SaveDetailRequest model)
    {
        model.LOGIN_USER = CurrentUser!.EmpCd;
        var (success, msg) = await _svc.SaveDetailAsync(model);
        return Json(new { success, message = msg });
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> PublishWeek(int id)
    {
        var (success, msg) = await _svc.PublishAsync(id, CurrentUser!.EmpCd);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = msg;
        return RedirectToAction("Manage");
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> UnpublishWeek(int id)
    {
        var (success, msg) = await _svc.UnpublishAsync(id, CurrentUser!.EmpCd);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = msg;
        return RedirectToAction("Manage");
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> DeleteWeek(int id)
    {
        var (success, msg) = await _svc.DeleteWeekAsync(id);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = msg;
        return RedirectToAction("Manage");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TẢI MẪU EXCEL
    // ═══════════════════════════════════════════════════════════════════════════

    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> DownloadTemplate()
    {
        var foods = await _svc.GetActiveFoodsAsync();

        using var wb = new XLWorkbook();

        // ── Sheet 1: THỰC ĐƠN TUẦN ──────────────────────────────────────────
        var ws = wb.AddWorksheet("THỰC ĐƠN TUẦN");

        // Column widths
        ws.Column(1).Width = 16;
        ws.Column(2).Width = 10;
        ws.Column(3).Width = 30;
        ws.Column(4).Width = 30;
        ws.Column(5).Width = 30;
        ws.Column(6).Width = 32;
        ws.Column(7).Width = 32;

        // Màu
        var colorTitle   = XLColor.FromHtml("#C00000");
        var colorCA1     = XLColor.FromHtml("#1F4E79");
        var colorCA2     = XLColor.FromHtml("#375623");
        var colorCA3     = XLColor.FromHtml("#7B3F00");
        var colorHeader  = XLColor.FromHtml("#D9E1F2");
        var colorDay     = XLColor.FromHtml("#F2F2F2");
        var colorInput   = XLColor.FromHtml("#FFFEF0");
        var colorNote    = XLColor.FromHtml("#FFF2CC");

        // Row 1: tiêu đề
        ws.Range("A1:G1").Merge().Value = "THỰC ĐƠN TUẦN — CÔNG TY TNHH VIỆT NAM SAMHO";
        StyleHeader(ws.Range("A1:G1"), colorTitle, 14, true);
        ws.Row(1).Height = 28;

        // Row 2: ngày
        ws.Range("A2:B2").Merge().Value = "Từ ngày:";
        StyleCell(ws.Cell("A2"), colorDay, 11, true);
        ws.Range("C2:D2").Merge();
        StyleCell(ws.Cell("C2"), colorInput, 11);
        ws.Cell("C2").Style.DateFormat.Format = "DD/MM/YYYY";

        ws.Cell("E2").Value = "Đến ngày:";
        StyleCell(ws.Cell("E2"), colorDay, 11, true);
        ws.Range("F2:G2").Merge();
        StyleCell(ws.Cell("F2"), colorInput, 11);
        ws.Cell("F2").Style.DateFormat.Format = "DD/MM/YYYY";
        ws.Row(2).Height = 22;

        // Row 3: hướng dẫn
        ws.Range("A3:G3").Merge().Value =
            "👉  Ô món: nhập ID số (tra sheet DANH MỤC MÓN) hoặc tên tự do.  " +
            "Nhiều món trong 1 ô (ví dụ 2 món bánh): Alt+Enter hoặc phân cách bởi +, /.  Ngày nghỉ: để trống ô đó.";
        StyleCell(ws.Cell("A3"), colorNote, 9);
        ws.Cell("A3").Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
        ws.Row(3).Height = 15;

        // Vẽ 3 ca
        DrawSection(ws, 5,  "CA 1",           colorCA1, colorHeader, colorDay, colorInput);
        DrawSection(ws, 14, "CA 2 / TĂNG CA", colorCA2, colorHeader, colorDay, colorInput);
        DrawSection(ws, 23, "CA 3",           colorCA3, colorHeader, colorDay, colorInput);

        // ── Sheet 2: DANH MỤC MÓN ──────────────────────────────────────────
        var wsFood = wb.AddWorksheet("DANH MỤC MÓN");
        wsFood.Column(1).Width = 8;
        wsFood.Column(2).Width = 40;
        wsFood.Column(3).Width = 15;

        wsFood.Cell("A1").Value = "ID";
        wsFood.Cell("B1").Value = "TÊN MÓN ĂN";
        wsFood.Cell("C1").Value = "LOẠI";
        wsFood.Range("A1:C1").Style.Font.SetBold(true);
        wsFood.Range("A1:C1").Style.Fill.SetBackgroundColor(colorHeader);

        for (int i = 0; i < foods.Count; i++)
        {
            wsFood.Cell(i + 2, 1).Value = foods[i].ID;
            wsFood.Cell(i + 2, 2).Value = foods[i].FOOD_NAME;
            wsFood.Cell(i + 2, 3).Value = foods[i].FOOD_TYPE;
        }

        // ── Sheet 3: HƯỚNG DẪN ─────────────────────────────────────────────
        var wsGuide = wb.AddWorksheet("HƯỚNG DẪN");
        wsGuide.Column(1).Width = 22;
        wsGuide.Column(2).Width = 55;
        var guide = new[]
        {
            ("Ô màu vàng",        "Nhập dữ liệu vào đây"),
            ("Ngày",              "Định dạng DD/MM/YYYY, ví dụ: 02/06/2026"),
            ("Nhiều món / 1 ô",   "Gõ Alt+Enter để xuống dòng trong 1 ô"),
            ("Ngày nghỉ lễ",      "Để trống toàn bộ hàng của ngày đó"),
            ("CA 1",              "Ca sáng chính"),
            ("CA 2 / TĂNG CA",    "Ca chiều + tăng ca"),
            ("CA 3",              "Ca đêm"),
            ("MÓN MẶN",          "Món chính có thịt/cá"),
            ("MÓN DỰ KIẾN",      "Món thay thế"),
            ("MÓN NHẸ",          "Bún / phở / mì..."),
            ("MÓN CHAY",         "Món chay"),
            ("MÓN BÁNH (2 MÓN)", "Suất bánh gồm 2 món — nhập cả 2 vào cùng 1 ô, cách nhau bằng Alt+Enter hoặc dấu +"),
        };
        for (int i = 0; i < guide.Length; i++)
        {
            wsGuide.Cell(i + 1, 1).Value = guide[i].Item1;
            wsGuide.Cell(i + 1, 1).Style.Font.SetBold(true);
            wsGuide.Cell(i + 1, 2).Value = guide[i].Item2;
        }

        // Xuất file
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Seek(0, SeekOrigin.Begin);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"MauThucDon_{DateTime.Today:yyyyMMdd}.xlsx");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // IMPORT EXCEL
    // ═══════════════════════════════════════════════════════════════════════════

    // Chỉ ĐỌC + validate file Excel, KHÔNG lưu gì cả — trả JSON cho JS xử lý tiếp:
    //   - success=false + errors: lỗi cứng (ngày sai, tên không giống món nào cả) → hiển thị, bắt sửa lại.
    //   - success=true + pending rỗng: không có gì mơ hồ → JS gọi luôn FinalizeWeekImport.
    //   - success=true + pending có dữ liệu: có tên gần giống món đã có → JS hỏi lại rồi mới Finalize.
    [HttpPost]
    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> ImportExcel(IFormFile file)
    {
        var errors  = new List<ImportRowError>();
        var items   = new List<SaveDetailItem>();
        var pending = new List<WeekImportPendingItem>();

        // JSON trả về theo convention lowercase của HR_web (Newtonsoft DefaultContractResolver giữ
        // nguyên tên property C# — phải tự map object ẩn danh, không trả thẳng model PascalCase).
        static object[] MapErrors(IEnumerable<ImportRowError> list)
            => list.Select(e => new { location = e.Location, message = e.Message }).ToArray();

        try
        {
            if (file == null || file.Length == 0)
                return Json(new { success = false, errors = MapErrors(new[] { new ImportRowError { Location = "File", Message = "Vui lòng chọn file Excel (.xlsx)" } }) });

            if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, errors = MapErrors(new[] { new ImportRowError { Location = "File", Message = "Chỉ chấp nhận file .xlsx" } }) });

            using var wb = new XLWorkbook(file.OpenReadStream());
            var ws = wb.Worksheets.First();

            // ── Đọc FROM_DATE / TO_DATE ─────────────────────────────────────
            if (!TryReadDate(ws.Cell("C2"), out var fromDate))
            {
                errors.Add(new() { Location = "Ô C2 (Từ ngày)", Message = "Ngày không hợp lệ. Định dạng: DD/MM/YYYY" });
            }
            if (!TryReadDate(ws.Cell("F2"), out var toDate))
            {
                errors.Add(new() { Location = "Ô F2 (Đến ngày)", Message = "Ngày không hợp lệ. Định dạng: DD/MM/YYYY" });
            }

            if (!errors.Any() && fromDate > toDate)
            {
                errors.Add(new() { Location = "Ngày", Message = "Ngày bắt đầu phải nhỏ hơn ngày kết thúc" });
            }

            // ── Đọc từng ca / ngày / ô món ─────────────────────────────────
            if (!errors.Any())
            {
                var foods = await _svc.GetActiveFoodsAsync();
                foreach (var (shift, dataStart) in Sections)
                {
                    for (int dayOffset = 0; dayOffset < 6; dayOffset++)
                    {
                        int row    = dataStart + dayOffset;
                        int dayNo  = dayOffset + 2;                // 2=T2 … 7=T7
                        string day = DayLabels[dayOffset];

                        foreach (var (col, mealType) in MealCols)
                        {
                            var cellVal = ws.Cell(row, col).GetString().Trim();
                            if (string.IsNullOrWhiteSpace(cellVal)) continue;

                            // BANH: hỗ trợ đọc nhiều món trong 1 ô (Alt+Enter, +, /). Các loại khác
                            // (MAN/NHE/CHAY/DU_KIEN) vẫn 1 món/ô như cũ — không tách theo +, /, dấu
                            // phẩy vì tên món tiếng Việt hay chứa các ký tự này (VD "Cơm, canh, mặn").
                            List<string> lines;
                            if (mealType == "BANH")
                            {
                                lines = cellVal
                                    .Split(new[] { '\n', '\r', '+', '/' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(l => l.Trim())
                                    .Where(l => !string.IsNullOrWhiteSpace(l))
                                    .ToList();
                            }
                            else
                            {
                                var first = cellVal
                                    .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(l => l.Trim())
                                    .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
                                lines = first != null ? new List<string> { first } : new List<string>();
                            }

                            int displayOrder = 1;
                            foreach (var line in lines)
                            {
                                if (int.TryParse(line, out int foodId))
                                {
                                    items.Add(new SaveDetailItem
                                    {
                                        DAY_NO        = dayNo,
                                        SHIFT         = shift,
                                        MEAL_TYPE     = mealType,
                                        FOOD_ID       = foodId,
                                        DISPLAY_ORDER = displayOrder++
                                    });
                                }
                                else
                                {
                                    var matched = foods.FirstOrDefault(f =>
                                        f.FOOD_NAME.Equals(line, StringComparison.OrdinalIgnoreCase));
                                    if (matched != null)
                                    {
                                        items.Add(new SaveDetailItem
                                        {
                                            DAY_NO        = dayNo,
                                            SHIFT         = shift,
                                            MEAL_TYPE     = mealType,
                                            FOOD_ID       = matched.ID,
                                            DISPLAY_ORDER = displayOrder++
                                        });
                                        continue;
                                    }

                                    // Không trùng tuyệt đối — thử tìm tên gần giống trước khi báo lỗi cứng.
                                    var similar = FoodNameMatcher.FindSimilar(line, null, foods);
                                    if (similar != null)
                                    {
                                        pending.Add(new WeekImportPendingItem
                                        {
                                            Shift = shift, DayNo = dayNo, MealType = mealType, DisplayOrder = displayOrder++,
                                            TypedName = line, MatchedId = similar.ID, MatchedName = similar.FOOD_NAME,
                                            MatchedHasImage = similar.IS_IMAGE == "Y"
                                        });
                                        continue;
                                    }

                                    errors.Add(new()
                                    {
                                        Location = $"{shift} — {day} — {MealLabel[mealType]}",
                                        Message  = $"Không tìm thấy \"{line}\" trong danh mục. Nhập ID số hoặc thêm món vào danh mục trước."
                                    });
                                }
                            }
                        }
                    }
                }
            }

            if (errors.Any())
                return Json(new { success = false, errors = MapErrors(errors) });

            if (items.Count == 0 && pending.Count == 0)
                return Json(new { success = false, errors = MapErrors(new[] { new ImportRowError { Location = "File", Message = "File Excel không có dữ liệu món ăn nào" } }) });

            return Json(new
            {
                success = true, fromDate, toDate, items,
                pending = pending.Select(p => new
                {
                    shift = p.Shift, dayNo = p.DayNo, mealType = p.MealType, displayOrder = p.DisplayOrder,
                    typedName = p.TypedName, matchedId = p.MatchedId, matchedName = p.MatchedName, matchedHasImage = p.MatchedHasImage
                })
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, errors = MapErrors(new[] { new ImportRowError { Location = "File", Message = $"Lỗi đọc file: {ex.Message}" } } ) });
        }
    }

    // POST /Menu/FinalizeWeekImport — hoàn tất import thực đơn tuần sau khi JS đã hỏi xong các
    // dòng "tên gần giống" (nếu có). Tạo món mới cho các dòng chọn createNew/createNewCopyImage,
    // gộp với items đã khớp chắc chắn, rồi tạo/tìm tuần + lưu grid y hệt luồng cũ.
    [HttpPost]
    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> FinalizeWeekImport([FromBody] FinalizeWeekImportRequest req)
    {
        try
        {
            var items = new List<SaveDetailItem>(req.Items ?? new());

            foreach (var r in req.Resolved ?? new())
            {
                int foodId;
                if (r.Decision == "useExisting")
                {
                    foodId = r.MatchedId;
                }
                else
                {
                    // Danh mục món dùng chung FOOD_TYPE='MAN' cho cả cột "Món mặn" lẫn "Món dự kiến"
                    // (xem FOOD_TYPE_MAP ở EditWeek.cshtml) — món mới tạo từ cột DU_KIEN cũng phải
                    // lưu type MAN để lần sau hiện đúng trong danh sách chọn của cột đó.
                    var foodType = r.MealType == "DU_KIEN" ? "MAN" : r.MealType;
                    var (ok, createMsg, newId) = await _svc.SaveFoodAsync(new SaveFoodRequest
                    {
                        FOOD_NAME  = r.TypedName,
                        FOOD_TYPE  = foodType,
                        IS_ACTIVE  = 1,
                        LOGIN_USER = CurrentUser!.EmpCd
                    });
                    if (!ok) return Json(new { success = false, message = $"Không tạo được món \"{r.TypedName}\": {createMsg}" });
                    foodId = newId;
                    if (r.Decision == "createNewCopyImage")
                        await CopyFoodImageAsync(r.MatchedId, foodId);
                }

                items.Add(new SaveDetailItem
                {
                    DAY_NO        = r.DayNo,
                    SHIFT         = r.Shift,
                    MEAL_TYPE     = r.MealType,
                    FOOD_ID       = foodId,
                    DISPLAY_ORDER = r.DisplayOrder
                });
            }

            if (items.Count == 0)
                return Json(new { success = false, message = "Không có dữ liệu món ăn nào" });

            // ── Tạo hoặc tìm tuần theo ngày ────────────────────────────────
            var (okWeek, msgWeek) = await _svc.SaveWeekAsync(new SaveWeekRequest
            {
                FROM_DATE  = req.FromDate,
                TO_DATE    = req.ToDate,
                WEEK_NAME  = $"Tuần {req.FromDate:dd/MM} – {req.ToDate:dd/MM/yyyy}",
                LOGIN_USER = CurrentUser!.EmpCd
            });

            var weeks = await _svc.GetWeekListAsync();
            var week  = weeks.FirstOrDefault(w =>
                w.FROM_DATE.Date == req.FromDate.Date && w.TO_DATE.Date == req.ToDate.Date);

            if (week == null)
                return Json(new { success = false, message = okWeek ? "Không thể tìm thấy tuần vừa tạo" : $"Không thể tạo tuần: {msgWeek}" });

            var (success, msg) = await _svc.SaveDetailAsync(new SaveDetailRequest
            {
                WEEK_ID    = week.ID,
                ITEMS      = items,
                LOGIN_USER = CurrentUser!.EmpCd
            });

            return Json(new
            {
                success,
                message = success ? $"Import thành công {items.Count} món cho tuần {week.WEEK_NAME}!" : msg,
                weekId  = week.ID
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Lỗi: {ex.Message}" });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // QUẢN LÝ MÓN ĂN
    // ═══════════════════════════════════════════════════════════════════════════

    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> FoodManage()
    {
        var foods = await _svc.GetFoodListAsync();
        return View(foods);
    }

    // AJAX — lưu món ăn (thêm/sửa)
    [HttpPost]
    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> SaveFood(SaveFoodRequest model, IFormFile? imageFile)
    {
        if (string.IsNullOrWhiteSpace(model.FOOD_NAME))
            return Json(new { success = false, message = "Vui lòng nhập tên món ăn" });

        if (imageFile != null && imageFile.Length > 0)
        {
            var ext = Path.GetExtension(imageFile.FileName).ToLower();
            if (ext != ".jpg" && ext != ".jpeg")
                return Json(new { success = false, message = "Ảnh chỉ chấp nhận JPG" });
            if (imageFile.Length > 15 * 1024 * 1024)
                return Json(new { success = false, message = "Ảnh không được vượt quá 15MB" });
        }

        // Kiểm tra trùng tên trong cùng loại món
        var allFoods = await _svc.GetFoodListAsync();
        var trimmedName = model.FOOD_NAME.Trim();
        var duplicate = allFoods.FirstOrDefault(f =>
            f.FOOD_NAME.Equals(trimmedName, StringComparison.OrdinalIgnoreCase) &&
            f.FOOD_TYPE == model.FOOD_TYPE &&
            f.ID != (model.ID ?? 0));
        if (duplicate != null)
            return Json(new { success = false, message = $"Tên \"{trimmedName}\" đã tồn tại trong loại {model.FOOD_TYPE}." });

        // Món mới, chưa bypass → kiểm tra tên gần giống món đã có (khác tên nhưng thực chất là 1 món,
        // dẫn tới trùng lặp ngầm — món cũ có hình, món mới không có). Hỏi người dùng trước khi tạo.
        bool isNew = model.ID == null || model.ID == 0;
        if (isNew && !model.Bypass)
        {
            var similar = FoodNameMatcher.FindSimilar(trimmedName, model.FOOD_TYPE, allFoods);
            if (similar != null)
            {
                return Json(new
                {
                    success = false,
                    needConfirm = true,
                    candidate = new { id = similar.ID, name = similar.FOOD_NAME, hasImage = similar.IS_IMAGE == "Y" }
                });
            }
        }

        model.LOGIN_USER = CurrentUser!.EmpCd;

        // Lưu vào DB trước để lấy ID (với món mới)
        var (success, msg, savedId) = await _svc.SaveFoodAsync(model);
        if (!success)
            return Json(new { success = false, message = msg });

        int foodId = (model.ID == null || model.ID == 0) ? savedId : model.ID.Value;

        if (isNew && model.CopyImageFromId.HasValue)
            await CopyFoodImageAsync(model.CopyImageFromId.Value, foodId);

        // Lưu ảnh lên network share với tên = {id}.jpg
        string? imgError = null;
        if (imageFile != null && imageFile.Length > 0 && foodId > 0)
        {
            try
            {
                using (new NetworkShareHelper(ImageController.ShareRoot, ImageController.ShareCred))
                {
                    Directory.CreateDirectory(CantinFolder);
                    using var fs = new FileStream(Path.Combine(CantinFolder, $"{foodId}.jpg"), FileMode.Create);
                    await imageFile.CopyToAsync(fs);
                }
            }
            catch (Exception ex)
            {
                imgError = ex.Message;
            }
        }

        if (imgError != null)
            return Json(new { success = true, message = msg + " (Lưu ảnh thất bại: " + imgError + ")" });

        if (imageFile != null && imageFile.Length > 0 && foodId > 0)
            await _svc.SetFoodImageAsync(foodId, "Y");

        return Json(new { success = true, message = msg });
    }

    // AJAX — food IDs on this week's published menu
    [HttpGet]
    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> GetThisWeekFoodIds()
    {
        var ids = await _svc.GetThisWeekFoodIdsAsync();
        return Json(new { success = true, ids });
    }

    // AJAX — food IDs on today's published menu
    [HttpGet]
    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> GetTodayFoodIds()
    {
        var ids = await _svc.GetTodayFoodIdsAsync();
        return Json(new { success = true, ids });
    }

    // AJAX — toggle
    [HttpPost]
    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> ToggleFood(int id)
    {
        var (success, msg) = await _svc.ToggleFoodAsync(id, CurrentUser!.EmpCd);
        return Json(new { success, message = msg });
    }

    // AJAX — xóa
    [HttpPost]
    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> DeleteFood(int id)
    {
        var (success, msg) = await _svc.DeleteFoodAsync(id);
        if (success)
        {
            try
            {
                using (new NetworkShareHelper(ImageController.ShareRoot, ImageController.ShareCred))
                {
                    var imgPath = Path.Combine(CantinFolder, $"{id}.jpg");
                    if (System.IO.File.Exists(imgPath)) System.IO.File.Delete(imgPath);
                }
            }
            catch { /* ảnh không xóa được thì bỏ qua */ }
            await _svc.SetFoodImageAsync(id, "N");
        }
        return Json(new { success, message = msg });
    }

    // Xuất Excel danh mục món ăn
    [HttpGet]
    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> ExportFoodExcel()
    {
        var foods = await _svc.GetFoodListAsync();
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Danh mục món ăn");

        ws.Cell(1, 1).Value = "ID";
        ws.Cell(1, 2).Value = "Tên món";
        ws.Cell(1, 3).Value = "Loại";
        ws.Cell(1, 4).Value = "Trạng thái";
        ws.Cell(1, 5).Value = "Ngày tạo";

        var header = ws.Range(1, 1, 1, 5);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#BDD7EE");

        for (int i = 0; i < foods.Count; i++)
        {
            var f = foods[i];
            var typeLabel = f.FOOD_TYPE switch
            {
                "MAN"     => "Mặn",
                "NHE"     => "Nhẹ",
                "CHAY"    => "Chay",
                "BANH"    => "Bánh",
                "DU_KIEN" => "Dự kiến",
                _         => f.FOOD_TYPE ?? "—"
            };
            ws.Cell(i + 2, 1).Value = f.ID;
            ws.Cell(i + 2, 2).Value = f.FOOD_NAME;
            ws.Cell(i + 2, 3).Value = typeLabel;
            ws.Cell(i + 2, 4).Value = f.IS_ACTIVE == 1 ? "Hiện" : "Ẩn";
            ws.Cell(i + 2, 5).Value = f.INST_DT?.ToString("dd/MM/yyyy") ?? "";
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"DanhMucMonAn_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    // Tải file mẫu import món ăn
    [HttpGet]
    [Authorize(Roles = "Admin,HR,Canteen")]
    public IActionResult DownloadFoodTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Import món ăn");

        // Header
        string[] headers = { "Tên món (*)", "Loại món (*)", "Hiển thị (1/0)" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E79");
            cell.Style.Font.FontColor = XLColor.White;
        }

        // Ghi chú hướng dẫn
        ws.Cell(1, 5).Value = "Loại món: MAN = Mặn | NHE = Nhẹ | CHAY = Chay | BANH = Bánh";
        ws.Cell(1, 5).Style.Font.Italic = true;
        ws.Cell(1, 5).Style.Font.FontColor = XLColor.Gray;

        // Dòng ví dụ
        ws.Cell(2, 1).Value = "Cá kho rau răm";
        ws.Cell(2, 2).Value = "MAN";
        ws.Cell(2, 3).Value = 1;

        ws.Cell(3, 1).Value = "Rau muống xào tỏi";
        ws.Cell(3, 2).Value = "CHAY";
        ws.Cell(3, 3).Value = 1;

        // Dropdown validation cho cột Loại món (B2:B500)
        var typeRange = ws.Range("B2:B500");
        typeRange.CreateDataValidation().List("\"MAN,NHE,CHAY,BANH\"", true);

        ws.Columns(1, 4).AdjustToContents();
        ws.Column(5).Width = 45;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "MauImportMonAn.xlsx");
    }

    // Import món ăn từ Excel — trả JSON: dòng nào tên gần giống món có sẵn thì đưa vào
    // "pending" hỏi lại thay vì tự thêm luôn (tránh trùng lặp ngầm, món mới không có hình).
    [HttpPost]
    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> ImportFoodExcel(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Chưa chọn file." });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return Json(new { success = false, message = "Chỉ chấp nhận file .xlsx." });

        var errors  = new List<string>();
        var pending = new List<FoodImportPendingItem>();
        int inserted = 0;

        // Tải danh sách hiện có 1 lần để check trùng trong cùng loại — thêm dần món mới
        // insert được ngay trong lúc chạy để các dòng sau trong cùng file cũng so được với nó.
        var existingFoods = await _svc.GetFoodListAsync();
        // Track tên đã import trong file này (tên+loại) để tránh trùng nội bộ file
        var importedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var stream = file.OpenReadStream();
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (int row = 2; row <= lastRow; row++)
        {
            var name    = ws.Cell(row, 1).GetValue<string>()?.Trim();
            var rawType = ws.Cell(row, 2).GetValue<string>()?.Trim().ToUpper();
            var active  = ws.Cell(row, 3).GetValue<string>()?.Trim();

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(rawType)) continue;

            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add($"Dòng {row}: Thiếu tên món.");
                continue;
            }

            var validTypes = new[] { "MAN", "NHE", "CHAY", "BANH" };
            if (string.IsNullOrWhiteSpace(rawType) || !validTypes.Contains(rawType))
            {
                errors.Add($"Dòng {row} ({name}): Loại món không hợp lệ — dùng MAN / NHE / CHAY / BANH.");
                continue;
            }

            // Kiểm tra trùng tên trong cùng loại (trong DB)
            var dupInDb = existingFoods.Any(f =>
                f.FOOD_NAME.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                f.FOOD_TYPE == rawType);
            if (dupInDb)
            {
                errors.Add($"Dòng {row} ({name}): Tên đã tồn tại trong loại {rawType}.");
                continue;
            }

            // Kiểm tra trùng nội bộ trong file Excel
            var key = $"{rawType}|{name}";
            if (!importedKeys.Add(key))
            {
                errors.Add($"Dòng {row} ({name}): Trùng tên trong file Excel (loại {rawType}).");
                continue;
            }

            int isActive = active == "0" ? 0 : 1;

            // Tên gần giống món đã có (khác tên, cùng loại) → không tự thêm, đưa vào hàng chờ hỏi lại.
            var similar = FoodNameMatcher.FindSimilar(name, rawType, existingFoods);
            if (similar != null)
            {
                pending.Add(new FoodImportPendingItem
                {
                    Name = name, Type = rawType, Active = isActive,
                    MatchedId = similar.ID, MatchedName = similar.FOOD_NAME, MatchedHasImage = similar.IS_IMAGE == "Y"
                });
                continue;
            }

            var req = new SaveFoodRequest
            {
                FOOD_NAME  = name,
                FOOD_TYPE  = rawType,
                IS_ACTIVE  = isActive,
                LOGIN_USER = CurrentUser!.EmpCd
            };

            var (ok, msg, newId) = await _svc.SaveFoodAsync(req);
            if (ok)
            {
                inserted++;
                existingFoods.Add(new MenuFoodModel { ID = newId, FOOD_NAME = name, FOOD_TYPE = rawType, IS_ACTIVE = isActive, IS_IMAGE = "N" });
            }
            else errors.Add($"Dòng {row} ({name}): {msg}");
        }

        return Json(new
        {
            success = true, inserted, errors,
            pending = pending.Select(p => new
            {
                name = p.Name, type = p.Type, active = p.Active,
                matchedId = p.MatchedId, matchedName = p.MatchedName, matchedHasImage = p.MatchedHasImage
            })
        });
    }

    // POST /Menu/ResolveFoodImportReview — người dùng đã chọn xong xử lý các dòng "tên gần giống"
    // sau khi Import Excel danh mục món ăn (xem ImportFoodExcel).
    [HttpPost]
    [Authorize(Roles = "Admin,HR,Canteen")]
    public async Task<IActionResult> ResolveFoodImportReview([FromBody] List<FoodImportResolveItem> items)
    {
        int inserted = 0;
        var errors = new List<string>();
        foreach (var it in items ?? new())
        {
            if (it.Decision == "useExisting") continue;

            var req = new SaveFoodRequest
            {
                FOOD_NAME  = it.Name,
                FOOD_TYPE  = it.Type,
                IS_ACTIVE  = it.Active,
                LOGIN_USER = CurrentUser!.EmpCd
            };
            var (ok, msg, newId) = await _svc.SaveFoodAsync(req);
            if (!ok) { errors.Add($"{it.Name}: {msg}"); continue; }

            inserted++;
            if (it.Decision == "createNewCopyImage")
                await CopyFoodImageAsync(it.MatchedId, newId);
        }
        return Json(new { success = true, inserted, errors });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CÔNG NHÂN XEM
    // ═══════════════════════════════════════════════════════════════════════════

    [AllowAnonymous]
    public async Task<IActionResult> Today()
    {
        var data = await _svc.GetTodayMenuAsync();
        var empCd = CurrentUser?.EmpCd;
        if (!string.IsNullOrEmpty(empCd))
            ViewBag.UserMeals = await _svc.GetUserTodayMealAsync(empCd);
        // Suất 2 món Bánh cố định — không theo lưới thực đơn tuần, HR đổi bất kỳ lúc nào (Menu/Manage)
        ViewBag.BanhFixed = await _svc.GetBanhFixedAsync();
        return View(data);
    }

    public async Task<IActionResult> ChangeMeal()
    {
        var empCd = CurrentUser!.EmpCd;
        var meals = await _svc.GetUserTodayMealAsync(empCd);
        var first = meals.FirstOrDefault();
        var vm = new HR_web.Models.Menu.ChangeMealViewModel
        {
            EmpCd           = empCd,
            FullName        = CurrentUser.FullName,
            CurrentFoodType = first?.FOOD_TYPE,
            CurrentFoodName = first?.FOOD_NAME,
        };
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> GetMealForDate(string date, string? typeMeal)
    {
        var empCd    = CurrentUser!.EmpCd;
        var dateStr  = date?.Replace("-", "") ?? DateTime.Today.ToString("yyyyMMdd");
        var foodType = await _svc.GetUserMealByDateAsync(empCd, dateStr, typeMeal ?? "LUNCH");
        return Json(new { success = true, foodType });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> ChangeMealSubmit([FromBody] HR_web.Models.Menu.ChangeMealRequest req)
    {
        req.LoginUser = CurrentUser!.EmpCd;
        var (ok, msg) = await _svc.ChangeMealAsync(req);
        return Json(new { success = ok, message = msg });
    }

    [AllowAnonymous]
    public async Task<IActionResult> ThisWeek()
    {
        var nextWeekData = await _svc.GetNextWeekMenuAsync();
        ViewBag.HasNextWeek = nextWeekData.Any();

        // Chủ nhật: tuần T2–T7 đã kết thúc → tự động hiển thị thực đơn tuần sau nếu căn tin đã đăng
        if (DateTime.Today.DayOfWeek == DayOfWeek.Sunday)
        {
            if (nextWeekData.Any())
            {
                ViewBag.IsNextWeek   = true;
                ViewBag.AutoNextWeek = true;
                ViewBag.WeekMonday   = DateTime.Today.AddDays(1);
                if (CurrentUser != null)
                    ViewBag.UserWeekMeals = await _svc.GetUserWeekMealAsync(CurrentUser.EmpCd);
                return View(nextWeekData);
            }
            ViewBag.NoNextWeekMsg = true;
        }

        var data = await _svc.GetThisWeekMenuAsync();
        ViewBag.WeekMonday = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7));
        if (CurrentUser != null)
            ViewBag.UserWeekMeals = await _svc.GetUserWeekMealAsync(CurrentUser.EmpCd);
        return View(data);
    }

    public async Task<IActionResult> NextWeek()
    {
        var data = await _svc.GetNextWeekMenuAsync();
        if (!data.Any()) return RedirectToAction("ThisWeek");
        ViewBag.IsNextWeek   = true;
        ViewBag.WeekMonday   = DateTime.Today.AddDays(7 - ((int)DateTime.Today.DayOfWeek + 6) % 7);
        if (CurrentUser != null)
            ViewBag.UserWeekMeals = await _svc.GetUserWeekMealAsync(CurrentUser.EmpCd);
        return View("ThisWeek", data);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    // Copy file ảnh {fromId}.jpg -> {toId}.jpg trên network share (popup "món tên giống nhau"
    // chọn "Tạo mới + copy hình"). Bỏ qua im lặng nếu món nguồn không có ảnh — không phải lỗi.
    private async Task<bool> CopyFoodImageAsync(int fromId, int toId)
    {
        try
        {
            using (new NetworkShareHelper(ImageController.ShareRoot, ImageController.ShareCred))
            {
                var srcPath = Path.Combine(CantinFolder, $"{fromId}.jpg");
                if (!System.IO.File.Exists(srcPath)) return false;
                Directory.CreateDirectory(CantinFolder);
                System.IO.File.Copy(srcPath, Path.Combine(CantinFolder, $"{toId}.jpg"), overwrite: true);
            }
            await _svc.SetFoodImageAsync(toId, "Y");
            return true;
        }
        catch { return false; }
    }

    private static bool TryReadDate(IXLCell cell, out DateTime result)
    {
        result = default;
        try
        {
            if (cell.DataType == XLDataType.DateTime)
            {
                result = cell.GetDateTime();
                return true;
            }
            var s = cell.GetString().Trim();
            return DateTime.TryParseExact(s,
                new[] { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy", "dd-MM-yyyy" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }
        catch { return false; }
    }

    private static void DrawSection(IXLWorksheet ws, int startRow, string label,
        XLColor caColor, XLColor headerColor, XLColor dayColor, XLColor inputColor)
    {
        // Ca header
        ws.Range(startRow, 1, startRow, 7).Merge().Value = label;
        StyleHeader(ws.Range(startRow, 1, startRow, 7), caColor, 12, true);
        ws.Row(startRow).Height = 22;

        // Column headers
        int hr = startRow + 1;
        ws.Cell(hr, 1).Value = "CA";
        ws.Cell(hr, 2).Value = "THỨ";
        ws.Cell(hr, 3).Value = "MÓN MẶN";
        ws.Cell(hr, 4).Value = "MÓN DỰ KIẾN";
        ws.Cell(hr, 5).Value = "MÓN NHẸ";
        ws.Cell(hr, 6).Value = "MÓN CHAY";
        ws.Cell(hr, 7).Value = "MÓN BÁNH (2 MÓN)";
        ws.Range(hr, 1, hr, 7).Style
            .Fill.SetBackgroundColor(headerColor)
            .Font.SetBold(true);
        ws.Row(hr).Height = 26;

        // Data rows T2-T7
        string[] days = { "Thứ 2", "Thứ 3", "Thứ 4", "Thứ 5", "Thứ 6", "Thứ 7" };
        for (int i = 0; i < 6; i++)
        {
            int r = hr + 1 + i;
            ws.Row(r).Height = 42;
            ws.Cell(r, 2).Value = days[i];
            ws.Cell(r, 2).Style.Fill.SetBackgroundColor(dayColor).Font.SetBold(true);
            ws.Cell(r, 2).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                                         .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            for (int c = 3; c <= 7; c++)
            {
                ws.Cell(r, c).Style
                    .Fill.SetBackgroundColor(inputColor)
                    .Alignment.SetWrapText(true)
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Top);
            }
        }

        // Merge cột A cho 6 ngày
        ws.Range(hr + 1, 1, hr + 6, 1).Merge().Value = label;
        ws.Range(hr + 1, 1, hr + 6, 1).Style
            .Fill.SetBackgroundColor(caColor)
            .Font.SetBold(true).Font.SetFontColor(XLColor.White)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                       .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                       .Alignment.SetWrapText(true);
    }

    private static void StyleHeader(IXLRange range, XLColor bg, int size, bool bold)
    {
        range.Style
            .Fill.SetBackgroundColor(bg)
            .Font.SetBold(bold).Font.SetFontColor(XLColor.White).Font.SetFontSize(size)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                       .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
    }

    private static void StyleCell(IXLCell cell, XLColor bg, int size, bool bold = false)
    {
        cell.Style
            .Fill.SetBackgroundColor(bg)
            .Font.SetBold(bold).Font.SetFontSize(size)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
    }
}

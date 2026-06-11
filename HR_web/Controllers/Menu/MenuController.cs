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

    private const string ShareRoot   = @"\\192.168.1.5\vserp_picture";
    private const string CantinFolder = @"\\192.168.1.5\vserp_picture\MY_SAMHO_CANTIN";
    private static readonly NetworkCredential ShareCred =
        new NetworkCredential("localfileserver", "!samh0!!");

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
        { 3, "MAN" }, { 4, "DU_KIEN" }, { 5, "NHE" }, { 6, "CHAY" }
    };

    private static readonly Dictionary<string, string> MealLabel = new()
    {
        { "MAN", "Món mặn" }, { "DU_KIEN", "Món dự kiến" },
        { "NHE", "Món nhẹ" }, { "CHAY", "Món chay" }
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

    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> Manage()
    {
        var weeks = await _svc.GetWeekListAsync();
        return View(weeks);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> CreateWeek(SaveWeekRequest model)
    {
        model.LOGIN_USER = CurrentUser!.EmpCd;
        model.WEEK_NAME  = $"Tuần {model.FROM_DATE:dd/MM} – {model.TO_DATE:dd/MM/yyyy}";
        var (success, msg) = await _svc.SaveWeekAsync(model);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = msg;
        return RedirectToAction("Manage");
    }

    [Authorize(Roles = "Admin,HR")]
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
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> SaveGrid([FromBody] SaveDetailRequest model)
    {
        model.LOGIN_USER = CurrentUser!.EmpCd;
        var (success, msg) = await _svc.SaveDetailAsync(model);
        return Json(new { success, message = msg });
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> PublishWeek(int id)
    {
        var (success, msg) = await _svc.PublishAsync(id, CurrentUser!.EmpCd);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = msg;
        return RedirectToAction("Manage");
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> UnpublishWeek(int id)
    {
        var (success, msg) = await _svc.UnpublishAsync(id, CurrentUser!.EmpCd);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = msg;
        return RedirectToAction("Manage");
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> DeleteWeek(int id)
    {
        var (success, msg) = await _svc.DeleteWeekAsync(id);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = msg;
        return RedirectToAction("Manage");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TẢI MẪU EXCEL
    // ═══════════════════════════════════════════════════════════════════════════

    [Authorize(Roles = "Admin,HR")]
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
        ws.Range("A1:F1").Merge().Value = "THỰC ĐƠN TUẦN — CÔNG TY TNHH VIỆT NAM SAMHO";
        StyleHeader(ws.Range("A1:F1"), colorTitle, 14, true);
        ws.Row(1).Height = 28;

        // Row 2: ngày
        ws.Range("A2:B2").Merge().Value = "Từ ngày:";
        StyleCell(ws.Cell("A2"), colorDay, 11, true);
        ws.Range("C2:D2").Merge();
        StyleCell(ws.Cell("C2"), colorInput, 11);
        ws.Cell("C2").Style.DateFormat.Format = "DD/MM/YYYY";
        ws.Cell("C2").Comment.AddText("Nhập ngày bắt đầu tuần (DD/MM/YYYY)");

        ws.Cell("E2").Value = "Đến ngày:";
        StyleCell(ws.Cell("E2"), colorDay, 11, true);
        StyleCell(ws.Cell("F2"), colorInput, 11);
        ws.Cell("F2").Style.DateFormat.Format = "DD/MM/YYYY";
        ws.Cell("F2").Comment.AddText("Nhập ngày kết thúc tuần (DD/MM/YYYY)");
        ws.Row(2).Height = 22;

        // Row 3: hướng dẫn
        ws.Range("A3:F3").Merge().Value =
            "👉  Nhập ngày vào ô màu vàng.  Nhiều món trong 1 ô: xuống dòng bằng Alt+Enter.  " +
            "Ngày nghỉ lễ: để trống toàn bộ hàng đó.  Xem sheet [DANH MỤC MÓN] để tra mã.";
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

    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> ImportExcel(IFormFile file)
    {
        var errors = new List<ImportRowError>();
        var items  = new List<SaveDetailItem>();

        try
        {
            if (file == null || file.Length == 0)
            {
                TempData["ImportError"] = "Vui lòng chọn file Excel (.xlsx)";
                return RedirectToAction("Manage");
            }

            if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ImportError"] = "Chỉ chấp nhận file .xlsx";
                return RedirectToAction("Manage");
            }

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

                            // Mỗi dòng trong ô = 1 món riêng
                            var lines = cellVal
                                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(l => l.Trim())
                                .Where(l => !string.IsNullOrWhiteSpace(l))
                                .ToList();

                            for (int i = 0; i < lines.Count; i++)
                            {
                                if (lines[i].Length > 500)
                                {
                                    errors.Add(new()
                                    {
                                        Location = $"{shift} — {day} — {MealLabel[mealType]} (dòng {i + 1})",
                                        Message  = "Tên món vượt quá 500 ký tự"
                                    });
                                    continue;
                                }

                                items.Add(new SaveDetailItem
                                {
                                    DAY_NO       = dayNo,
                                    SHIFT        = shift,
                                    MEAL_TYPE    = mealType,
                                    FOOD_TEXT    = lines[i],
                                    DISPLAY_ORDER = i + 1
                                });
                            }
                        }
                    }
                }
            }

            // ── Có lỗi → dừng, hiển thị cho HR sửa ────────────────────────
            if (errors.Any())
            {
                TempData["ImportErrors"] = JsonConvert.SerializeObject(errors);
                return RedirectToAction("Manage");
            }

            if (items.Count == 0)
            {
                TempData["ImportError"] = "File Excel không có dữ liệu món ăn nào";
                return RedirectToAction("Manage");
            }

            // ── Tạo hoặc tìm tuần theo ngày ────────────────────────────────
            var (okWeek, msgWeek) = await _svc.SaveWeekAsync(new SaveWeekRequest
            {
                FROM_DATE  = fromDate,
                TO_DATE    = toDate,
                WEEK_NAME  = $"Tuần {fromDate:dd/MM} – {toDate:dd/MM/yyyy}",
                LOGIN_USER = CurrentUser!.EmpCd
            });

            // Lấy ID tuần vừa tạo/tồn tại
            var weeks = await _svc.GetWeekListAsync();
            var week  = weeks.FirstOrDefault(w =>
                w.FROM_DATE.Date == fromDate.Date && w.TO_DATE.Date == toDate.Date);

            if (week == null)
            {
                TempData["ErrorMessage"] = $"Không thể tạo tuần: {msgWeek}";
                return RedirectToAction("Manage");
            }

            // Thử match tên món với food master để gán FOOD_ID (tuỳ chọn)
            var foodMaster = await _svc.GetActiveFoodsAsync();
            var foodByName = foodMaster
                .GroupBy(f => f.FOOD_NAME.ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.First().ID);

            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.FOOD_TEXT) &&
                    foodByName.TryGetValue(item.FOOD_TEXT.ToUpperInvariant(), out int fid))
                {
                    item.FOOD_ID = fid;
                }
            }

            // ── Lưu vào DB ──────────────────────────────────────────────────
            var (success, msg) = await _svc.SaveDetailAsync(new SaveDetailRequest
            {
                WEEK_ID    = week.ID,
                ITEMS      = items,
                LOGIN_USER = CurrentUser!.EmpCd
            });

            TempData[success ? "SuccessMessage" : "ErrorMessage"] = success
                ? $"Import thành công {items.Count} món cho tuần {week.WEEK_NAME}!"
                : msg;

            return success
                ? RedirectToAction("EditWeek", new { id = week.ID })
                : RedirectToAction("Manage");
        }
        catch (Exception ex)
        {
            TempData["ImportError"] = $"Lỗi đọc file: {ex.Message}";
            return RedirectToAction("Manage");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // QUẢN LÝ MÓN ĂN
    // ═══════════════════════════════════════════════════════════════════════════

    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> FoodManage()
    {
        var foods = await _svc.GetFoodListAsync();
        return View(foods);
    }

    // AJAX — lưu món ăn (thêm/sửa)
    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> SaveFood(SaveFoodRequest model, IFormFile? imageFile)
    {
        if (string.IsNullOrWhiteSpace(model.FOOD_NAME))
            return Json(new { success = false, message = "Vui lòng nhập tên món ăn" });

        model.LOGIN_USER = CurrentUser!.EmpCd;

        string? validatedExt = null;
        if (imageFile != null && imageFile.Length > 0)
        {
            validatedExt = Path.GetExtension(imageFile.FileName).ToLower();
            if (validatedExt != ".jpg" && validatedExt != ".jpeg" && validatedExt != ".png" && validatedExt != ".webp")
                return Json(new { success = false, message = "Ảnh chỉ chấp nhận JPG, PNG, WEBP" });
            if (imageFile.Length > 5 * 1024 * 1024)
                return Json(new { success = false, message = "Ảnh không được vượt quá 5MB" });
        }

        bool isNew = model.ID == null || model.ID == 0;

        if (validatedExt != null && !isNew)
        {
            // EDIT: tên file = {id}{ext}, xóa file cũ nếu tên khác
            var newFileName = $"{model.ID}{validatedExt}";
            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                Directory.CreateDirectory(CantinFolder);
                // Xóa file cũ nếu đổi sang tên/ext khác
                if (!string.IsNullOrEmpty(model.IMAGE_PATH) && model.IMAGE_PATH != newFileName)
                {
                    var oldPath = Path.Combine(CantinFolder, model.IMAGE_PATH);
                    if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                }
                using var fs = new FileStream(Path.Combine(CantinFolder, newFileName), FileMode.Create);
                await imageFile!.CopyToAsync(fs);
            }
            model.IMAGE_PATH = newFileName;
        }

        // Lưu vào DB (với IMAGE_PATH nếu edit, không có image_path nếu new+ảnh)
        var imagePathForNew = validatedExt != null && isNew ? null : model.IMAGE_PATH;
        var reqFirst = isNew && validatedExt != null
            ? new SaveFoodRequest { FOOD_NAME = model.FOOD_NAME, FOOD_TYPE = model.FOOD_TYPE, IS_ACTIVE = model.IS_ACTIVE, LOGIN_USER = model.LOGIN_USER }
            : model;

        var (success, msg, newId) = await _svc.SaveFoodAsync(reqFirst);
        if (!success)
            return Json(new { success = false, message = msg });

        if (validatedExt != null && isNew && newId > 0)
        {
            // NEW + ảnh: upload với tên = {newId}{ext}, rồi cập nhật IMAGE_PATH
            var newFileName = $"{newId}{validatedExt}";
            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                Directory.CreateDirectory(CantinFolder);
                using var fs = new FileStream(Path.Combine(CantinFolder, newFileName), FileMode.Create);
                await imageFile!.CopyToAsync(fs);
            }
            var updateReq = new SaveFoodRequest
            {
                ID         = newId,
                FOOD_NAME  = model.FOOD_NAME,
                FOOD_TYPE  = model.FOOD_TYPE,
                IS_ACTIVE  = model.IS_ACTIVE,
                IMAGE_PATH = newFileName,
                LOGIN_USER = model.LOGIN_USER
            };
            var (ok2, msg2, _) = await _svc.SaveFoodAsync(updateReq);
            if (!ok2) return Json(new { success = false, message = msg2 });
            return Json(new { success = true, message = "Thêm món ăn thành công", imagePath = newFileName });
        }

        return Json(new { success, message = msg, imagePath = model.IMAGE_PATH });
    }

    // AJAX — toggle
    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> ToggleFood(int id)
    {
        var (success, msg) = await _svc.ToggleFoodAsync(id, CurrentUser!.EmpCd);
        return Json(new { success, message = msg });
    }

    // AJAX — xóa
    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> DeleteFood(int id)
    {
        var (success, msg) = await _svc.DeleteFoodAsync(id);
        return Json(new { success, message = msg });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SERVE ẢNH TỪ NETWORK SHARE
    // ═══════════════════════════════════════════════════════════════════════════

    [AllowAnonymous]
    [ResponseCache(Duration = 86400)] // cache 1 ngày
    public IActionResult GetImage(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains(".."))
            return NotFound();

        var filePath = Path.Combine(CantinFolder, fileName);
        try
        {
            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                if (!System.IO.File.Exists(filePath))
                    return NotFound();

                var bytes = System.IO.File.ReadAllBytes(filePath);
                var ext   = Path.GetExtension(fileName).ToLower();
                var mime  = ext switch
                {
                    ".png"  => "image/png",
                    ".webp" => "image/webp",
                    _       => "image/jpeg"
                };
                return File(bytes, mime);
            }
        }
        catch { return NotFound(); }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CÔNG NHÂN XEM
    // ═══════════════════════════════════════════════════════════════════════════

    [AllowAnonymous]
    public async Task<IActionResult> Today()
    {
        var data = await _svc.GetTodayMenuAsync();
        return View(data);
    }

    [AllowAnonymous]
    public async Task<IActionResult> ThisWeek()
    {
        var data = await _svc.GetThisWeekMenuAsync();
        return View(data);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════

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
        ws.Range(startRow, 1, startRow, 6).Merge().Value = label;
        StyleHeader(ws.Range(startRow, 1, startRow, 6), caColor, 12, true);
        ws.Row(startRow).Height = 22;

        // Column headers
        int hr = startRow + 1;
        ws.Cell(hr, 1).Value = "CA";
        ws.Cell(hr, 2).Value = "THỨ";
        ws.Cell(hr, 3).Value = "MÓN MẶN";
        ws.Cell(hr, 4).Value = "MÓN DỰ KIẾN";
        ws.Cell(hr, 5).Value = "MÓN NHẸ";
        ws.Cell(hr, 6).Value = "MÓN CHAY";
        ws.Range(hr, 1, hr, 6).Style
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
                                         .SetVertical(XLAlignmentVerticalValues.Center);
            for (int c = 3; c <= 6; c++)
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
            .Font.SetBold(true).SetFontColor(XLColor.White)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                       .SetVertical(XLAlignmentVerticalValues.Center)
                       .SetWrapText(true);
    }

    private static void StyleHeader(IXLRange range, XLColor bg, int size, bool bold)
    {
        range.Style
            .Fill.SetBackgroundColor(bg)
            .Font.SetBold(bold).SetFontColor(XLColor.White).SetFontSize(size)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                       .SetVertical(XLAlignmentVerticalValues.Center);
    }

    private static void StyleCell(IXLCell cell, XLColor bg, int size, bool bold = false)
    {
        cell.Style
            .Fill.SetBackgroundColor(bg)
            .Font.SetBold(bold).SetFontSize(size)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
    }
}

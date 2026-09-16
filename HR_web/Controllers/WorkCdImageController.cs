using ClosedXML.Excel;
using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers;

// Trang quản lý hình minh hoạ theo mã công việc (ECM100.INTEREST, VD Y80/QUÉT KEO) - chỉ HR/Admin.
[Authorize(Roles = "Admin,HR")]
public class WorkCdImageController : BaseController
{
    private readonly DirectoryService _service;

    public WorkCdImageController(DirectoryService service)
    {
        _service = service;
    }

    // GET: /WorkCdImage/Index?search=&missingOnly=&page=
    public async Task<IActionResult> Index(string? search, bool missingOnly = false, int page = 1)
    {
        const int pageSize = 60;

        var result = await _service.GetInterestListAsync(search, page, pageSize);

        var existing = ImageController.CheckWorkCdImagesExist(result.Items.Select(i => i.ImageFileName));
        foreach (var item in result.Items)
            item.HasImage = existing.Contains(item.ImageFileName);

        if (missingOnly)
            result.Items = result.Items.Where(i => !i.HasImage).ToList();

        ViewBag.Search = search;
        ViewBag.MissingOnly = missingOnly;
        ViewBag.TotalPages = (int)Math.Ceiling(result.Total / (double)pageSize);

        return View(result);
    }

    // GET: /WorkCdImage/ExportExcel — xuất Excel 2 sheet (Đã có ảnh / Chưa có ảnh).
    // LƯU Ý: CheckWorkCdImagesExist chỉ mount được network share khi chạy trên Windows
    // (NetworkShareHelper no-op trên máy khác Windows) — phải chạy action này từ server
    // production thật (hoặc máy Windows có quyền vào \\192.168.1.5\vserp_picture) mới ra
    // kết quả đúng, chạy từ máy dev khác Windows sẽ báo TẤT CẢ đều "chưa có ảnh".
    [HttpGet]
    public async Task<IActionResult> ExportExcel()
    {
        var all = new List<HR_web.Models.Directory.WorkCdItemModel>();
        int page = 1;
        const int pageSize = 200;
        while (true)
        {
            var result = await _service.GetInterestListAsync(null, page, pageSize);
            if (result.Items.Count == 0) break;
            all.AddRange(result.Items);
            if (all.Count >= result.Total) break;
            page++;
        }

        var existing  = ImageController.CheckWorkCdImagesExist(all.Select(i => i.ImageFileName));
        var hasImage  = all.Where(i => existing.Contains(i.ImageFileName)).ToList();
        var missing   = all.Where(i => !existing.Contains(i.ImageFileName)).ToList();

        using var wb = new XLWorkbook();
        void FillSheet(IXLWorksheet ws, List<HR_web.Models.Directory.WorkCdItemModel> items)
        {
            ws.Cell(1, 1).Value = "Mã công việc";
            ws.Cell(1, 2).Value = "Tên công việc";
            for (int i = 0; i < items.Count; i++)
            {
                ws.Cell(i + 2, 1).Value = items[i].InterestCd;
                ws.Cell(i + 2, 2).Value = items[i].InterestName ?? "";
            }
            ws.Columns().AdjustToContents();
        }
        FillSheet(wb.Worksheets.Add("DaCoAnh"), hasImage);
        FillSheet(wb.Worksheets.Add("ChuaCoAnh"), missing);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"WorkCd_DaCo_ChuaCo_Anh_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    // POST: /WorkCdImage/UploadBulk
    [HttpPost]
    [ValidateAntiForgeryToken]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadBulk(List<IFormFile> files, string? search, bool missingOnly = false, int page = 1)
    {
        var (savedCount, matched, skipped) = await ImageController.SaveWorkCdImagesAsync(files);

        TempData["UploadSavedCount"] = savedCount;
        TempData["UploadSkipped"] = System.Text.Json.JsonSerializer.Serialize(skipped);

        return RedirectToAction("Index", new { search, missingOnly, page });
    }
}

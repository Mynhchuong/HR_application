using HR_web.API.Service;
using HR_web.Helpers;
using HR_web.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace HR_web.Controllers;


[Authorize]
public class ImageController : BaseController
{
    private readonly AccountService _accountService;

    // ── Network share config (dùng chung toàn app) ──────────────────────────
    internal const string ShareRoot = @"\\192.168.1.5\vserp_picture";
    internal static readonly System.Net.NetworkCredential ShareCred =
        new("localfileserver", "!samh0!!");

    // ── Thư mục từng loại ảnh ───────────────────────────────────────────────
    private const string EmployeeFolder = ShareRoot + @"\VSHRMS";
    private const string SignatureFolder = ShareRoot + @"\WRK_SIGN";
    private const string CantinFolder   = ShareRoot + @"\MY_SAMHO_CANTIN";
    private const string PolicyFolder   = ShareRoot + @"\POLICY";
    private const string BulletinFolder = ShareRoot + @"\BULLETIN\IMG";
    private const string HomeBannerFolder = ShareRoot + @"\MY_SAMHO_HOME";
    internal const string WorkCdFolder = ShareRoot + @"\workcd";

    private static readonly string[] ImageExts    = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private const long               ImageMaxBytes = 10L * 1024 * 1024; // 10 MB

    public ImageController(AccountService accountService)
    {
        _accountService = accountService;
    }

    // ── Helper chung: đọc file từ network share và trả về ảnh ───────────────
    private IActionResult ServeNetworkImage(string filePath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains(".."))
            return NotFound();
        try
        {
            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                if (!System.IO.File.Exists(filePath)) return NotFound();
                var bytes = System.IO.File.ReadAllBytes(filePath);
                var mime  = Path.GetExtension(fileName).ToLower() switch
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

    // ── Ảnh nhân viên ───────────────────────────────────────────────────────
    // AllowAnonymous: trang Directory (xem thông tin đồng nghiệp) không cần login vẫn phải load được avatar.
    [HttpGet, AllowAnonymous]
    public IActionResult GetEmployeeImage(string empCd)
    {
        if (string.IsNullOrWhiteSpace(empCd)) return BadRequest();
        var fileName = empCd + ".jpg";
        return ServeNetworkImage(Path.Combine(EmployeeFolder, fileName), fileName);
    }

    // ── Chữ ký ──────────────────────────────────────────────────────────────
    [HttpGet]
    public IActionResult GetSignature(string empCd)
    {
        if (string.IsNullOrWhiteSpace(empCd)) return BadRequest();
        var fileName = empCd + ".jpg";
        return ServeNetworkImage(Path.Combine(SignatureFolder, fileName), fileName);
    }

    // ── Ảnh món ăn (cantin) ─────────────────────────────────────────────────
    [HttpGet, AllowAnonymous]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetFoodImage(string fileName)
        => ServeNetworkImage(Path.Combine(CantinFolder, fileName), fileName);

    // ── Ảnh Policy ──────────────────────────────────────────────────────────
    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    [IgnoreAntiforgeryToken]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadPolicyImage(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Chưa chọn file!" });

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (!ImageExts.Contains(ext))
            return Json(new { success = false, message = "Chỉ chấp nhận JPG, PNG, WebP, GIF!" });

        if (file.Length > ImageMaxBytes)
            return Json(new { success = false, message = "File không được vượt quá 10 MB!" });

        var fileName = Guid.NewGuid().ToString("N") + ext;
        var savePath = Path.Combine(PolicyFolder, fileName);

        try
        {
            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                Directory.CreateDirectory(PolicyFolder);
                await using var stream = new FileStream(savePath, FileMode.Create);
                await file.CopyToAsync(stream);
            }
            return Json(new { success = true, url = Url.Action("GetPolicyImage", "Image", new { fileName }) });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Lỗi lưu file: {ex.Message}" });
        }
    }

    [HttpGet, AllowAnonymous]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetPolicyImage(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
            return NotFound();
        return ServeNetworkImage(Path.Combine(PolicyFolder, fileName), fileName);
    }

    // ── Ảnh Bulletin (cover + ảnh trong nội dung TipTap) ────────────────────
    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    [IgnoreAntiforgeryToken]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadBulletinImage(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Chưa chọn file!" });

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (!ImageExts.Contains(ext))
            return Json(new { success = false, message = "Chỉ chấp nhận JPG, PNG, WebP, GIF!" });

        if (file.Length > ImageMaxBytes)
            return Json(new { success = false, message = "File không được vượt quá 10 MB!" });

        var fileName = Guid.NewGuid().ToString("N") + ext;
        var savePath = Path.Combine(BulletinFolder, fileName);

        try
        {
            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                Directory.CreateDirectory(BulletinFolder);
                await using var stream = new FileStream(savePath, FileMode.Create);
                await file.CopyToAsync(stream);
            }
            return Json(new { success = true, url = Url.Action("GetBulletinImage", "Image", new { fileName }) });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Lỗi lưu file: {ex.Message}" });
        }
    }

    [HttpGet, AllowAnonymous]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetBulletinImage(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
            return NotFound();
        return ServeNetworkImage(Path.Combine(BulletinFolder, fileName), fileName);
    }

    // ── Home banner ─────────────────────────────────────────────────────────
    // Stream ảnh/video banner từ \\192.168.1.5\vserp_picture\MY_SAMHO_HOME\
    [HttpGet, AllowAnonymous]
    [ResponseCache(Duration = 300)] // 5 phút — banner đổi thường xuyên hơn bulletin
    public IActionResult GetHomeBanner(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
            return NotFound();

        var ext = Path.GetExtension(fileName).ToLower();
        if (ext == ".mp4" || ext == ".webm")
            return ServeNetworkVideo(Path.Combine(HomeBannerFolder, fileName), fileName);
        return ServeNetworkImage(Path.Combine(HomeBannerFolder, fileName), fileName);
    }

    private IActionResult ServeNetworkVideo(string filePath, string fileName)
    {
        try
        {
            byte[] bytes;
            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                if (!System.IO.File.Exists(filePath)) return NotFound();
                bytes = System.IO.File.ReadAllBytes(filePath);
            }
            var mime = Path.GetExtension(fileName).ToLower() switch
            {
                ".webm" => "video/webm",
                _       => "video/mp4"
            };
            return File(bytes, mime, enableRangeProcessing: true);
        }
        catch { return NotFound(); }
    }

    // ── Upload Home banner (chỉ HR/Admin) ──────────────────────────────────
    // Banner popup chủ yếu xem trên mobile → ảnh DỌC 4:5 (1080×1350).
    // Validate 4:5 (±2%), 1080×1350 ≤ w×h ≤ 2160×2700, ≤5MB, JPG/PNG/WebP
    // Auto-resize về 1080×1350 q=85 JPEG
    [HttpPost]
    [ValidateAntiForgeryToken]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadHomeBanner(IFormFile? file)
    {
        // Role gate
        var role = CurrentUser?.RoleName;
        if (role is not ("HR" or "Admin"))
            return Json(new { success = false, message = "Bạn không có quyền upload banner" });

        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Chưa chọn file!" });

        // 1. MIME
        var allowedMime = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowedMime.Contains(file.ContentType))
            return Json(new { success = false, message = "Chỉ chấp nhận JPG/PNG/WebP" });

        // 2. Size
        if (file.Length > 5L * 1024 * 1024)
            return Json(new { success = false, message = $"Ảnh {(file.Length / 1024 / 1024)}MB quá lớn, tối đa 5MB" });

        try
        {
            using var stream = file.OpenReadStream();
            using var image  = await SixLabors.ImageSharp.Image.LoadAsync(stream);

            int w = image.Width, h = image.Height;

            // 3. Dimensions (ảnh dọc 4:5)
            if (w < 1080 || h < 1350)
                return Json(new { success = false, message = $"Ảnh {w}×{h} quá nhỏ. Tối thiểu 1080×1350 (khuyến nghị 1080×1350)" });
            if (w > 2160 || h > 2700)
                return Json(new { success = false, message = $"Ảnh {w}×{h} quá lớn. Tối đa 2160×2700" });

            // 4. Aspect ratio 4:5 ±2%
            double ratio  = (double)w / h;
            double target = 4.0 / 5.0;
            if (Math.Abs(ratio - target) > 0.02 * target)
                return Json(new { success = false, message = $"Tỉ lệ ảnh sai ({w}×{h}). Cần tỉ lệ 4:5 (ảnh dọc) — crop lại 1080×1350" });

            // 5. Resize về 1080×1350 nếu khác
            if (w != 1080 || h != 1350)
            {
                image.Mutate(x => x.Resize(1080, 1350));
            }

            // 6. Save JPEG q=85 vào share folder
            var fileName = "banner_" + Guid.NewGuid().ToString("N") + ".jpg";
            var savePath = Path.Combine(HomeBannerFolder, fileName);

            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                Directory.CreateDirectory(HomeBannerFolder);
                await using var fs = new FileStream(savePath, FileMode.Create);
                await image.SaveAsJpegAsync(fs, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 85 });
            }

            return Json(new { success = true, fileName, url = Url.Action("GetHomeBanner", "Image", new { fileName }) });
        }
        catch (SixLabors.ImageSharp.UnknownImageFormatException)
        {
            return Json(new { success = false, message = "Không đọc được file — có phải ảnh hợp lệ không?" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Lỗi upload: {ex.Message}" });
        }
    }

    // ── Upload Home banner VIDEO (chỉ HR/Admin) ────────────────────────────
    // Validate: MP4/WebM, ≤ 50MB, magic byte đúng định dạng, MP4 duration ≤ 30s (best-effort server-side).
    [HttpPost]
    [ValidateAntiForgeryToken]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadHomeBannerVideo(IFormFile? file)
    {
        var role = CurrentUser?.RoleName;
        if (role is not ("HR" or "Admin"))
            return Json(new { success = false, message = "Bạn không có quyền upload banner" });

        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Chưa chọn file!" });

        var allowedMime = new[] { "video/mp4", "video/webm" };
        var allowedExt  = new[] { ".mp4", ".webm" };
        var uploadExt   = Path.GetExtension(file.FileName).ToLower();
        if (!allowedMime.Contains(file.ContentType) || !allowedExt.Contains(uploadExt))
            return Json(new { success = false, message = "Chỉ chấp nhận file MP4 hoặc WebM" });

        if (file.Length > 50L * 1024 * 1024)
            return Json(new { success = false, message = $"Video {(file.Length / 1024 / 1024)}MB quá lớn, tối đa 50MB" });

        try
        {
            await using var upStream = file.OpenReadStream();

            // 1. Magic byte — ContentType do client khai, không tin được
            var magic = new byte[12];
            int mn = await ReadAtLeastAsync(upStream, magic, 12);
            bool looksMp4  = mn >= 12 && magic[4] == (byte)'f' && magic[5] == (byte)'t'
                                      && magic[6] == (byte)'y' && magic[7] == (byte)'p';
            bool looksWebm = mn >= 4  && magic[0] == 0x1A && magic[1] == 0x45
                                      && magic[2] == 0xDF && magic[3] == 0xA3;
            if (uploadExt == ".mp4"  && !looksMp4)
                return Json(new { success = false, message = "File không phải video MP4 hợp lệ" });
            if (uploadExt == ".webm" && !looksWebm)
                return Json(new { success = false, message = "File không phải video WebM hợp lệ" });

            // 2. Duration (MP4): parse moov/mvhd. Không đọc được → bỏ qua (client đã check).
            if (uploadExt == ".mp4" && upStream.CanSeek)
            {
                var dur = TryGetMp4DurationSeconds(upStream);
                if (dur is > 31)
                    return Json(new { success = false, message = $"Video {dur.Value:F1}s quá dài, tối đa 30 giây" });
            }

            var fileName = "banner_" + Guid.NewGuid().ToString("N") + uploadExt;
            var savePath = Path.Combine(HomeBannerFolder, fileName);

            if (upStream.CanSeek) upStream.Position = 0;
            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                Directory.CreateDirectory(HomeBannerFolder);
                await using var fs = new FileStream(savePath, FileMode.Create);
                if (upStream.CanSeek) await upStream.CopyToAsync(fs);
                else                  await file.CopyToAsync(fs);
            }

            return Json(new { success = true, fileName, url = Url.Action("GetHomeBanner", "Image", new { fileName }) });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Lỗi upload: {ex.Message}" });
        }
    }

    // Xoá 1 file banner khỏi network share (best-effort). Dùng khi thay ảnh banner để không tồn rác.
    internal static void TryDeleteHomeBannerFile(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
            return;
        try
        {
            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                var p = Path.Combine(HomeBannerFolder, fileName);
                if (System.IO.File.Exists(p)) System.IO.File.Delete(p);
            }
        }
        catch { /* best-effort, không chặn flow chính */ }
    }

    // Đọc tối thiểu `count` byte (hoặc tới EOF) — stream có thể trả từng phần.
    private static async Task<int> ReadAtLeastAsync(Stream s, byte[] buf, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = await s.ReadAsync(buf.AsMemory(total, count - total));
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private static int ReadExact(Stream s, byte[] buf, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = s.Read(buf, total, count - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    // Best-effort MP4 duration: đi qua các box top-level tìm 'moov' → 'mvhd'.
    // Trả null nếu không parse được (file lạ, moov thiếu, box 64-bit bất thường…).
    private static double? TryGetMp4DurationSeconds(Stream s)
    {
        try
        {
            long len = s.Length;
            long pos = 0;
            var hdr = new byte[16];
            while (pos + 8 <= len)
            {
                s.Position = pos;
                if (ReadExact(s, hdr, 8) < 8) break;
                long size = ((long)hdr[0] << 24) | ((long)hdr[1] << 16) | ((long)hdr[2] << 8) | hdr[3];
                string type = System.Text.Encoding.ASCII.GetString(hdr, 4, 4);
                int headerLen = 8;
                if (size == 1) // 64-bit largesize
                {
                    if (ReadExact(s, hdr, 8) < 8) break;
                    size = 0;
                    for (int i = 0; i < 8; i++) size = (size << 8) | hdr[i];
                    headerLen = 16;
                }
                if (size < headerLen) break;

                if (type == "moov")
                {
                    long moovEnd = pos + size;
                    long p = pos + headerLen;
                    var b = new byte[8];
                    while (p + 8 <= moovEnd && p + 8 <= len)
                    {
                        s.Position = p;
                        if (ReadExact(s, b, 8) < 8) break;
                        long bsize = ((long)b[0] << 24) | ((long)b[1] << 16) | ((long)b[2] << 8) | b[3];
                        string btype = System.Text.Encoding.ASCII.GetString(b, 4, 4);
                        if (bsize < 8) break;
                        if (btype == "mvhd")
                        {
                            int take = (int)Math.Min(bsize, 120);
                            var mv = new byte[take];
                            s.Position = p;
                            if (ReadExact(s, mv, take) < 32) return null;
                            byte version = mv[8];
                            if (version == 1)
                            {
                                if (take < 40) return null;
                                uint ts = (uint)((mv[28] << 24) | (mv[29] << 16) | (mv[30] << 8) | mv[31]);
                                ulong dur = 0;
                                for (int i = 32; i < 40; i++) dur = (dur << 8) | mv[i];
                                return ts == 0 ? (double?)null : (double)dur / ts;
                            }
                            else
                            {
                                if (take < 28) return null;
                                uint ts  = (uint)((mv[20] << 24) | (mv[21] << 16) | (mv[22] << 8) | mv[23]);
                                uint dur = (uint)((mv[24] << 24) | (mv[25] << 16) | (mv[26] << 8) | mv[27]);
                                return ts == 0 ? (double?)null : (double)dur / ts;
                            }
                        }
                        p += bsize;
                    }
                    return null;
                }
                pos += size;
            }
        }
        catch { /* parse lỗi → coi như không xác định */ }
        return null;
    }

    // ── Hình minh hoạ theo work cd (Dept+Line+Work) ─────────────────────────
    // Tên file chuẩn: {DEPTCD}_{LINECD}_{WORKCD}.jpg — xem GetWorkCdKey().
    internal static string GetWorkCdKey(string deptCd, string lineCd, string workCd)
        => $"{deptCd}_{lineCd}_{workCd}.jpg";

    // Check hàng loạt xem file đã tồn tại chưa - mở 1 phiên network share dùng chung cho cả danh sách.
    internal static HashSet<string> CheckWorkCdImagesExist(IEnumerable<string> fileNames)
    {
        var found = new HashSet<string>();
        try
        {
            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                foreach (var fileName in fileNames)
                {
                    if (System.IO.File.Exists(Path.Combine(WorkCdFolder, fileName)))
                        found.Add(fileName);
                }
            }
        }
        catch { /* network share tạm thời không truy cập được -> coi như chưa có ảnh */ }
        return found;
    }

    [HttpGet, AllowAnonymous]
    public IActionResult GetWorkCdImage(string deptCd, string lineCd, string workCd)
    {
        if (string.IsNullOrWhiteSpace(deptCd) || string.IsNullOrWhiteSpace(lineCd) || string.IsNullOrWhiteSpace(workCd))
            return BadRequest();
        var fileName = GetWorkCdKey(deptCd, lineCd, workCd);
        return ServeNetworkImage(Path.Combine(WorkCdFolder, fileName), fileName);
    }

    // Lưu nhiều ảnh cùng lúc. Mỗi file phải đặt tên đúng {DEPTCD}_{LINECD}_{WORKCD}.<ext>
    // (phần mở rộng không quan trọng, sẽ được convert về JPEG khi lưu).
    // Helper thuần (không phải action) - dùng chung cho WorkCdImageController.
    internal static async Task<(int savedCount, List<string> matched, List<object> skipped)> SaveWorkCdImagesAsync(List<IFormFile> files)
    {
        var matched = new List<string>();
        var skipped = new List<object>();

        if (files == null || files.Count == 0)
            return (0, matched, skipped);

        using (new NetworkShareHelper(ShareRoot, ShareCred))
        {
            Directory.CreateDirectory(WorkCdFolder);

            foreach (var file in files)
            {
                var baseName = Path.GetFileNameWithoutExtension(file.FileName);
                var ext = Path.GetExtension(file.FileName).ToLower();

                if (!ImageExts.Contains(ext))
                {
                    skipped.Add(new { file = file.FileName, reason = "Định dạng không hỗ trợ" });
                    continue;
                }
                if (file.Length > ImageMaxBytes)
                {
                    skipped.Add(new { file = file.FileName, reason = "File vượt quá 10 MB" });
                    continue;
                }

                var parts = baseName.Split('_');
                if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
                {
                    skipped.Add(new { file = file.FileName, reason = "Tên file không đúng mẫu DEPTCD_LINECD_WORKCD" });
                    continue;
                }

                try
                {
                    var savePath = Path.Combine(WorkCdFolder, GetWorkCdKey(parts[0], parts[1], parts[2]));
                    using var stream = file.OpenReadStream();
                    using var image = await SixLabors.ImageSharp.Image.LoadAsync(stream);
                    await using var fs = new FileStream(savePath, FileMode.Create);
                    await image.SaveAsJpegAsync(fs, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 90 });
                    matched.Add(file.FileName);
                }
                catch (Exception ex)
                {
                    skipped.Add(new { file = file.FileName, reason = $"Lỗi lưu: {ex.Message}" });
                }
            }
        }

        return (matched.Count, matched, skipped);
    }

    // ── Chữ ký ──────────────────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> UploadSignature(string empCd, IFormFile? file)
    {
        bool isExpat = CurrentUser?.RoleName == "Expat";

        if (string.IsNullOrWhiteSpace(empCd) || file == null || file.Length == 0)
        {
            TempData["ErrorMessage"] = isExpat ? "Please select a file!" : "Chưa chọn file hoặc mã nhân viên trống!";
            return RedirectToAction("ProfileUser", "Profile");
        }

        string ext = Path.GetExtension(file.FileName).ToLower();
        if (ext != ".jpg" && ext != ".jpeg")
        {
            TempData["ErrorMessage"] = isExpat ? "Only JPG files are accepted." : "Chỉ chấp nhận file JPG.";
            return RedirectToAction("ProfileUser", "Profile");
        }

        try
        {
            string savePath = Path.Combine(SignatureFolder, empCd + ".jpg");

            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                using (var stream = new FileStream(savePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
            }

            if (CurrentUser != null)
            {
                var updatedUser = CurrentUser;
                updatedUser.SIGNATUREBLOB = "Y";

                await AuthHelper.UpdateUserSessionAsync(HttpContext, updatedUser);

                await _accountService.UpdateSignatureFlagAsync(empCd, true, CurrentUser.EmpCd);
            }

            TempData["SuccessMessage"] = isExpat ? "Signature updated successfully!" : "Cập nhật chữ ký thành công!";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = isExpat ? $"File save error: {ex.Message}" : $"Lỗi lưu file: {ex.Message}";
        }

        return RedirectToAction("ProfileUser", "Profile");
    }
}

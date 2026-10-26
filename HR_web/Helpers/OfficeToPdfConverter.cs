using System.Diagnostics;

namespace HR_web.Helpers;

// Convert Word/Excel/PowerPoint sang PDF bằng LibreOffice headless, ngay lúc HR upload tài liệu đào
// tạo (FileController.UploadTrainingMaterial) — để công nhân xem được trong app qua đúng khung PDF
// đang có sẵn (MaterialPreview.cshtml), không cần máy cài Word/Excel/PowerPoint (yêu cầu 2026-10-26:
// Android không có gì tương đương QuickLook của iOS, máy công nhân phổ thông thường không có Office).
//
// PHẢI cài LibreOffice trên server production trước (soffice.exe) — việc này KHÔNG tự làm được từ
// đây, cần IT cài tay trên máy chủ Windows rồi khai đường dẫn vào appsettings.json → LibreOfficePath.
// Nếu chưa cài/convert lỗi: TryConvertAsync trả false, không ném exception — tài liệu vẫn lưu/dùng
// được bình thường như trước đây (chỉ mất khả năng preview, nút "Tải về máy" vẫn hoạt động).
public static class OfficeToPdfConverter
{
    public static async Task<bool> TryConvertAsync(string sourceFilePath, string outDir, string libreOfficePath, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(libreOfficePath) || !OperatingSystem.IsWindows())
            return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = libreOfficePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--headless");
            psi.ArgumentList.Add("--convert-to");
            psi.ArgumentList.Add("pdf");
            psi.ArgumentList.Add("--outdir");
            psi.ArgumentList.Add(outDir);
            psi.ArgumentList.Add(sourceFilePath);

            using var process = Process.Start(psi);
            if (process == null) return false;

            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                return false;
            }

            var expectedPdf = Path.Combine(outDir, Path.GetFileNameWithoutExtension(sourceFilePath) + ".pdf");
            return process.ExitCode == 0 && File.Exists(expectedPdf);
        }
        catch
        {
            return false;
        }
    }
}

namespace HR_web.API.Service;

/// <summary>
/// Xử lý file video trên network share: stream, upload, xóa.
/// Không đụng DB — mỗi module tự cập nhật VIDEO_PATH sau khi gọi service này.
/// </summary>
public class VideoFileService
{
    private readonly IConfiguration _config;

    public static readonly string[] AllowedExts = { ".mp4", ".webm", ".mov" };
    public const long MaxBytes = 500L * 1024 * 1024;

    public VideoFileService(IConfiguration config) => _config = config;

    // Base path từ config — mỗi máy set 1 lần trong appsettings.json
    // Ví dụ: \\192.168.1.5\vserp_picture
    private string BasePath =>
        _config["VideoBasePath"] ?? @"\\192.168.1.5\vserp_picture";

    // Full path đến file: {BasePath}\{folder}\{id}{ext}
    // Ví dụ: \\192.168.1.5\vserp_picture\GUIDE_VIDEO\5.mp4
    public string FilePath(string folder, int id, string ext) =>
        Path.Combine(BasePath, folder, $"{id}{ext}");

    // Tìm file thực tế của 1 item (quét tất cả ext được phép)
    public (string path, string ext)? Find(string folder, int id)
    {
        foreach (var ext in AllowedExts)
        {
            var p = FilePath(folder, id, ext);
            if (File.Exists(p)) return (p, ext);
        }
        return null;
    }

    // Xóa tất cả extension cũ của 1 item
    public void DeleteAll(string folder, int id)
    {
        foreach (var ext in AllowedExts)
        {
            var p = FilePath(folder, id, ext);
            if (File.Exists(p)) File.Delete(p);
        }
    }

    // Lưu file lên share, trả về extension đã lưu
    public async Task<(bool ok, string message, string ext)> SaveAsync(
        string folder, int id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return (false, "Không có file", "");

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (!AllowedExts.Contains(ext))
            return (false, "Chỉ chấp nhận MP4, WebM, MOV", "");
        if (file.Length > MaxBytes)
            return (false, "Video không được vượt quá 500 MB", "");
        if (!IsValidFolder(folder))
            return (false, "Folder không hợp lệ", "");

        Directory.CreateDirectory(Path.Combine(BasePath, folder));
        DeleteAll(folder, id);

        var savePath = FilePath(folder, id, ext);
        await using var stream = new FileStream(savePath, FileMode.Create);
        await file.CopyToAsync(stream);

        return (true, "Lưu thành công", ext);
    }

    // Chỉ cho phép folder name là chữ/số/gạch dưới — tránh path traversal
    public static bool IsValidFolder(string? folder) =>
        !string.IsNullOrEmpty(folder) &&
        folder.All(c => char.IsLetterOrDigit(c) || c == '_');

    public static string MimeType(string ext) => ext.ToLower() switch
    {
        ".mp4"  => "video/mp4",
        ".webm" => "video/webm",
        ".mov"  => "video/quicktime",
        _       => "application/octet-stream"
    };
}

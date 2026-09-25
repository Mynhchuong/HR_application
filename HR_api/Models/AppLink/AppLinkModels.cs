namespace HR_api.Models.AppLink;

// HR_APP_LINK — kho link cài đặt app iOS, 1 link chỉ phát cho đúng 1 người (Guide/Index import + phát link)
public class AppLinkModel
{
    public int       ID { get; set; }
    public string    LINK_URL { get; set; } = "";
    public string    STATUS { get; set; } = "AVAILABLE";   // AVAILABLE | ASSIGNED | REPLACED
    public string?   ASSIGNED_EMPCD { get; set; }
    public string?   ASSIGNED_EMP_NAME { get; set; }
    public DateTime? ASSIGNED_DT { get; set; }
    public string?   INST_ID { get; set; }
    public DateTime? INST_DT { get; set; }
}

public class AppLinkStatsModel
{
    public int TOTAL { get; set; }
    public int AVAILABLE { get; set; }
    public int ASSIGNED { get; set; }
    public int REPLACED { get; set; }
}

public class ImportAppLinkRequest
{
    public List<string> LINKS { get; set; } = new();
    public string? LOGIN_USER { get; set; }
}

public class RequestAppLinkRequest
{
    public string EMPCD { get; set; } = "";
}

public class ReassignAppLinkRequest
{
    public string EMPCD { get; set; } = "";
    public string? ACTOR_EMPCD { get; set; }
}

// Kết quả phát link — ALREADY_ASSIGNED=true nghĩa là mã thẻ này đã từng nhận link rồi (web sẽ hỏi
// "Cập nhật app" hay "Đổi máy mới" thay vì hiện link luôn).
public class AppLinkResult
{
    public bool    success { get; set; }
    public string? message { get; set; }
    public bool    alreadyAssigned { get; set; }
    public string? linkUrl { get; set; }
    public DateTime? assignedDt { get; set; }
    // Hết quota 3 lần -> trả về các link cũ đã từng phát cho mã thẻ này, để NV thử lại link cũ
    // (link ASSIGNED có thể còn dùng được, REPLACED là link cũ đã đóng khi đổi máy).
    public bool    limitReached { get; set; }
    public List<AppLinkHistoryItem>? previousLinks { get; set; }
}

public class AppLinkHistoryItem
{
    public string    LINK_URL { get; set; } = "";
    public string    STATUS   { get; set; } = "";
    public DateTime? ASSIGNED_DT { get; set; }
}

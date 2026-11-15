import Foundation

/// Dữ liệu widget hiển thị. Giữ phẳng, đúng những gì sếp chốt 2026-11-15:
///   - Công nhân : thông báo quan trọng · đơn của mình duyệt tới đâu · bản tin Samho
///   - Quản lý   : bao nhiêu đơn cần duyệt · hôm nay bao nhiêu NV nghỉ · bao nhiêu đơn ra cổng chờ ký
///   - Expat     : giống quản lý, khác ngôn ngữ
struct WidgetSnapshot: Codable {
    var role      = "worker"          // worker | manager | expat
    var lang      = "vi"              // vi | en | ko
    var fullName  = ""
    var unread    = 0

    // Công nhân
    var noticeTitle   = ""
    var noticeIsHigh  = false
    var noticeMore    = 0
    var reqPending    = 0
    var reqApproved   = 0
    var reqRejected   = 0
    var bulletinTitle = ""

    // Quản lý / Expat
    var needApprove = 0
    var gatePass    = 0
    var leaveToday  = 0

    var updatedAt: Date? = nil
    var isStale   = false             // lần gọi gần nhất hỏng -> số đang cũ
}

/// Đọc/ghi vùng dùng chung giữa app và widget.
enum WidgetStore {

    /// Phải trùng App Group trong entitlements của CẢ 2 target, và trùng hằng số cùng tên bên
    /// Mysamho/WidgetBridge.swift.
    static let appGroup = "group.vn.esamho.hrapp"

    private static let kCookie   = "widget_cookie"
    private static let kBase     = "widget_base_url"
    private static let kSnapshot = "widget_snapshot"
    private static let kAt       = "widget_snapshot_at"
    private static let kStale    = "widget_snapshot_stale"

    private static var store: UserDefaults? { UserDefaults(suiteName: appGroup) }

    static var cookie: String?  { store?.string(forKey: kCookie) }
    static var baseURL: String  { store?.string(forKey: kBase) ?? "https://hr.esamho.com/HR_Web" }

    static func loadCached() -> WidgetSnapshot? {
        guard let d = store?.data(forKey: kSnapshot),
              var s = try? JSONDecoder().decode(WidgetSnapshot.self, from: d) else { return nil }
        s.updatedAt = store?.object(forKey: kAt) as? Date
        s.isStale   = store?.bool(forKey: kStale) ?? false
        return s
    }

    static func save(_ s: WidgetSnapshot) {
        guard let d = try? JSONEncoder().encode(s) else { return }
        store?.set(d, forKey: kSnapshot)
        store?.set(Date(), forKey: kAt)
        store?.set(false, forKey: kStale)
    }

    static func markStale() { store?.set(true, forKey: kStale) }

    /// Gọi `/Widget/Data` bằng cookie phiên mà app đã chép sang.
    /// Hỏng (mất mạng / hết phiên) thì trả bản đã lưu kèm cờ `isStale` — widget vẫn có nội dung.
    static func fetch() async -> WidgetSnapshot? {
        guard let cookie, !cookie.isEmpty,
              let url = URL(string: baseURL + "/Widget/Data") else { return loadCached() }

        var req = URLRequest(url: url)
        req.httpMethod = "GET"
        req.setValue(cookie, forHTTPHeaderField: "Cookie")
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        req.timeoutInterval = 10

        // Hết phiên thì server trả 302 về trang Đăng nhập — KHÔNG đi theo, nếu không sẽ nhận về
        // HTML trang login rồi parse JSON thất bại một cách khó hiểu.
        let cfg = URLSessionConfiguration.ephemeral
        cfg.httpShouldSetCookies = false
        let session = URLSession(configuration: cfg, delegate: NoRedirect(), delegateQueue: nil)

        do {
            let (data, resp) = try await session.data(for: req)
            guard let http = resp as? HTTPURLResponse, http.statusCode == 200,
                  let jo = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                  jo["ok"] as? Bool == true
            else { markStale(); return loadCached() }

            var s = WidgetSnapshot()
            s.role     = jo["role"] as? String ?? "worker"
            s.lang     = jo["lang"] as? String ?? "vi"
            s.fullName = jo["fullName"] as? String ?? ""
            s.unread   = jo["unread"] as? Int ?? 0

            if let notices = jo["notices"] as? [[String: Any]], let first = notices.first {
                s.noticeTitle  = first["title"] as? String ?? ""
                s.noticeIsHigh = first["high"] as? Bool ?? false
                s.noticeMore   = max(0, notices.count - 1)
            }
            if let reqs = jo["myReqs"] as? [[String: Any]] {
                for r in reqs {
                    switch r["status"] as? String {
                    case "APPROVED": s.reqApproved += 1
                    case "REJECTED": s.reqRejected += 1
                    default:         s.reqPending  += 1
                    }
                }
            }
            if let b = jo["bulletin"] as? [String: Any] {
                s.bulletinTitle = b["title"] as? String ?? ""
            }
            if let c = jo["counters"] as? [String: Any] {
                s.needApprove = c["needApprove"] as? Int ?? 0
                s.gatePass    = c["gatePass"]    as? Int ?? 0
                s.leaveToday  = c["leaveToday"]  as? Int ?? 0
            }

            save(s)
            s.updatedAt = Date()
            return s
        } catch {
            markStale()
            return loadCached()
        }
    }
}

/// Chặn URLSession tự đi theo redirect (xem chú thích ở `fetch`).
private final class NoRedirect: NSObject, URLSessionTaskDelegate {
    func urlSession(_ session: URLSession, task: URLSessionTask,
                    willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest,
                    completionHandler: @escaping (URLRequest?) -> Void) {
        completionHandler(nil)
    }
}

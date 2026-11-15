import Foundation
import UIKit
import UserNotifications
import WebKit
import WidgetKit

/// Cầu nối app -> widget.
///
/// Widget của iOS chạy trong tiến trình RIÊNG và KHÔNG đọc được cookie của WKWebView, nên app phải
/// chủ động chép cookie phiên sang vùng dùng chung (App Group) mỗi khi nó đổi. Widget lấy cookie đó
/// gọi `/Widget/Data` của HR_web — tức là xác thực bằng đúng phiên người dùng đang đăng nhập, không
/// phải nhúng API key của HR_api vào app (nhúng key thì ai moi ra cũng tra được dữ liệu mọi mã NV).
enum WidgetBridge {

    /// Phải trùng với App Group khai trong entitlements của CẢ app lẫn widget, và trùng hằng số
    /// cùng tên bên MysamhoWidget/WidgetStore.swift — lệch 1 ký tự là widget đọc ra rỗng mà không báo lỗi.
    static let appGroup = "group.vn.esamho.hrapp"

    static let kCookie  = "widget_cookie"
    static let kBase    = "widget_base_url"

    private static var store: UserDefaults? { UserDefaults(suiteName: appGroup) }

    /// Chép cookie phiên hiện tại của WebView sang App Group rồi yêu cầu widget vẽ lại.
    /// Gọi sau mỗi lần tải xong trang bên trong (đăng nhập xong, duyệt đơn xong...).
    static func syncSession(from webView: WKWebView, baseURL: String) {
        guard let host = URL(string: baseURL)?.host else { return }

        webView.configuration.websiteDataStore.httpCookieStore.getAllCookies { cookies in
            // Chỉ lấy cookie của đúng host này; gộp lại thành chuỗi "a=1; b=2" như header Cookie.
            let mine = cookies.filter { host.hasSuffix($0.domain.hasPrefix(".") ? String($0.domain.dropFirst()) : $0.domain) }
            guard !mine.isEmpty else { return }

            let header = mine.map { "\($0.name)=\($0.value)" }.joined(separator: "; ")
            store?.set(header,  forKey: kCookie)
            store?.set(baseURL, forKey: kBase)
            WidgetCenter.shared.reloadAllTimelines()
            refreshBadge()
            // Nút tắt đặt theo role, mà role nằm trong bản chụp widget vừa lấy — làm mới cùng lượt.
            DispatchQueue.main.async { AppShortcuts.refresh() }
        }
    }

    /// Cập nhật số trên icon app theo số thông báo CHƯA ĐỌC thật.
    ///
    /// Server chỉ gắn được badge cho thông báo gửi tới đúng 1 người; thông báo gửi cả công ty/phòng
    /// dùng chung 1 lần gửi nên không mang số riêng (xem NotificationHelper.SendFcmAsync). Vì vậy
    /// app phải tự chỉnh lại mỗi khi có dịp — nếu không, đọc hết thông báo rồi mà số vẫn đứng nguyên
    /// trên icon.
    ///
    /// Chỉ cần đúng 1 con số nên ở đây đọc mỗi trường `unread`, không dựng lại cả bộ giải mã như
    /// widget (WidgetStore.fetch).
    static func refreshBadge() {
        guard let cookie = store?.string(forKey: kCookie), !cookie.isEmpty,
              let base = store?.string(forKey: kBase),
              let url  = URL(string: base + "/Widget/Data") else { return }

        var req = URLRequest(url: url)
        req.setValue(cookie, forHTTPHeaderField: "Cookie")
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        req.timeoutInterval = 10

        URLSession.shared.dataTask(with: req) { data, resp, _ in
            // Hết phiên -> server trả 302 về trang Đăng nhập. Giữ nguyên badge cũ, đừng xoá về 0
            // làm người dùng tưởng đã đọc hết.
            guard let http = resp as? HTTPURLResponse, http.statusCode == 200, let data,
                  let jo = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let unread = jo["unread"] as? Int else { return }

            UNUserNotificationCenter.current().setBadgeCount(unread)
        }.resume()
    }

    /// Đăng xuất -> xoá sạch, không để số liệu người cũ nằm lại trên màn hình chính.
    static func clearSession() {
        UNUserNotificationCenter.current().setBadgeCount(0)
        DispatchQueue.main.async { AppShortcuts.clear() }
        store?.removeObject(forKey: kCookie)
        store?.removeObject(forKey: "widget_snapshot")
        store?.removeObject(forKey: "widget_snapshot_at")
        WidgetCenter.shared.reloadAllTimelines()
    }

    /// Có thông báo đẩy -> số liệu có thể đã đổi, bảo widget lấy lại.
    static func reloadAfterPush() {
        WidgetCenter.shared.reloadAllTimelines()
        refreshBadge()
    }
}

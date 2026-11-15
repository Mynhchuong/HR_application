import UIKit

/// Nút tắt hiện ra khi giữ icon My Samho trên màn hình chính.
///
/// Đặt theo ROLE: công nhân cần tạo đơn cho mình, quản lý cần duyệt đơn của người khác — hai nhóm
/// gần như không dùng chung chức năng nào, để chung 1 bộ thì ai cũng phải lướt qua mục không liên
/// quan. Role đọc từ bản chụp mà widget đã lấy về (App Group), nên không tốn thêm lần gọi mạng.
enum AppShortcuts {

    /// Khoá nhận diện nút tắt nào được bấm; giá trị userInfo mang đường dẫn cần mở.
    static let pathKey = "open_path"

    private static func L(_ lang: String, _ vi: String, _ en: String, _ ko: String) -> String {
        switch lang {
        case "ko": return ko
        case "en": return en
        default:   return vi
        }
    }

    /// Gọi sau mỗi lần đồng bộ phiên — role có thể đổi (máy dùng chung giữa các ca).
    static func refresh() {
        let store = UserDefaults(suiteName: WidgetBridge.appGroup)
        var role = "worker", lang = "vi"
        if let data = store?.data(forKey: "widget_snapshot"),
           let jo = try? JSONSerialization.jsonObject(with: data) as? [String: Any] {
            role = jo["role"] as? String ?? "worker"
            lang = jo["lang"] as? String ?? "vi"
        }

        var items: [UIApplicationShortcutItem] = []
        if role == "worker" {
            items = [
                make("leave", L(lang, "Xin nghỉ phép", "Request leave", "휴가 신청"),
                     "/Leave/LeaveMyRequests", "calendar.badge.minus"),
                make("gate", L(lang, "Đăng ký ra cổng", "Gate pass", "외출 신청"),
                     "/GatePass/GpMyRequests", "arrow.right.square"),
                make("pay", L(lang, "Phiếu lương", "Payslip", "급여명세서"),
                     "/Payslip/Index", "doc.text")
            ]
        } else {
            // Expat có màn hình duyệt RIÊNG (…ForExpat) — dùng nhầm màn hình của quản lý người Việt
            // thì giao diện ra tiếng Việt hết.
            let expat = role == "expat"
            items = [
                make("appr_leave", L(lang, "Duyệt nghỉ phép", "Approve leave", "휴가 승인"),
                     expat ? "/Leave/LeaveApprovalForExpat" : "/Leave/TeamSchedule", "checkmark.seal"),
                make("appr_gate", L(lang, "Duyệt ra cổng", "Approve gate pass", "외출 승인"),
                     expat ? "/GatePass/GpListForExpat" : "/GatePass/GpListForSupervisor", "arrow.right.square"),
                make("calendar", L(lang, "Lịch nghỉ nhóm", "Team calendar", "팀 일정"),
                     expat ? "/Leave/TeamCalendarForExpat" : "/Leave/TeamCalendar", "calendar")
            ]
        }
        UIApplication.shared.shortcutItems = items
    }

    /// Đăng xuất -> bỏ nút tắt, không để người sau cầm máy thấy lối tắt của role người trước.
    static func clear() {
        UIApplication.shared.shortcutItems = []
    }

    private static func make(_ type: String, _ title: String,
                             _ path: String, _ symbol: String) -> UIApplicationShortcutItem {
        UIApplicationShortcutItem(
            type: type,
            localizedTitle: title,
            localizedSubtitle: nil,
            icon: UIApplicationShortcutIcon(systemImageName: symbol),
            userInfo: [pathKey: path as NSSecureCoding])
    }

    /// Xử lý khi người dùng bấm 1 nút tắt. Trả về true nếu đã nhận.
    @discardableResult
    static func handle(_ item: UIApplicationShortcutItem) -> Bool {
        guard let path = item.userInfo?[pathKey] as? String else { return false }

        // App đang chạy -> điều hướng ngay; app chưa dựng xong màn hình -> để dành, loadWebApp sẽ dùng.
        if let vc = UIApplication.shared.connectedScenes
            .compactMap({ ($0 as? UIWindowScene)?.keyWindow?.rootViewController as? ViewController })
            .first {
            vc.openPath(path)
        } else {
            ViewController.pendingOpenPath = path
        }
        return true
    }
}

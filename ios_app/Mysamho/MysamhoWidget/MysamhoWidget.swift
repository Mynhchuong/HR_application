import WidgetKit
import SwiftUI

// MARK: - Nhãn theo ngôn ngữ
// Widget nằm ngoài WebView nên không dùng được bộ dịch của web — 3 ngôn ngữ viết thẳng ở đây.
private func L(_ lang: String, _ vi: String, _ en: String, _ ko: String) -> String {
    switch lang {
    case "ko": return ko
    case "en": return en
    default:   return vi
    }
}

/// Ngôn ngữ dùng khi CHƯA đăng nhập (chưa biết tài khoản của ai).
/// Lúc đó server chưa trả "lang" nên phải đoán theo ngôn ngữ của máy — mặc định cứng tiếng Việt sẽ
/// làm anh em Hàn/nước ngoài thấy dòng "Chạm để đăng nhập" bằng tiếng Việt ngay trên màn hình chính,
/// trong khi họ chưa có cách nào đổi.
private var deviceLang: String {
    let code = Locale.preferredLanguages.first?.prefix(2).lowercased() ?? "en"
    if code == "ko" { return "ko" }
    if code == "vi" { return "vi" }
    return "en"     // còn lại dùng tiếng Anh, đúng quy ước của phần Expat trên web
}

// MARK: - Timeline

struct WidgetEntry: TimelineEntry {
    let date: Date
    let snap: WidgetSnapshot?
}

struct Provider: TimelineProvider {
    func placeholder(in context: Context) -> WidgetEntry {
        WidgetEntry(date: Date(), snap: nil)
    }

    func getSnapshot(in context: Context, completion: @escaping (WidgetEntry) -> Void) {
        // Xem trước trong thư viện widget: dùng bản đã lưu, không gọi mạng cho nhanh.
        completion(WidgetEntry(date: Date(), snap: WidgetStore.loadCached()))
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<WidgetEntry>) -> Void) {
        Task {
            let snap = await WidgetStore.fetch()
            // iOS tự quyết lúc nào chạy lại, 30 phút chỉ là mong muốn. Đường làm mới chính vẫn là
            // app gọi WidgetCenter.reloadAllTimelines (mở app, nhận thông báo đẩy).
            let next = Calendar.current.date(byAdding: .minute, value: 30, to: Date())!
            completion(Timeline(entries: [WidgetEntry(date: Date(), snap: snap)], policy: .after(next)))
        }
    }
}

// MARK: - Giao diện

struct MysamhoWidgetView: View {
    var entry: WidgetEntry

    private var lang: String { entry.snap?.lang ?? deviceLang }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            header
            if let s = entry.snap {
                if s.role == "worker" { workerBody(s) } else { managerBody(s) }
                Spacer(minLength: 0)
                footer(s)
            } else {
                Spacer(minLength: 0)
                Text(L(lang, "Chạm để đăng nhập", "Tap to sign in", "탭하여 로그인"))
                    .font(.system(size: 13, weight: .semibold))
                Spacer(minLength: 0)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .widgetURL(URL(string: "mysamho://open"))
    }

    private var header: some View {
        HStack(spacing: 5) {
            Image(systemName: "bell.fill")
                .font(.system(size: 11))
                .foregroundStyle(Color(red: 0.86, green: 0.15, blue: 0.15))
            Text(entry.snap?.fullName.isEmpty == false ? entry.snap!.fullName : "My Samho")
                .font(.system(size: 12, weight: .bold))
                .lineLimit(1)
            Spacer(minLength: 2)
            if let u = entry.snap?.unread, u > 0 {
                Text(u > 99 ? "99+" : "\(u)")
                    .font(.system(size: 10, weight: .bold))
                    .padding(.horizontal, 6).padding(.vertical, 2)
                    .background(Capsule().fill(Color.secondary.opacity(0.18)))
                    .foregroundStyle(Color(red: 0.86, green: 0.15, blue: 0.15))
            }
        }
    }

    // ── Công nhân ───────────────────────────────────────────────────────────
    @ViewBuilder
    private func workerBody(_ s: WidgetSnapshot) -> some View {
        VStack(alignment: .leading, spacing: 5) {
            Text(s.noticeTitle.isEmpty
                 ? L(lang, "Không có thông báo mới", "No new notifications", "새 알림 없음")
                 : (s.noticeIsHigh ? "⚠ " : "") + s.noticeTitle
                   + (s.noticeMore > 0 ? L(lang, "  (+\(s.noticeMore) nữa)", "  (+\(s.noticeMore) more)", "  (+\(s.noticeMore)건)") : ""))
                .font(.system(size: 13, weight: .semibold))
                .lineLimit(2)

            Text(myRequestLine(s))
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
                .lineLimit(2)

            Text(s.bulletinTitle.isEmpty
                 ? L(lang, "📰 Chưa có bản tin mới", "📰 No new bulletin", "📰 새 소식 없음")
                 : "📰 " + s.bulletinTitle)
                .font(.system(size: 11))
                .foregroundStyle(.secondary)
                .lineLimit(2)
        }
    }

    private func myRequestLine(_ s: WidgetSnapshot) -> String {
        let head = L(lang, "Đơn của bạn: ", "Your requests: ", "내 신청: ")
        if s.reqPending + s.reqApproved + s.reqRejected == 0 {
            return head + L(lang, "chưa có đơn nào", "none yet", "없음")
        }
        var parts = ["\(s.reqPending)" + L(lang, " chờ duyệt", " pending", "건 대기")]
        if s.reqApproved > 0 { parts.append("\(s.reqApproved)" + L(lang, " đã duyệt", " approved", "건 승인")) }
        if s.reqRejected > 0 { parts.append("\(s.reqRejected)" + L(lang, " từ chối", " rejected", "건 반려")) }
        return head + parts.joined(separator: " · ")
    }

    // ── Quản lý / Expat ─────────────────────────────────────────────────────
    @ViewBuilder
    private func managerBody(_ s: WidgetSnapshot) -> some View {
        HStack(spacing: 6) {
            cell("\(s.needApprove)", L(lang, "Cần bạn duyệt", "To approve", "결재 대기"), accent: true)
            cell("\(s.gatePass)",    L(lang, "Ra cổng chờ ký", "Gate passes", "외출 결재"))
            cell("\(s.leaveToday)",  L(lang, "Nghỉ hôm nay", "On leave today", "오늘 휴가"))
        }
    }

    private func cell(_ value: String, _ label: String, accent: Bool = false) -> some View {
        VStack(spacing: 1) {
            Text(value)
                .font(.system(size: 21, weight: .bold))
                .foregroundStyle(accent ? Color(red: 0.86, green: 0.15, blue: 0.15) : Color.primary)
            Text(label)
                .font(.system(size: 9))
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .lineLimit(2)
                .minimumScaleFactor(0.8)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color.secondary.opacity(0.12)))
    }

    // ── Chân thẻ ────────────────────────────────────────────────────────────
    @ViewBuilder
    private func footer(_ s: WidgetSnapshot) -> some View {
        // Số cũ thì nói thẳng là cũ, đừng để người dùng tin đó là số thật lúc này.
        if s.isStale {
            Text(L(lang, "Số liệu cũ — mở app để cập nhật",
                         "Outdated — open app to refresh",
                         "오래된 정보 — 앱에서 새로고침"))
                .font(.system(size: 9)).foregroundStyle(.secondary).lineLimit(1)
        } else if let at = s.updatedAt {
            Text(agoText(at))
                .font(.system(size: 9)).foregroundStyle(.secondary).lineLimit(1)
        }
    }

    private func agoText(_ at: Date) -> String {
        let mins = Int(Date().timeIntervalSince(at) / 60)
        if mins < 1  { return L(lang, "Vừa cập nhật", "Just updated", "방금 업데이트") }
        if mins < 60 { return L(lang, "Cập nhật \(mins) phút trước", "Updated \(mins)m ago", "\(mins)분 전 업데이트") }
        let hrs = mins / 60
        return L(lang, "Cập nhật \(hrs) giờ trước", "Updated \(hrs)h ago", "\(hrs)시간 전 업데이트")
    }
}

// MARK: - Khai báo widget

@main
struct MysamhoWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "MysamhoWidget", provider: Provider()) { entry in
            if #available(iOS 17.0, *) {
                MysamhoWidgetView(entry: entry)
                    .containerBackground(.fill.tertiary, for: .widget)
            } else {
                MysamhoWidgetView(entry: entry).padding(12)
            }
        }
        // Hai dòng này hiện trong THƯ VIỆN WIDGET của iOS, theo ngôn ngữ của MÁY (người dùng chưa
        // mở app cũng thấy) — lấy bản dịch từ Localizable.xcstrings, đừng viết chuỗi cứng.
        .configurationDisplayName(Text("My Samho"))
        .description(Text("Tasks to handle and new notifications"))
        .supportedFamilies([.systemMedium])
    }
}

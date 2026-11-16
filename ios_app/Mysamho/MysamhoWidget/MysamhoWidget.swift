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

// Bảng màu dùng chung với bản Android (res/values/colors.xml + values-night) để 2 nền tảng nhìn
// như một. Nền tối phải dùng tông NHẠT hơn: màu đậm của nền sáng đặt lên nền đen thì chữ số chìm
// hẳn, gần như không đọc được.
private struct Pal {
    let accent, blue, amber, green: Color

    static let light = Pal(accent: .init(red: 0.86, green: 0.15, blue: 0.15),
                           blue:   .init(red: 0.15, green: 0.39, blue: 0.92),
                           amber:  .init(red: 0.85, green: 0.47, blue: 0.02),
                           green:  .init(red: 0.02, green: 0.59, blue: 0.41))

    static let dark  = Pal(accent: .init(red: 0.97, green: 0.44, blue: 0.44),   // #F87171
                           blue:   .init(red: 0.38, green: 0.65, blue: 0.98),   // #60A5FA
                           amber:  .init(red: 0.98, green: 0.75, blue: 0.14),   // #FBBF24
                           green:  .init(red: 0.20, green: 0.83, blue: 0.60))   // #34D399
}

struct MysamhoWidgetView: View {
    var entry: WidgetEntry

    @Environment(\.colorScheme) private var scheme

    private var lang: String { entry.snap?.lang ?? deviceLang }
    private var P: Pal { scheme == .dark ? .dark : .light }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
            Divider().padding(.top, 8)

            if let s = entry.snap {
                if s.role == "worker" { workerBody(s) } else { managerBody(s); Spacer(minLength: 4) }
                footer(s).padding(.top, 5)
            } else {
                Spacer(minLength: 0)
                HStack(spacing: 8) {
                    Image(systemName: "bell.badge.fill").font(.system(size: 14)).foregroundStyle(P.accent)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(L(lang, "Chạm để đăng nhập", "Tap to sign in", "탭하여 로그인"))
                            .font(.system(size: 13, weight: .semibold))
                        Text(L(lang, "Xem thông báo và đơn của bạn ngay trên màn hình chính",
                                     "See your notifications and requests right here",
                                     "알림과 신청 현황을 홈 화면에서 바로 확인"))
                            .font(.system(size: 10)).foregroundStyle(.secondary).lineLimit(2)
                    }
                }
                Spacer(minLength: 0)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .widgetURL(URL(string: "mysamho://open"))
    }

    private var header: some View {
        HStack(spacing: 7) {
            Image(systemName: "bell.fill")
                .font(.system(size: 11, weight: .semibold))
                .foregroundStyle(P.accent)
            Text(entry.snap?.fullName.isEmpty == false ? entry.snap!.fullName : "My Samho")
                .font(.system(size: 13, weight: .bold))
                .lineLimit(1)
            Spacer(minLength: 2)
            if let u = entry.snap?.unread, u > 0 {
                // Nền đỏ đặc chữ trắng: đọc được từ xa hơn nền nhạt chữ đỏ.
                Text(u > 99 ? "99+" : "\(u)")
                    .font(.system(size: 10, weight: .bold))
                    .foregroundStyle(.white)
                    .padding(.horizontal, 6).padding(.vertical, 2)
                    .background(Capsule().fill(P.accent))
            }
        }
    }

    // ── Công nhân: 3 dòng, mỗi dòng 1 icon dẫn mắt ──────────────────────────
    @ViewBuilder
    private func workerBody(_ s: WidgetSnapshot) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            row(icon: s.noticeIsHigh ? "exclamationmark.circle.fill" : "bell.fill",
                tint: P.accent,
                text: s.noticeTitle.isEmpty
                      ? L(lang, "Không có thông báo mới", "No new notifications", "새 알림 없음")
                      : s.noticeTitle + (s.noticeMore > 0
                          ? L(lang, "  (+\(s.noticeMore) nữa)", "  (+\(s.noticeMore) more)", "  (+\(s.noticeMore)건)")
                          : ""),
                strong: true, lines: 2)

            Spacer(minLength: 2)
            row(icon: "checkmark.circle.fill", tint: P.green, text: myRequestLine(s))
            Spacer(minLength: 2)
            row(icon: "newspaper.fill", tint: P.blue,
                text: s.bulletinTitle.isEmpty
                      ? L(lang, "Chưa có bản tin mới", "No new bulletin", "새 소식 없음")
                      : s.bulletinTitle)
        }
        .padding(.top, 9)
        .frame(maxHeight: .infinity, alignment: .top)
    }

    private func row(icon: String, tint: Color, text: String,
                     strong: Bool = false, lines: Int = 1) -> some View {
        HStack(alignment: .top, spacing: 8) {
            Image(systemName: icon)
                .font(.system(size: 11))
                .foregroundStyle(tint)
                .frame(width: 13, alignment: .center)
                .padding(.top, 1)
            Text(text)
                .font(.system(size: strong ? 12 : 11, weight: strong ? .semibold : .regular))
                .foregroundStyle(strong ? Color.primary : Color.secondary)
                .lineLimit(lines)
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

    // ── Quản lý / Expat: 3 ô số, mỗi ô 1 màu riêng ──────────────────────────
    @ViewBuilder
    private func managerBody(_ s: WidgetSnapshot) -> some View {
        HStack(spacing: 7) {
            cell("\(s.needApprove)", L(lang, "Cần duyệt tháng này", "To approve (month)", "이달 결재 대기"),
                 icon: "checklist", tint: P.accent)
            cell("\(s.gatePass)", L(lang, "Ra cổng chờ ký", "Gate passes", "외출 결재"),
                 icon: "arrow.right.square.fill", tint: P.blue)
            cell("\(s.leaveToday)", L(lang, "Nghỉ hôm nay", "On leave today", "오늘 휴가"),
                 icon: "calendar", tint: P.amber)
        }
        .padding(.top, 9)
    }

    private func cell(_ value: String, _ label: String, icon: String, tint: Color) -> some View {
        VStack(spacing: 1) {
            Image(systemName: icon).font(.system(size: 10)).foregroundStyle(tint)
            Text(value)
                .font(.system(size: 20, weight: .bold))
                .foregroundStyle(tint)
                .minimumScaleFactor(0.6)
                .lineLimit(1)
            Text(label)
                .font(.system(size: 9))
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .lineLimit(2)
                .minimumScaleFactor(0.75)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .padding(.vertical, 6)
        .background(RoundedRectangle(cornerRadius: 14).fill(tint.opacity(0.10)))
    }

    // ── Chân thẻ ────────────────────────────────────────────────────────────
    @ViewBuilder
    private func footer(_ s: WidgetSnapshot) -> some View {
        // Số cũ thì nói thẳng là cũ, VÀ nói luôn phải làm gì — chỉ ghi "số liệu cũ" thì ngay tại
        // xưởng ai cũng tưởng widget hỏng, trong khi thật ra chỉ vì đang bắt wifi công ty.
        if s.isStale {
            switch s.failReason {
            case "companyWifi":
                // App iOS chỉ chạy qua máy chủ ngoài, wifi công ty không ra tới đó được.
                Label(L(lang, "Wifi công ty không xem được — bật 4G hoặc wifi cá nhân",
                              "Company Wi-Fi can't load this — use 4G or personal Wi-Fi",
                              "회사 Wi-Fi로는 불러올 수 없음 — 4G 또는 개인 Wi-Fi 사용"),
                      systemImage: "wifi.exclamationmark")
                    .font(.system(size: 9)).foregroundStyle(P.amber).lineLimit(2)
            case "session":
                Label(L(lang, "Phiên đã hết — mở app đăng nhập lại",
                              "Session expired — open app to sign in",
                              "세션 만료 — 앱에서 다시 로그인"), systemImage: "person.crop.circle.badge.exclamationmark")
                    .font(.system(size: 9)).foregroundStyle(.secondary).lineLimit(2)
            default:
                Label(L(lang, "Mất mạng — đang hiện số liệu cũ",
                              "No connection — showing old data",
                              "연결 없음 — 이전 정보 표시"), systemImage: "wifi.slash")
                    .font(.system(size: 9)).foregroundStyle(.secondary).lineLimit(2)
            }
        } else if let at = s.updatedAt {
            Text(agoText(at))
                .font(.system(size: 9)).foregroundStyle(.secondary).opacity(0.75).lineLimit(1)
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

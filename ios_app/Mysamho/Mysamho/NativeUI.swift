import UIKit
import WebKit

/// Giao diện GỐC cho thông báo / xác nhận / cảnh báo của web (yêu cầu 2026-11-21: khách review app thấy toast/modal
/// kiểu web là biết ngay app chỉ là WebView).
///
/// Web gọi qua 1 bridge generic `window.webkit.messageHandlers.nativeUI.postMessage({...})` — xem
/// HR_web/wwwroot/js/native-ui.js. Lớp này CHỈ vẽ giao diện, không biết nghiệp vụ (đúng NATIVE_HELPER_RULES.md).
/// Bản Android tương ứng: NativeUi.java.
///   {action:"toast",   message, type: success|error|warning|info}   → banner trượt từ trên xuống + rung
///   {action:"confirm", id, title, message, okText, cancelText, destructive} → NativeUI._done(id, true|false)
///   {action:"alert",   id, title, message, okText}                         → NativeUI._done(id, true)
///   {action:"haptic",  style: success|warning|error|light}
final class NativeUIPresenter {
    private weak var host: UIViewController?
    private let webView: () -> WKWebView?
    private weak var currentBanner: UIView?

    init(host: UIViewController, webView: @escaping () -> WKWebView?) {
        self.host = host
        self.webView = webView
    }

    func handle(_ body: Any) {
        guard let o = body as? [String: Any] else { return }
        let s = { (k: String) in (o[k] as? String) ?? "" }
        switch s("action") {
        case "toast":   toast(s("message"), type: s("type"))
        case "confirm": confirm(id: s("id"), title: s("title"), message: s("message"),
                                ok: s("okText"), cancel: s("cancelText"), destructive: (o["destructive"] as? Bool) ?? false)
        case "alert":   alert(id: s("id"), title: s("title"), message: s("message"), ok: s("okText"))
        case "haptic":  haptic(s("style"))
        default: break
        }
    }

    // MARK: Hộp thoại

    private func confirm(id: String, title: String, message: String, ok: String, cancel: String, destructive: Bool) {
        let a = UIAlertController(title: title.isEmpty ? nil : title, message: message, preferredStyle: .alert)
        a.addAction(UIAlertAction(title: cancel.isEmpty ? "Hủy" : cancel, style: .cancel) { [weak self] _ in self?.done(id, false) })
        a.addAction(UIAlertAction(title: ok.isEmpty ? "OK" : ok, style: destructive ? .destructive : .default) { [weak self] _ in self?.done(id, true) })
        if destructive { haptic("warning") }
        present(a)
    }

    private func alert(id: String, title: String, message: String, ok: String) {
        let a = UIAlertController(title: title.isEmpty ? nil : title, message: message, preferredStyle: .alert)
        a.addAction(UIAlertAction(title: ok.isEmpty ? "OK" : ok, style: .default) { [weak self] _ in self?.done(id, true) })
        present(a)
    }

    /// Trình bày từ màn hình đang nằm trên cùng (đang mở sẵn 1 alert/màn quét mã thì vẫn hiện được).
    private func present(_ vc: UIViewController) {
        guard var top = host else { return }
        while let p = top.presentedViewController, !p.isBeingDismissed { top = p }
        top.present(vc, animated: true)
    }

    private func done(_ id: String, _ ok: Bool) {
        guard !id.isEmpty, let data = try? JSONSerialization.data(withJSONObject: [id]),
              let arr = String(data: data, encoding: .utf8) else { return }
        // arr = ["n12"] → lấy phần tử đầu, chuỗi đã được escape an toàn
        webView()?.evaluateJavaScript("window.NativeUI&&NativeUI._done(\(arr)[0],\(ok))", completionHandler: nil)
    }

    // MARK: Banner thông báo (kiểu thông báo hệ thống: nền mờ, bo tròn, trượt từ trên xuống, vuốt lên để tắt)

    private func toast(_ message: String, type: String) {
        guard !message.isEmpty, let hostView = host?.view else { return }
        currentBanner?.removeFromSuperview()

        let (symbol, color): (String, UIColor) = {
            switch type {
            case "error":   return ("xmark.octagon.fill", .systemRed)
            case "warning": return ("exclamationmark.triangle.fill", .systemOrange)
            case "info":    return ("info.circle.fill", .systemBlue)
            default:        return ("checkmark.circle.fill", .systemGreen)
            }
        }()

        let blur = UIVisualEffectView(effect: UIBlurEffect(style: .systemThickMaterial))
        blur.translatesAutoresizingMaskIntoConstraints = false
        blur.layer.cornerRadius = 18
        blur.layer.cornerCurve = .continuous
        blur.clipsToBounds = true

        let shadowHost = UIView()
        shadowHost.translatesAutoresizingMaskIntoConstraints = false
        shadowHost.layer.shadowColor = UIColor.black.cgColor
        shadowHost.layer.shadowOpacity = 0.16
        shadowHost.layer.shadowRadius = 14
        shadowHost.layer.shadowOffset = CGSize(width: 0, height: 6)
        shadowHost.accessibilityLabel = message
        shadowHost.isAccessibilityElement = true
        shadowHost.addSubview(blur)

        let icon = UIImageView(image: UIImage(systemName: symbol,
                                              withConfiguration: UIImage.SymbolConfiguration(pointSize: 20, weight: .semibold)))
        icon.tintColor = color
        icon.setContentHuggingPriority(.required, for: .horizontal)

        let label = UILabel()
        label.text = message
        label.numberOfLines = 4
        label.font = .preferredFont(forTextStyle: .subheadline)
        label.adjustsFontForContentSizeCategory = true
        label.textColor = .label

        let row = UIStackView(arrangedSubviews: [icon, label])
        row.axis = .horizontal
        row.alignment = .center
        row.spacing = 12
        row.translatesAutoresizingMaskIntoConstraints = false
        blur.contentView.addSubview(row)

        hostView.addSubview(shadowHost)
        let g = hostView.safeAreaLayoutGuide
        NSLayoutConstraint.activate([
            blur.topAnchor.constraint(equalTo: shadowHost.topAnchor),
            blur.bottomAnchor.constraint(equalTo: shadowHost.bottomAnchor),
            blur.leadingAnchor.constraint(equalTo: shadowHost.leadingAnchor),
            blur.trailingAnchor.constraint(equalTo: shadowHost.trailingAnchor),
            row.topAnchor.constraint(equalTo: blur.contentView.topAnchor, constant: 14),
            row.bottomAnchor.constraint(equalTo: blur.contentView.bottomAnchor, constant: -14),
            row.leadingAnchor.constraint(equalTo: blur.contentView.leadingAnchor, constant: 16),
            row.trailingAnchor.constraint(equalTo: blur.contentView.trailingAnchor, constant: -18),
            shadowHost.topAnchor.constraint(equalTo: g.topAnchor, constant: 8),
            shadowHost.centerXAnchor.constraint(equalTo: g.centerXAnchor),
            shadowHost.leadingAnchor.constraint(greaterThanOrEqualTo: g.leadingAnchor, constant: 12),
            shadowHost.widthAnchor.constraint(lessThanOrEqualToConstant: 480),
        ])
        hostView.layoutIfNeeded()
        currentBanner = shadowHost

        // Trượt xuống từ phía trên
        shadowHost.transform = CGAffineTransform(translationX: 0, y: -(shadowHost.frame.maxY + 20))
        UIView.animate(withDuration: 0.5, delay: 0, usingSpringWithDamping: 0.78, initialSpringVelocity: 0.4) {
            shadowHost.transform = .identity
        }
        haptic(type == "error" ? "error" : type == "warning" ? "warning" : type == "info" ? "light" : "success")

        let dismiss: () -> Void = { [weak shadowHost] in
            guard let v = shadowHost, v.superview != nil else { return }
            UIView.animate(withDuration: 0.3, delay: 0, options: .curveEaseIn, animations: {
                v.transform = CGAffineTransform(translationX: 0, y: -(v.frame.maxY + 20))
                v.alpha = 0
            }, completion: { _ in v.removeFromSuperview() })
        }
        let swipe = UISwipeGestureRecognizer(target: nil, action: nil)
        swipe.direction = .up
        shadowHost.addGestureRecognizer(swipe)
        swipe.addTarget(BlockTarget.shared, action: #selector(BlockTarget.fire(_:)))
        BlockTarget.shared.actions[ObjectIdentifier(swipe)] = dismiss
        let tap = UITapGestureRecognizer(target: BlockTarget.shared, action: #selector(BlockTarget.fire(_:)))
        shadowHost.addGestureRecognizer(tap)
        BlockTarget.shared.actions[ObjectIdentifier(tap)] = dismiss

        let seconds: Double = (type == "error" || message.count > 70) ? 4.0 : 2.6
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds) {
            dismiss()
            BlockTarget.shared.actions[ObjectIdentifier(swipe)] = nil
            BlockTarget.shared.actions[ObjectIdentifier(tap)] = nil
        }
    }

    // MARK: Rung phản hồi

    func haptic(_ style: String) {
        switch style {
        case "success": UINotificationFeedbackGenerator().notificationOccurred(.success)
        case "warning": UINotificationFeedbackGenerator().notificationOccurred(.warning)
        case "error":   UINotificationFeedbackGenerator().notificationOccurred(.error)
        default:        UIImpactFeedbackGenerator(style: .light).impactOccurred()
        }
    }
}

/// Gắn closure cho UIGestureRecognizer (UIKit chỉ nhận target/selector).
private final class BlockTarget: NSObject {
    static let shared = BlockTarget()
    var actions: [ObjectIdentifier: () -> Void] = [:]
    @objc func fire(_ g: UIGestureRecognizer) { actions[ObjectIdentifier(g)]?() }
}

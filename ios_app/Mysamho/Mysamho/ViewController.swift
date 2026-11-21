import UIKit
import WebKit
import LocalAuthentication
import Network
import UserNotifications

class ViewController: UIViewController {

    // MARK: - UI Components

    private var webView: WKWebView!
    private var progressBar: UIProgressView!
    private var noNetworkView: UIView!

    // MARK: - Properties

    private let networkMonitor = NWPathMonitor()
    private var isConnected = true
    private var isFirstPathUpdate = true
    private var progressObservation: NSKeyValueObservation?
    private var lastDownloadedFileURL: URL?
    private var pendingNotificationTap = false
    private var pendingNotiId: String?
    // Mã NV đang đăng nhập trên web (bridge setEmpcd, _Layout.cshtml gọi mỗi lần tải trang) — dùng để
    // checkAppVersion() tra đúng link App Store riêng của NV đó (xem AppVersionController.Check).
    private static var currentEmpcd: String?
    // Chỉ hiện popup ép cập nhật 1 lần/phiên app, dù checkAppVersion() gọi lại khi có empcd.
    private var hasShownForceUpdateAlert = false
    // Đăng nhập Face ID — credentials từ Login.cshtml CHƯA chắc đúng (có thể login sai), chỉ lưu thật
    // vào Keychain khi didFinish xác nhận điều hướng kế tiếp không còn là trang Login (= thành công).
    private var pendingCredentials: KeychainCredentialHelper.Credentials?
    // Chỉ tự thử Face ID 1 lần/phiên app, tránh hỏi lặp lại gây khó chịu nếu user bấm Huỷ.
    private var hasTriedBiometricLogin = false

    private let appURL = "https://hr.esamho.com/HR_Web"
    private var overlayView: UIView?
    // Splash logo — che khoảng trắng chớp giữa lúc WebView bắt đầu tải và trang Home vẽ xong lần đầu
    // (progress bar mỏng không đủ rõ, cảm giác như app đứng hình — yêu cầu tối ưu UX 2026-10-26).
    private var splashView: UIView?
    private weak var splashGroup: UIView?
    private var didDismissSplash = false

    // MARK: - Lifecycle

    private var didStartMonitoring = false

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .white
        setupNoNetworkView()
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(handleNotificationTap),
            name: .didTapPushNotification,
            object: nil
        )
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(handleForegroundPush),
            name: .didReceiveForegroundPush,
            object: nil
        )

        // App từ nền quay lại: nếu WebView bị iOS giải phóng tiến trình (thiếu RAM khi nằm nền lâu) thì trang trống
        // trắng — tự tải lại (xem recoverBlankWebView).
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(handleWillEnterForeground),
            name: UIApplication.willEnterForegroundNotification,
            object: nil
        )

        // Consume pending notification tap from cold launch (app was killed)
        if AppDelegate.pendingNotificationTap {
            AppDelegate.pendingNotificationTap = false
            pendingNotificationTap = true
            pendingNotiId = AppDelegate.pendingNotiId
            AppDelegate.pendingNotiId = nil
        }

        diagLog("viewDidLoad pendingTap=\(pendingNotificationTap) notiId=\(pendingNotiId ?? "nil")")

        checkAppVersion()
    }

    override func viewDidAppear(_ animated: Bool) {
        super.viewDidAppear(animated)
        guard !didStartMonitoring else { return }
        didStartMonitoring = true
        startNetworkMonitoring()
        #if DEBUG
        // Công tắc chẩn đoán (chỉ Debug): DIAG_WARM_TAP=<giây>:<noti_id> -> sau N giây tự bấm thông báo giả khi app đang chạy.
        // DIAG_AUTO_NOTICE=<giây> -> tự bấm "Đã hiểu" sau N giây. Máy ảo không nhận lệnh chạm từ
        // ngoài, nên không có cái này thì không quay/kiểm được hoạt ảnh mở màn.
        if let d = ProcessInfo.processInfo.environment["DIAG_AUTO_NOTICE"], let delay = Double(d) {
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in
                guard let self, self.overlayView != nil else { return }
                diagLog("DIAG_AUTO_NOTICE firing")
                self.didConfirmNotice()
            }
        }
        if let cfg = ProcessInfo.processInfo.environment["DIAG_WARM_TAP"] {
            let parts = cfg.split(separator: ":").map(String.init)
            let delay = Double(parts.first ?? "20") ?? 20
            let id = parts.count > 1 ? parts[1] : ""
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) {
                diagLog("DIAG_WARM_TAP firing id=\(id)")
                AppDelegate.pendingNotificationTap = true
                AppDelegate.pendingNotiId = id
                NotificationCenter.default.post(name: .didTapPushNotification, object: nil)
            }
        }
        #endif
    }

    deinit {
        networkMonitor.cancel()
        progressObservation?.invalidate()
        NotificationCenter.default.removeObserver(self)
    }

    // MARK: - Startup Notice

    private func showStartupNotice() {
        let overlay = UIView(frame: view.bounds)
        overlay.backgroundColor = UIColor.black.withAlphaComponent(0.5)
        overlay.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        overlayView = overlay

        let card = UIView()
        card.backgroundColor = .systemBackground
        card.layer.cornerRadius = 20
        card.translatesAutoresizingMaskIntoConstraints = false

        let stack = UIStackView()
        stack.axis = .vertical
        stack.alignment = .center
        stack.spacing = 16
        stack.translatesAutoresizingMaskIntoConstraints = false

        let mascotImages = ["a_hoi", "chi"]
        let randomName = mascotImages[Int.random(in: 0..<mascotImages.count)]
        let imageView = UIImageView(image: UIImage(named: randomName))
        imageView.contentMode = .scaleAspectFit
        imageView.translatesAutoresizingMaskIntoConstraints = false
        imageView.heightAnchor.constraint(equalToConstant: 130).isActive = true
        imageView.widthAnchor.constraint(equalToConstant: 130).isActive = true
        imageView.layer.cornerRadius = 65
        imageView.clipsToBounds = true

        let titleLabel = UILabel()
        titleLabel.text = "Chào mừng / Welcome"
        titleLabel.font = .boldSystemFont(ofSize: 20)
        titleLabel.textColor = .label
        titleLabel.textAlignment = .center
        titleLabel.numberOfLines = 0

        let noticeBox = UIView()
        noticeBox.backgroundColor = UIColor.systemBlue.withAlphaComponent(0.08)
        noticeBox.layer.cornerRadius = 12
        noticeBox.translatesAutoresizingMaskIntoConstraints = false

        let noticeLabel = UILabel()
        noticeLabel.text = "Để có trải nghiệm tốt nhất, hãy dùng 4G/5G hoặc WiFi cá nhân.\n\nFor the best experience, please use cellular data or personal WiFi."
        noticeLabel.font = .systemFont(ofSize: 14)
        noticeLabel.textColor = .label
        noticeLabel.textAlignment = .center
        noticeLabel.numberOfLines = 0
        noticeLabel.translatesAutoresizingMaskIntoConstraints = false

        noticeBox.addSubview(noticeLabel)
        NSLayoutConstraint.activate([
            noticeLabel.topAnchor.constraint(equalTo: noticeBox.topAnchor, constant: 14),
            noticeLabel.bottomAnchor.constraint(equalTo: noticeBox.bottomAnchor, constant: -14),
            noticeLabel.leadingAnchor.constraint(equalTo: noticeBox.leadingAnchor, constant: 16),
            noticeLabel.trailingAnchor.constraint(equalTo: noticeBox.trailingAnchor, constant: -16)
        ])

        // Nền trắng chữ đỏ, viền đỏ mảnh — hợp tông thương hiệu hơn nút xanh mặc định của iOS,
        // và nối liền mạch với nền trắng mà nút này sẽ phóng to thành khi bấm.
        var btnConfig = UIButton.Configuration.filled()
        btnConfig.title = "Đã hiểu / Got it"
        btnConfig.baseBackgroundColor = .white
        btnConfig.baseForegroundColor = ViewController.brandRed
        btnConfig.background.strokeColor = ViewController.brandRed.withAlphaComponent(0.35)
        btnConfig.background.strokeWidth = 1.5
        btnConfig.attributedTitle = AttributedString(
            "Đã hiểu / Got it",
            attributes: AttributeContainer([.font: UIFont.systemFont(ofSize: 17, weight: .semibold)]))
        btnConfig.cornerStyle = .medium
        btnConfig.contentInsets = NSDirectionalEdgeInsets(top: 14, leading: 60, bottom: 14, trailing: 60)
        let okButton = UIButton(configuration: btnConfig)
        okButton.addTarget(self, action: #selector(didConfirmNotice), for: .touchUpInside)

        stack.addArrangedSubview(imageView)
        stack.addArrangedSubview(titleLabel)
        stack.addArrangedSubview(noticeBox)
        stack.addArrangedSubview(okButton)

        card.addSubview(stack)
        overlay.addSubview(card)
        view.addSubview(overlay)

        NSLayoutConstraint.activate([
            card.centerXAnchor.constraint(equalTo: overlay.centerXAnchor),
            card.centerYAnchor.constraint(equalTo: overlay.centerYAnchor),
            card.leadingAnchor.constraint(greaterThanOrEqualTo: overlay.leadingAnchor, constant: 32),
            card.trailingAnchor.constraint(lessThanOrEqualTo: overlay.trailingAnchor, constant: -32),
            noticeBox.widthAnchor.constraint(equalTo: stack.widthAnchor),
            stack.topAnchor.constraint(equalTo: card.topAnchor, constant: 28),
            stack.bottomAnchor.constraint(equalTo: card.bottomAnchor, constant: -28),
            stack.leadingAnchor.constraint(equalTo: card.leadingAnchor, constant: 24),
            stack.trailingAnchor.constraint(equalTo: card.trailingAnchor, constant: -24)
        ])

        // ── Bước 1: LOGO APP nở ra thành bảng thông báo ─────────────────────
        // Mở app ra là thấy ngay icon quen thuộc ở giữa màn hình trắng, rồi chính nó giãn ra thành
        // bảng thông báo. Trước đây bảng này bật ra giữa nền xám, không dính dáng gì tới cái icon
        // người dùng vừa bấm. (Bước 2 — bảng thông báo -> logo CÔNG TY — nằm ở dismissOverlay.)
        if !didMorphIconIntoCard {
            didMorphIconIntoCard = true
            morphAppIcon(into: card, on: overlay)
            return
        }

        overlay.alpha = 0
        UIView.animate(withDuration: 0.3) { overlay.alpha = 1 }
    }

    /// Icon app (ô vuông bo tròn đỏ, chữ S trắng) giãn dần thành bảng thông báo trắng.
    private func morphAppIcon(into card: UIView, on overlay: UIView) {
        // Phải ép tính layout trước: thẻ dùng Auto Layout nên lúc này frame vẫn đang bằng 0,
        // không biết phải giãn tới kích thước nào.
        view.layoutIfNeeded()
        let target = card.frame

        overlay.backgroundColor = .white          // nền trắng, không phải lớp mờ đen như sau này
        overlay.alpha = 1
        card.alpha = 0

        let side: CGFloat = ViewController.splashIconSide
        let icon = UIView(frame: CGRect(x: target.midX - side / 2, y: target.midY - side / 2,
                                        width: side, height: side))
        icon.backgroundColor = ViewController.logoCrimson
        icon.layer.cornerRadius = 23
        icon.layer.cornerCurve = .continuous
        icon.clipsToBounds = true

        // Chữ S tách ra từ icon app đang mang màu đỏ — ở đây nền đã đỏ nên phải tô lại thành trắng.
        let sMark = UIImageView(image: UIImage(named: "SamhoS")?.withRenderingMode(.alwaysTemplate))
        sMark.contentMode = .scaleAspectFit
        sMark.tintColor = .white
        sMark.frame = CGRect(x: side * 0.3, y: side * 0.17, width: side * 0.4, height: side * 0.66)
        icon.addSubview(sMark)
        overlay.addSubview(icon)

        // Hiện SẴN đúng như icon hệ điều hành vừa hiện (không ẩn rồi bật lại — đó là nhịp "giật" đầu tiên)

        // Giãn thành thẻ. Chữ S KHÔNG biến mất đột ngột: nó co nhỏ về giữa thẻ và đổi từ trắng
        // sang đỏ ngay trong lúc nền đỏ nhạt dần thành trắng — nên suốt cả quãng này luôn có đúng
        // một chữ S trên màn hình. Chính nó là sợi dây nối logo APP với logo CÔNG TY ở bước sau,
        // thay vì mỗi bước vẽ lại một hình mới chẳng liên quan gì nhau.
        let seedSide: CGFloat = 40
        let seed = CGRect(x: target.midX - seedSide * 0.32, y: target.midY - seedSide / 2,
                          width: seedSide * 0.64, height: seedSide)
        splashSeedFrame = seed

        UIView.animate(withDuration: 0.46, delay: 0.5, options: [.curveEaseInOut], animations: {
            icon.frame = target
            icon.backgroundColor = .systemBackground
            icon.layer.cornerRadius = 20
            sMark.frame = overlay.convert(seed, to: icon)
            sMark.tintColor = ViewController.logoCrimson
        }, completion: nil)

        // Nội dung thẻ hiện ĐÈ LÊN trong lúc thẻ còn đang giãn (không chờ xong mới hiện) — chồng
        // lấn nhau như vậy mới thấy liền mạch, tách bạch từng bước là ra cảm giác giật cục.
        UIView.animate(withDuration: 0.3, delay: 0.72, options: [.curveEaseOut], animations: {
            card.alpha = 1
            sMark.alpha = 0
            overlay.backgroundColor = UIColor.black.withAlphaComponent(0.5)
        }, completion: { _ in
            icon.removeFromSuperview()
        })
    }

    /// Bấm "Đã hiểu": bảng thông báo CO LẠI thành một đĩa tròn đen ngay giữa màn hình, đúng chỗ
    /// và đúng cỡ cái vòng của logo công ty sắp hiện ra. Nhờ vậy bảng "xếp lại thành vòng" chứ
    /// không phải bảng biến mất rồi logo bật ra ở đâu đó.
    private func dismissOverlay() {
        guard let overlay = overlayView, let card = overlay.subviews.first else {
            overlayView?.removeFromSuperview(); overlayView = nil; return
        }

        // Co về ĐÚNG khối icon đỏ của màn chào (showSplash) — cùng chỗ, cùng cỡ, cùng màu, nên bảng
        // "xếp lại thành icon" rồi màn chào chạy tiếp liền mạch (thiết kế mở app mới 2026-11-20).
        let side = ViewController.splashIconSide
        let target = CGRect(x: (view.bounds.width - side) / 2,
                            y: view.bounds.height / 2 - ViewController.splashIconLift - side / 2, width: side, height: side)

        // Thẻ đang được Auto Layout giữ chỗ. Gán thẳng .frame mà không gỡ ràng buộc thì vòng
        // layout kế tiếp kéo nó về chỗ cũ -> thẻ phình to một nhịp rồi mới co, nhìn rất giật.
        view.layoutIfNeeded()
        let current = card.frame
        overlay.removeConstraints(overlay.constraints)
        card.translatesAutoresizingMaskIntoConstraints = true
        card.frame = current

        // Nội dung bên trong mờ đi SỚM hơn, để lúc thẻ co nhỏ không còn chữ bị bóp méo.
        UIView.animate(withDuration: 0.2) {
            card.subviews.forEach { $0.alpha = 0 }
        }
        UIView.animate(withDuration: 0.44, delay: 0.04, options: [.curveEaseInOut], animations: {
            card.frame = target
            card.layer.cornerRadius = side * 0.225
            card.backgroundColor = ViewController.logoCrimson       // đỏ, nối màu với icon màn chào
            overlay.backgroundColor = .white                        // lớp mờ xám -> nền trắng
        }, completion: nil)

        // Đĩa đen mờ đi ĐÚNG LÚC vòng đen phía dưới hiện lên (xem showSplash) — hai hình cùng màu,
        // cùng chỗ, cùng cỡ nên mắt thấy đĩa "rỗng ruột" thành vòng. Xoá thẳng lớp phủ ngay khi co
        // xong thì đĩa biến mất lúc vòng mới hiện được nửa, thành ra chớp một nhịp.
        UIView.animate(withDuration: 0.3, delay: 0.46, options: [.curveEaseInOut], animations: {
            card.alpha = 0
        }, completion: { _ in
            overlay.removeFromSuperview()
            self.overlayView = nil
        })
    }

    @objc private func didConfirmNotice() {
        dismissOverlay()
        startWebView()
    }

    private func startWebView() {
        diagLog("startWebView isConnected=\(isConnected) pendingTap=\(pendingNotificationTap)")
        showSplash()
        // setupWebView() chỉ load trang thật SAU KHI User-Agent đã set xong (completion) — tránh race
        // condition: evaluateJavaScript lấy UA gốc chạy bất đồng bộ, nếu gọi loadWebApp() ngay sau
        // (không chờ) thì request ĐẦU TIÊN (quan trọng nhất) có thể đi trước khi UA kịp nối hậu tố
        // "MySamhoMobile/iOS", khiến lần tải đầu web không nhận ra là app (yêu cầu rà soát kỹ
        // 2026-10-30, sau vụ WAF chặn do UA bị thay hẳn).
        setupWebView {
            self.setupProgressBar()
            self.setupPullToRefresh()
            if self.isConnected {
                self.loadWebApp()
            } else {
                self.showNoNetwork()
            }
        }
    }

    // MARK: - UI Setup

    private func setupWebView(completion: @escaping () -> Void) {
        let config = WKWebViewConfiguration()
        config.allowsInlineMediaPlayback = true
        config.mediaTypesRequiringUserActionForPlayback = []

        let prefs = WKWebpagePreferences()
        prefs.allowsContentJavaScript = true
        config.defaultWebpagePreferences = prefs

        let fixTextSize = WKUserScript(
            source: "document.documentElement.style.webkitTextSizeAdjust = '100%';",
            injectionTime: .atDocumentStart,
            forMainFrameOnly: true
        )
        config.userContentController.addUserScript(fixTextSize)

        // Bridge cho web JS gọi: window.webkit.messageHandlers.clearNotifications.postMessage(null)
        config.userContentController.add(self, name: "clearNotifications")
        // Web gọi window.webkit.messageHandlers.scanBarcode.postMessage(null) để mở màn hình quét mã vạch gốc.
        config.userContentController.add(self, name: "scanBarcode")
        // Web gọi window.webkit.messageHandlers.setEmpcd.postMessage(empcd) mỗi lần tải trang (_Layout.cshtml)
        // để native biết đang là NV nào — dùng tra đúng link App Store riêng của họ khi ép cập nhật
        // (checkAppVersion), vì iOS không có App Store public (xem AppVersionController.Check).
        config.userContentController.add(self, name: "setEmpcd")
        // Đăng nhập lại qua Face ID/Touch ID — Login.cshtml gọi saveCredentials lúc submit form (CHƯA
        // chắc đúng, chỉ lưu thật nếu điều hướng kế tiếp không còn là trang Login — xem didFinish bên
        // dưới); logoutForm (_Layout.cshtml) gọi clearCredentials lúc đăng xuất chủ động.
        config.userContentController.add(self, name: "saveCredentials")
        config.userContentController.add(self, name: "clearCredentials")

        webView = WKWebView(frame: .zero, configuration: config)
        webView.translatesAutoresizingMaskIntoConstraints = false
        webView.navigationDelegate = self
        webView.uiDelegate = self
        webView.allowsBackForwardNavigationGestures = true
        // Nền WebView = nền xám nhạt của trang (#f5f5f5): chuyển trang không còn chớp trắng (2026-11-20)
        let pageBg = UIColor(red: 245/255, green: 245/255, blue: 245/255, alpha: 1)
        webView.isOpaque = false
        webView.backgroundColor = pageBg
        webView.scrollView.backgroundColor = pageBg
        // Giữ lâu link không mở bản xem trước — bản xem trước không có phiên đăng nhập nên hiện
        // trang Login kèm popup điền mật khẩu của iOS (báo lỗi 2026-11-18).
        webView.allowsLinkPreview = false
        // 2026-10-30: đã thử đổi sang "giữ UA gốc + nối hậu tố" (nghi ngờ UA thay hẳn bị 1 WAF chặn),
        // nhưng test thật cho kết quả NGƯỢC LẠI — bản 10.1/10.2 bên Android (UA thay hẳn, cùng pattern
        // với dòng dưới) chạy ổn định trên wifi ngoài, còn bản UA-nối-thêm mới bị "Forbidden" trên
        // đúng wifi đó. Hoàn tác về UA gốc đã xác nhận ổn định — KHÔNG đổi lại thành nối thêm nữa trừ
        // khi có bằng chứng mới rõ ràng hơn (xem lịch sử chat 2026-10-30 nếu cần tra lại).
        webView.customUserAgent = "MySamhoMobile/iOS"
        completion()
        // Tắt pinch-zoom trang web trong app — cùng yêu cầu đã tắt bên Android (setSupportZoom(false)),
        // tránh NV vô tình zoom to lố giao diện khi chạm 2 ngón (yêu cầu 2026-10-26). Chỉ tắt gesture
        // zoom ở tầng UIScrollView của WKWebView, KHÔNG đụng meta viewport của trang web (web vẫn tự
        // zoom bình thường khi mở bằng Safari/Chrome ngoài app).
        webView.scrollView.pinchGestureRecognizer?.isEnabled = false

        view.insertSubview(webView, belowSubview: noNetworkView)

        NSLayoutConstraint.activate([
            webView.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor),
            webView.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            webView.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            webView.bottomAnchor.constraint(equalTo: view.bottomAnchor)
        ])

        progressObservation = webView.observe(\.estimatedProgress, options: .new) { [weak self] webView, _ in
            let progress = Float(webView.estimatedProgress)
            self?.progressBar?.setProgress(progress, animated: true)
            self?.progressBar?.isHidden = progress >= 1.0
        }
    }

    private func setupProgressBar() {
        progressBar = UIProgressView(progressViewStyle: .bar)
        progressBar.translatesAutoresizingMaskIntoConstraints = false
        progressBar.progressTintColor = .systemBlue
        progressBar.trackTintColor = .clear

        view.addSubview(progressBar)

        NSLayoutConstraint.activate([
            progressBar.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor),
            progressBar.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            progressBar.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            progressBar.heightAnchor.constraint(equalToConstant: 2)
        ])
    }

    private func setupNoNetworkView() {
        noNetworkView = UIView()
        noNetworkView.translatesAutoresizingMaskIntoConstraints = false
        noNetworkView.backgroundColor = .systemBackground
        noNetworkView.isHidden = true

        let stack = UIStackView()
        stack.translatesAutoresizingMaskIntoConstraints = false
        stack.axis = .vertical
        stack.alignment = .center
        stack.spacing = 16

        let imageView = UIImageView(image: UIImage(systemName: "wifi.slash"))
        imageView.tintColor = .systemGray
        imageView.contentMode = .scaleAspectFit
        imageView.translatesAutoresizingMaskIntoConstraints = false
        imageView.heightAnchor.constraint(equalToConstant: 60).isActive = true
        imageView.widthAnchor.constraint(equalToConstant: 60).isActive = true

        let titleLabel = UILabel()
        titleLabel.text = "Không thể kết nối\nUnable to connect"
        titleLabel.font = .boldSystemFont(ofSize: 20)
        titleLabel.textColor = .label
        titleLabel.textAlignment = .center
        titleLabel.numberOfLines = 0

        let messageLabel = UILabel()
        messageLabel.text = "Vui lòng kiểm tra kết nối và thử lại.\n\nPlease check your connection and try again."
        messageLabel.font = .systemFont(ofSize: 15)
        messageLabel.textColor = .secondaryLabel
        messageLabel.textAlignment = .center
        messageLabel.numberOfLines = 0

        var buttonConfig = UIButton.Configuration.filled()
        buttonConfig.title = "Thử lại / Retry"
        buttonConfig.baseBackgroundColor = .systemBlue
        buttonConfig.baseForegroundColor = .white
        buttonConfig.cornerStyle = .medium
        buttonConfig.contentInsets = NSDirectionalEdgeInsets(top: 12, leading: 32, bottom: 12, trailing: 32)
        let retryButton = UIButton(configuration: buttonConfig)
        retryButton.addTarget(self, action: #selector(retryConnection), for: .touchUpInside)

        stack.addArrangedSubview(imageView)
        stack.addArrangedSubview(titleLabel)
        stack.addArrangedSubview(messageLabel)
        stack.addArrangedSubview(retryButton)

        noNetworkView.addSubview(stack)
        view.addSubview(noNetworkView)

        NSLayoutConstraint.activate([
            noNetworkView.topAnchor.constraint(equalTo: view.topAnchor),
            noNetworkView.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            noNetworkView.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            noNetworkView.bottomAnchor.constraint(equalTo: view.bottomAnchor),
            stack.centerXAnchor.constraint(equalTo: noNetworkView.centerXAnchor),
            stack.centerYAnchor.constraint(equalTo: noNetworkView.centerYAnchor)
        ])
    }

    private func setupPullToRefresh() {
        let refreshControl = UIRefreshControl()
        refreshControl.addTarget(self, action: #selector(handleRefresh), for: .valueChanged)
        webView.scrollView.refreshControl = refreshControl
    }

    // MARK: - Network Monitoring

    private func startNetworkMonitoring() {
        networkMonitor.pathUpdateHandler = { [weak self] path in
            DispatchQueue.main.async {
                guard let self else { return }
                let wasConnected = self.isConnected
                self.isConnected = path.status == .satisfied

                if self.isFirstPathUpdate {
                    self.isFirstPathUpdate = false
                    if self.isConnected {
                        if self.pendingNotificationTap {
                            self.startWebView()
                        } else {
                            self.showStartupNotice()
                        }
                    } else {
                        self.showNoNetwork()
                    }
                    return
                }

                if self.isConnected && !wasConnected {
                    if self.webView != nil {
                        self.hideNoNetwork()
                        // Trang đầu chưa tải được lúc mất mạng (webView.url == nil) -> có mạng lại phải TẢI LẠI, nếu không
                        // chỉ bỏ lớp "không có mạng" và để lộ WebView trắng trơn. Đã có trang thì giữ nguyên, không reload
                        // (tránh mất dữ liệu đang nhập mỗi lần wifi<->4G chớp).
                        if self.webView.url == nil { self.loadWebApp() }
                    } else if self.overlayView == nil {
                        self.noNetworkView.isHidden = true
                        if self.pendingNotificationTap {
                            self.startWebView()
                        } else {
                            self.showStartupNotice()
                        }
                    }
                } else if !self.isConnected {
                    self.showNoNetwork()
                }
            }
        }
        networkMonitor.start(queue: .global(qos: .background))
    }

    private func showNoNetwork() {
        diagLog("showNoNetwork isConnected=\(isConnected)")
        noNetworkView.isHidden = false
        webView?.isHidden = true
        dismissSplash()
    }

    // MARK: - Splash

    /// Mốc thời gian hoạt ảnh mở màn chạy xong — giữ splash lại tối thiểu bằng đó để khỏi
    /// "chớp" mất nửa chừng khi trang web tải nhanh hơn hoạt ảnh.
    private var splashIntroEndsAt: Date = .distantPast
    /// Chỉ chạy hoạt ảnh "icon nở thành bảng" ở lần đầu — bấm "Thử lại" khi mất mạng thì hiện
    /// bảng ngay, đừng bắt xem lại hoạt ảnh.
    private var didMorphIconIntoCard = false
    /// Vị trí + cỡ chữ S lúc kết thúc bước 1 — bước 2 mọc tiếp từ ĐÚNG chỗ đó ra, nên hai bước
    /// nối vào nhau chứ không phải hai hoạt ảnh rời.
    private var splashSeedFrame: CGRect = .zero

    // Bảng màu logo Samho, lấy mẫu từ chính file logo và trang Đăng nhập.
    private static let logoCrimson = UIColor(red: 0.70, green: 0.12, blue: 0.22, alpha: 1)  // #B31E38
    private static let brandRed    = UIColor(red: 0.85, green: 0.22, blue: 0.20, alpha: 1)  // #DA3832 — chữ "My"
    private static let brandNavy   = UIColor(red: 0.13, green: 0.16, blue: 0.23, alpha: 1)  // #20293A — chữ "SAMHO"

    // Tỉ lệ đặt chữ S bên trong vòng — đo trực tiếp từ file logo gốc của công ty
    // ("TO BỰ NHẤT.png"), không ước lượng bằng mắt.
    private static let sInRing = CGRect(x: 0.2275, y: 0.1916, width: 0.5540, height: 0.5973)
    private static let ringRatio: CGFloat = 2724.0 / 3231.0      // vòng hơi dẹt, không phải tròn đều

    // ── Màn chào mở app (mẫu duyệt 2026-11-20, khớp loading web) ──────────────────────────
    // Khối đỏ chữ S (icon app) đứng ngay giữa → chữ S tự viết lại → logo "thở" + ánh đỏ toả → tên
    // My SAMHO trượt lên → thoát: icon phóng nhẹ + mờ, trang app hiện ra từ phía sau (mờ dần + 97%→100%).
    // Thay chuỗi cũ icon→đĩa đen→vòng→vòng đỏ (6 bước, 2 giây, bay logo tới chỗ logo trang Đăng nhập —
    // người đã đăng nhập vào thẳng Trang chủ nên logo bay tới chỗ trống).
    private static let splashIconSide: CGFloat = 108
    private static let splashIconLift: CGFloat = 24     // icon nằm hơi cao hơn giữa để chừa chỗ cho tên
    private static let easeOutSoft = CAMediaTimingFunction(controlPoints: 0.32, 0.72, 0, 1)

    /// Đường chữ S trong hệ toạ độ 1024×1024 (giống hệt SVG của loading web).
    private static func splashSPath(side: CGFloat) -> UIBezierPath {
        let k = side / 1024
        func p(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: x * k, y: y * k) }
        let path = UIBezierPath()
        path.move(to: p(690, 238))
        path.addCurve(to: p(410, 315), controlPoint1: p(600, 200), controlPoint2: p(430, 205))
        path.addCurve(to: p(540, 474), controlPoint1: p(395, 400), controlPoint2: p(470, 440))
        path.addCurve(to: p(650, 640), controlPoint1: p(625, 515), controlPoint2: p(668, 560))
        path.addCurve(to: p(300, 690), controlPoint1: p(628, 740), controlPoint2: p(425, 772))
        return path
    }

    private func showSplash() {
        // startWebView() cũng gọi hàm này — mở app bình thường sẽ gọi 2 lần. Lần sau bỏ qua để
        // splash chạy LIỀN MẠCH từ lúc mở tới lúc trang tải xong, không dựng chồng 2 lớp.
        guard splashView == nil else { return }

        let splash = UIView(frame: view.bounds)
        splash.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        splash.backgroundColor = .white

        let side = ViewController.splashIconSide
        let cx = view.bounds.width / 2, cy = view.bounds.height / 2 - ViewController.splashIconLift

        // Ánh đỏ toả phía sau icon
        let glow = CAGradientLayer()
        glow.type = .radial
        glow.colors = [UIColor(red: 0.78, green: 0.09, blue: 0.23, alpha: 0.30).cgColor, UIColor(red: 0.78, green: 0.09, blue: 0.23, alpha: 0).cgColor]
        glow.startPoint = CGPoint(x: 0.5, y: 0.5); glow.endPoint = CGPoint(x: 1, y: 1)
        glow.frame = CGRect(x: cx - side, y: cy - side, width: side * 2, height: side * 2)
        glow.opacity = 0
        splash.layer.addSublayer(glow)

        // Khối đỏ bo góc (giống icon app) + chữ S trắng vẽ bằng nét để "tự viết lại"
        let icon = UIView(frame: CGRect(x: cx - side / 2, y: cy - side / 2, width: side, height: side))
        icon.layer.cornerRadius = side * 0.225
        icon.layer.cornerCurve = .continuous
        icon.clipsToBounds = true
        let bg = CAGradientLayer()
        bg.type = .radial
        bg.colors = [UIColor(red: 0.78, green: 0.09, blue: 0.23, alpha: 1).cgColor,
                     UIColor(red: 0.61, green: 0.04, blue: 0.14, alpha: 1).cgColor,
                     UIColor(red: 0.42, green: 0.02, blue: 0.09, alpha: 1).cgColor]
        bg.locations = [0, 0.55, 1]
        bg.startPoint = CGPoint(x: 0.3, y: 0.28); bg.endPoint = CGPoint(x: 1.15, y: 1.15)
        bg.frame = icon.bounds
        icon.layer.addSublayer(bg)
        let sLayer = CAShapeLayer()
        sLayer.path = ViewController.splashSPath(side: side).cgPath
        sLayer.fillColor = UIColor.clear.cgColor
        sLayer.strokeColor = UIColor.white.cgColor
        sLayer.lineWidth = 96 * side / 1024
        sLayer.lineCap = .round; sLayer.lineJoin = .round
        icon.layer.addSublayer(sLayer)
        splash.addSubview(icon)

        // "My SAMHO" + "HR PORTAL"
        let name = UILabel(frame: CGRect(x: 0, y: cy + side / 2 + 18, width: view.bounds.width, height: 32))
        name.autoresizingMask = [.flexibleWidth]
        let t = NSMutableAttributedString(string: "My ", attributes: [.foregroundColor: ViewController.brandRed, .font: UIFont.systemFont(ofSize: 24, weight: .bold)])
        t.append(NSAttributedString(string: "SAMHO", attributes: [.foregroundColor: ViewController.brandNavy, .font: UIFont.systemFont(ofSize: 24, weight: .heavy)]))
        name.attributedText = t
        name.textAlignment = .center
        name.alpha = 0
        name.transform = CGAffineTransform(translationX: 0, y: 10)
        splash.addSubview(name)
        let sub = UILabel(frame: CGRect(x: 0, y: name.frame.maxY + 2, width: view.bounds.width, height: 16))
        sub.autoresizingMask = [.flexibleWidth]
        sub.attributedText = NSAttributedString(string: "HR PORTAL", attributes: [.foregroundColor: UIColor(red: 0.58, green: 0.64, blue: 0.72, alpha: 1), .font: UIFont.systemFont(ofSize: 11, weight: .bold), .kern: 2.0])
        sub.textAlignment = .center
        sub.alpha = 0
        splash.addSubview(sub)

        // Đặt DƯỚI bảng thông báo đang co lại — bảng co về đúng khối icon này rồi mờ đi.
        if let ov = overlayView { view.insertSubview(splash, belowSubview: ov) } else { view.addSubview(splash) }
        splashView = splash
        splashGroup = icon
        didDismissSplash = false
        // Có bảng thông báo đang co lại → đợi nó co xong mới chạy (icon đã nằm sẵn đúng chỗ bên dưới)
        let start: CFTimeInterval = overlayView != nil ? 0.5 : 0

        // 1. Chữ S xoá rồi tự viết lại (0.1s → 0.72s)
        let t0 = CACurrentMediaTime() + start + 0.1
        let sStart = CAKeyframeAnimation(keyPath: "strokeStart")
        sStart.values = [0, 1, 0, 0]; sStart.keyTimes = [0, 0.35, 0.351, 1]
        let sEnd = CAKeyframeAnimation(keyPath: "strokeEnd")
        sEnd.values = [1, 1, 0, 1]; sEnd.keyTimes = [0, 0.35, 0.351, 1]
        sEnd.timingFunctions = [CAMediaTimingFunction(name: .easeIn), CAMediaTimingFunction(name: .linear),
                                CAMediaTimingFunction(controlPoints: 0.65, 0, 0.35, 1)]
        sStart.timingFunctions = sEnd.timingFunctions
        let draw = CAAnimationGroup()
        draw.animations = [sStart, sEnd]; draw.duration = 0.62; draw.beginTime = t0
        draw.fillMode = .both; draw.isRemovedOnCompletion = true
        sLayer.add(draw, forKey: "draw")

        // 2. Logo "thở" + ánh đỏ toả (0.55s)
        UIView.animateKeyframes(withDuration: 0.9, delay: start + 0.55, options: [.calculationModeCubic]) {
            UIView.addKeyframe(withRelativeStartTime: 0, relativeDuration: 0.4) { icon.transform = CGAffineTransform(scaleX: 1.06, y: 1.06) }
            UIView.addKeyframe(withRelativeStartTime: 0.4, relativeDuration: 0.6) { icon.transform = .identity }
        }
        let g = CAKeyframeAnimation(keyPath: "opacity")
        g.values = [0, 1, 0]; g.keyTimes = [0, 0.4, 1]
        let gs = CABasicAnimation(keyPath: "transform.scale"); gs.fromValue = 0.8; gs.toValue = 1.25
        let glowAnim = CAAnimationGroup(); glowAnim.animations = [g, gs]; glowAnim.duration = 1.1
        glowAnim.beginTime = CACurrentMediaTime() + start + 0.55; glowAnim.timingFunction = CAMediaTimingFunction(name: .easeOut)
        glow.add(glowAnim, forKey: "glow")

        // 3. Tên trượt lên (0.75s), dòng HR PORTAL hiện mờ (0.95s)
        UIView.animate(withDuration: 0.5, delay: start + 0.75, usingSpringWithDamping: 0.9, initialSpringVelocity: 0, options: []) {
            name.alpha = 1; name.transform = .identity
        }
        UIView.animate(withDuration: 0.5, delay: start + 0.95, options: [.curveEaseOut]) { sub.alpha = 1 }

        splashIntroEndsAt = Date().addingTimeInterval(start + 1.5)
    }

    // Chỉ tắt 1 lần — gọi lại (vd didFinish bắn nhiều lần khi điều hướng trang khác sau Home) không
    // làm gì vì splashView đã nil.
    private func dismissSplash() {
        guard !didDismissSplash, let splash = splashView else { return }

        // Trang web tải xong trước khi hoạt ảnh chạy hết -> chờ nốt rồi mới tắt, đừng cắt ngang.
        let remain = splashIntroEndsAt.timeIntervalSinceNow
        if remain > 0 {
            DispatchQueue.main.asyncAfter(deadline: .now() + remain) { [weak self] in
                self?.dismissSplash()
            }
            return
        }
        didDismissSplash = true

        // Thoát: icon phóng nhẹ + mờ, màn trắng mờ đi, TRANG APP hiện ra từ phía sau (mờ dần + 97%→100%)
        // — đúng với mọi trang đích (Đăng nhập hay Trang chủ), không bay logo tới chỗ nào cả.
        if let wv = webView {
            wv.alpha = 0
            wv.transform = CGAffineTransform(scaleX: 0.97, y: 0.97)
            UIView.animate(withDuration: 0.5, delay: 0.05, usingSpringWithDamping: 1, initialSpringVelocity: 0, options: []) {
                wv.alpha = 1; wv.transform = .identity
            }
        }
        UIView.animate(withDuration: 0.45, delay: 0, usingSpringWithDamping: 1, initialSpringVelocity: 0, options: []) {
            self.splashGroup?.transform = CGAffineTransform(scaleX: 1.35, y: 1.35)
            self.splashGroup?.alpha = 0
        }
        UIView.animate(withDuration: 0.45, delay: 0, options: [.curveEaseOut], animations: {
            splash.alpha = 0
        }, completion: { _ in
            splash.removeFromSuperview()
            self.splashView = nil
        })
        splashView = nil
    }

    private func hideNoNetwork() {
        noNetworkView.isHidden = true
        webView?.isHidden = false
    }

    // MARK: - Web Loading

    /// Đường dẫn tương đối cần mở THẲNG khi vào app (vd "/Leave/LeaveMyRequests").
    /// Dùng chung cho nút tắt giữ icon app và cho thông báo đẩy muốn nhảy đúng màn hình.
    static var pendingOpenPath: String?

    private func loadWebApp() {
        // Mở thẳng trang đích, không ghé trang chủ rồi mới nhảy — tránh nháy 2 lần và tránh tải
        // thừa cả trang chủ trên mạng xưởng vốn đã chậm.
        let target: String
        if let path = ViewController.pendingOpenPath {
            ViewController.pendingOpenPath = nil
            target = appURL + path
        } else {
            target = pendingNotificationTap ? notificationURL() : appURL
        }
        guard let url = URL(string: target) else { return }
        diagLog("loadWebApp -> \(target)")
        webView.load(URLRequest(url: url))
    }

    /// App đang chạy sẵn, người dùng bấm nút tắt -> điều hướng ngay.
    func openPath(_ path: String) {
        guard let url = URL(string: appURL + path) else { return }
        if webView != nil {
            webView.load(URLRequest(url: url))
        } else {
            ViewController.pendingOpenPath = path
        }
    }

    /// Địa chỉ nhảy THẲNG tới màn hình của thông báo vừa bấm.
    ///
    /// Bảng đổi mã -> trang nằm ở server (NotificationController.Go bên HR_web), app chỉ ghép tham
    /// số. Chép bảng đó sang cả iOS lẫn Android nữa là thành 3 bản, sửa 1 chỗ quên 2 chỗ; mà app
    /// đã phát hành rồi thì không sửa lại được ngay.
    private func notificationURL() -> String {
        guard let action = AppDelegate.pendingNotiAction, !action.isEmpty else {
            // Không có mã (thông báo cũ gửi trước bản này) -> về hộp thư như trước.
            if let id = pendingNotiId, !id.isEmpty { return "\(appURL)/Notification?open=\(id)" }
            return "\(appURL)/Notification"
        }
        func esc(_ v: String) -> String {
            v.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? v
        }
        var url = "\(appURL)/Notification/Go?action=\(esc(action))"
        if let t = AppDelegate.pendingNotiTarget, !t.isEmpty { url += "&target=\(esc(t))" }
        if let id = pendingNotiId, !id.isEmpty             { url += "&noti=\(esc(id))" }
        AppDelegate.pendingNotiAction = nil
        AppDelegate.pendingNotiTarget = nil
        return url
    }

    // MARK: - Actions

    @objc private func retryConnection() {
        guard isConnected else { return }
        if webView == nil {
            noNetworkView.isHidden = true
            showStartupNotice()
        } else {
            hideNoNetwork()
            if webView.url == nil {
                loadWebApp()
            } else {
                webView.reload()
            }
        }
    }

    @objc private func handleRefresh(_ sender: UIRefreshControl) {
        webView.reload()
        // Đừng end ngay — để didFinish/didFail callback kết thúc spinner.
        // Safety timeout 5s tránh kẹt spinner nếu navigation không fire callback.
        DispatchQueue.main.asyncAfter(deadline: .now() + 5) { [weak sender] in
            sender?.endRefreshing()
        }
    }

    @objc private func handleNotificationTap() {
        diagLog("handleNotificationTap webViewNil=\(webView == nil) url=\(webView?.url?.absoluteString ?? "nil") hidden=\(webView?.isHidden ?? false)")
        pendingNotiId = AppDelegate.pendingNotiId
        AppDelegate.pendingNotiId = nil
        if webView != nil {
            let url = URL(string: notificationURL())!
            pendingNotiId = nil
            webView.load(URLRequest(url: url))
        } else {
            pendingNotificationTap = true
        }
    }

    // MARK: - Khôi phục màn hình trắng
    //
    // iOS có thể giải phóng tiến trình WebView của app đang nằm nền (nhất là khi máy đang chạy Facebook/Zalo...). Lúc đó
    // WKWebView giữ nguyên khung nhưng NỘI DUNG TRẮNG và không tự tải lại — nhân viên bấm vào thông báo, app mở ra màn trắng
    // (báo 2026-10-28). Có 2 đường vào: iOS báo trực tiếp (webViewWebContentProcessDidTerminate) hoặc kiểm tra lại khi app
    // quay về foreground (webView.url == nil).
    fileprivate func recoverBlankWebView() {
        guard let webView = webView, isConnected else { return }
        noNetworkView.isHidden = true
        webView.isHidden = false
        if pendingNotificationTap || webView.url == nil {
            loadWebApp()       // có thông báo đang chờ -> vào đúng trang Thông báo, không thì về trang chủ
        } else {
            webView.reload()
        }
    }

    @objc private func handleWillEnterForeground() {
        diagLog("willEnterForeground url=\(webView?.url?.absoluteString ?? "nil") hidden=\(webView?.isHidden ?? false)")
        guard let webView = webView, webView.url == nil, isConnected else { return }
        recoverBlankWebView()
    }

    // Push đến lúc app đang mở (không bấm vào noti) — báo web tự refresh badge số (sidebar/bottom-nav).
    @objc private func handleForegroundPush() {
        callWeb("onNativeNotificationReceived", "")
    }
}

// MARK: - App version check (bắt cập nhật nếu version hiện tại < min trên server)
//
// Android đã có sẵn cơ chế này (checkAppVersion/showForceUpdateDialog trong MainActivity.java) —
// iOS trước đây CHƯA có, nên admin đổi MinVersion bên server (apiHR/AppVersion/check, config trong
// appsettings.json, đọc lại ngay không cần restart server) chỉ ép được máy Android cập nhật, máy iOS
// vẫn lọt (báo lỗi thật 2026-10-26: "iOS cho cách nào tự động cập nhật không"). Port y hệt logic
// Android sang đây cho đồng bộ 2 nền tảng.
extension ViewController {
    fileprivate func checkAppVersion() {
        let currentVersion = (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String) ?? "0.0"
        // appURL dạng "https://hr.esamho.com/HR_Web" -> đổi sang "https://hr.esamho.com/HR_api/apiHR"
        let apiBase = appURL.replacingOccurrences(of: "/HR_Web", with: "/HR_api/apiHR")
        var urlStr = "\(apiBase)/AppVersion/check?platform=ios&version=\(currentVersion)"
        // Chưa có empcd (chưa đăng nhập xong/web chưa tải) -> vẫn check được, chỉ là updateUrl rơi về
        // link cấu hình chung (appsettings.json) thay vì link riêng của NV — gọi lại lần nữa ngay khi
        // web báo setEmpcd để có link chính xác.
        if let empcd = ViewController.currentEmpcd, !empcd.isEmpty {
            urlStr += "&empcd=\(empcd.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? empcd)"
        }
        guard let url = URL(string: urlStr) else { return }

        var request = URLRequest(url: url, timeoutInterval: 10)
        request.httpMethod = "GET"
        request.setValue("ac36484900c47f33feb65c507a7706ccf1bb0d22f18a13f9aea117a2a21e6dc9", forHTTPHeaderField: "X-Api-Key")
        // Gọi thẳng bằng URLSession (không qua WKWebView) nên KHÔNG tự mang UA đã sửa ở setupWebView()
        // — vẫn là UA mặc định của URLSession (không có "Mozilla/5.0..."), nghi cũng bị chặn/treo bởi
        // cùng rule WAF đã phát hiện hôm 2026-10-30 (lần này thành timeout thay vì "Forbidden" — log
        // thật 2026-10-30: AppVersion/check timeout -1001 ngay lúc app khởi động). Gắn UA dạng trình
        // duyệt + hậu tố riêng, đồng bộ với WKWebView, để không bị lọc theo UA. Tăng timeout 5s->10s
        // luôn vì request này rơi đúng lúc WebContent/GPU process còn đang khởi động nặng máy (log
        // "WebContent process took 1.2s to launch") — dễ timeout oan dù mạng bình thường.
        request.setValue(
            "Mozilla/5.0 (iPhone; CPU iPhone OS like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148 MySamhoMobile/iOS",
            forHTTPHeaderField: "User-Agent")

        URLSession.shared.dataTask(with: request) { [weak self] data, _, error in
            guard error == nil, let data = data,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  json["success"] as? Bool == true,
                  json["forceUpdate"] as? Bool == true,
                  self?.hasShownForceUpdateAlert != true else { return }
            self?.hasShownForceUpdateAlert = true

            let latest = json["latestVersion"] as? String ?? "?"
            let note   = json["releaseNote"] as? String ?? "Vui lòng cập nhật để tiếp tục sử dụng."
            let updateUrl = json["updateUrl"] as? String ?? ""

            DispatchQueue.main.async {
                self?.showForceUpdateDialog(latestVersion: latest, note: note, updateUrl: updateUrl)
            }
        }.resume()
        // Không có mạng / API lỗi -> im lặng bỏ qua, không chặn user (giống Android).
    }

    fileprivate func showForceUpdateDialog(latestVersion: String, note: String, updateUrl: String) {
        // Trước đây có exit(0) ngay sau khi mở link — exit() chạy ngay lập tức trong khi
        // UIApplication.shared.open(url) là bất đồng bộ, dễ bị tắt app TRƯỚC KHI kịp mở xong trang
        // App Store (báo lỗi thật 2026-10-26: "nhấn cập nhật app tự thoát" — không rõ có mở được trang
        // cập nhật không). Bỏ hẳn exit(): mở link tự đẩy app ra nền, hệ thống tự hiện Safari/App Store
        // đè lên — không cần ép thoát, cũng tránh luôn rủi ro Apple không khuyến khích app tự gọi
        // exit(). Alert kiểu .alert vốn không tự tắt khi bấm ra ngoài -> vẫn chặn được, chỉ bỏ nút
        // "Thoát" (không còn tác dụng thật nếu không gọi exit()).
        let alert = UIAlertController(
            title: "Có bản cập nhật mới (v\(latestVersion))",
            message: note,
            preferredStyle: .alert
        )
        alert.addAction(UIAlertAction(title: "Cập nhật ngay", style: .default) { _ in
            if let url = URL(string: updateUrl) { UIApplication.shared.open(url) }
        })
        present(alert, animated: true)
    }
}

// MARK: - WKNavigationDelegate

extension ViewController: WKNavigationDelegate {

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        webView.scrollView.refreshControl?.endRefreshing()
        dismissSplash()
        diagLog("didFinish url=\(webView.url?.absoluteString ?? "nil") hidden=\(webView.isHidden)")
        webView.evaluateJavaScript("(document.body ? document.body.innerText.length : -1) + '|' + document.title") { r, e in
            diagLog("  page text len|title = \(String(describing: r)) err=\(String(describing: e))")
        }

        let token = AppDelegate.savedFcmToken() ?? ""
        let escaped = token.replacingOccurrences(of: "'", with: "\\'")
        let js = """
        if (!window.MysamhoBridge) { window.MysamhoBridge = {}; }
        window.MysamhoBridge.getFcmToken = function() { return '\(escaped)'; };
        """
        webView.evaluateJavaScript(js, completionHandler: nil)

        if pendingNotificationTap {
            pendingNotificationTap = false
            let currentURL = webView.url?.absoluteString ?? ""
            if !currentURL.contains("/Notification") {
                webView.load(URLRequest(url: URL(string: notificationURL())!))
            }
            pendingNotiId = nil
        }

        // Nhận diện trang Đăng nhập bằng CHÍNH NỘI DUNG TRANG, không dựa vào URL.
        // BUG 2026-11-13 (log máy thật): sau khi đăng xuất, server trả trang Đăng nhập ngay tại
        // https://hr.esamho.com/HR_Web — URL KHÔNG chứa "/Account/Login", nên cách so chuỗi cũ luôn
        // sai => không bao giờ hỏi Face ID. Dò ô nhập mật khẩu + form đăng nhập thì đúng trong mọi
        // trường hợp, không phụ thuộc đường dẫn, PathBase hay hoa/thường.
        let probe = "(document.querySelector('.login-body form') && document.getElementById('passwordInput')) ? 1 : 0"
        webView.evaluateJavaScript(probe) { [weak self] result, _ in
            guard let self = self else { return }
            let isLogin = ((result as? NSNumber)?.intValue ?? 0) == 1
            self.handleBiometricLoginState(webView: webView, isLoginPage: isLogin)
            self.injectBioState(webView: webView)
            // Đang ở trang đăng nhập thì chưa có phiên, chép cookie sang cũng vô nghĩa (server sẽ
            // trả 302). Chỉ đồng bộ khi đã vào được bên trong.
            if !isLogin {
                WidgetBridge.syncSession(from: webView, baseURL: self.appURL)
            }
        }
    }

    // Báo cho web biết TRẠNG THÁI THẬT của đăng nhập sinh trắc học trên máy này.
    // Trước đây web mù tịt: trang Cài đặt chỉ đọc lựa chọn trong localStorage nên luôn ghi "Đang bật"
    // kể cả khi máy chưa lưu mật khẩu nào -> NV bật xong, đăng xuất, không thấy Face ID và không
    // hiểu vì sao (báo lỗi 2026-11-13). Giờ web đọc window.__bioState để nói đúng tình trạng.
    private func injectBioState(webView: WKWebView) {
        let ctx = LAContext()
        var err: NSError?
        let canBio = ctx.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: &err)
        let hasCreds = KeychainCredentialHelper.hasStoredCredentials()
        let js = "window.__bioState={supported:true,canBiometric:\(canBio),hasCreds:\(hasCreds)};"
            + "window.dispatchEvent(new Event('biostate'));"
        webView.evaluateJavaScript(js, completionHandler: nil)
    }

    // Đăng nhập Face ID/Touch ID (xem KeychainCredentialHelper.swift) — tự xác nhận/huỷ pendingCredentials
    // dựa vào trang vừa tải xong có còn là Login không, và tự kích hoạt Face ID khi vừa landing lại
    // trang Login (session hết hạn) mà đã có credentials lưu sẵn.
    private func handleBiometricLoginState(webView: WKWebView, isLoginPage: Bool) {

        // BUG 2026-11-13: hasTriedBiometricLogin chỉ bật lên 1 lần rồi KHÔNG BAO GIỜ tắt, nên sau khi
        // NV bấm Đăng xuất và quay lại trang Login trong CÙNG lần mở app thì Face ID không hiện nữa —
        // phải tắt hẳn app mới hỏi lại. Đúng cách NV hay thử tính năng này nên ai cũng tưởng hỏng.
        // Rời khỏi trang Login (tức đã đăng nhập xong) thì cho phép hỏi lại ở lần quay về Login sau.
        if !isLoginPage { hasTriedBiometricLogin = false }

        bioLog("didFinish isLogin=\(isLoginPage) pending=\(pendingCredentials != nil) tried=\(hasTriedBiometricLogin) url=\(webView.url?.absoluteString ?? "nil")")
        if let pending = pendingCredentials {
            pendingCredentials = nil
            if !isLoginPage {
                // Vừa submit xong, điều hướng sang trang khác Login -> đăng nhập đúng -> lưu thật.
                KeychainCredentialHelper.save(pending)
            }
            // Vẫn còn ở Login -> đăng nhập sai -> KHÔNG lưu (tránh lưu nhầm mật khẩu sai).
            return
        }

        guard isLoginPage, !hasTriedBiometricLogin, KeychainCredentialHelper.hasStoredCredentials() else {
            bioLog("khong hoi sinh trac hoc (isLogin=\(isLoginPage) tried=\(hasTriedBiometricLogin) hasCreds=\(KeychainCredentialHelper.hasStoredCredentials()))")
            return
        }
        hasTriedBiometricLogin = true
        bioLog("dang bat Face ID...")

        KeychainCredentialHelper.readWithBiometrics(reason: "Đăng nhập My Samho") { [weak self] creds in
            guard let self = self, let creds = creds else { return }
            let empcdJs = ViewController.jsStringLiteral(creds.empcd)
            let pwdJs   = ViewController.jsStringLiteral(creds.password)
            let js = """
            (function(){
                var e=document.getElementById('EMPCD'), p=document.getElementById('passwordInput');
                var f=document.querySelector('.login-body form');
                if(e&&p&&f){ e.value=\(empcdJs); p.value=\(pwdJs); f.submit(); }
            })();
            """
            self.webView.evaluateJavaScript(js, completionHandler: nil)
        }
    }

    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        diagLog("webContentProcessDidTerminate")
        DispatchQueue.main.async { [weak self] in self?.recoverBlankWebView() }
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        webView.scrollView.refreshControl?.endRefreshing()
        diagLog("didFailProvisional code=\((error as NSError).code) \(error.localizedDescription)")
        // Điều hướng bị HỦY (-999, vd bấm thông báo lúc trang cũ đang tải -> load đè lên) KHÔNG phải mất mạng:
        // trước đây vẫn gọi showNoNetwork() làm ẩn WebView.
        if (error as NSError).code == NSURLErrorCancelled { return }
        showNoNetwork()
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        webView.scrollView.refreshControl?.endRefreshing()
        diagLog("didFail code=\((error as NSError).code) \(error.localizedDescription)")
        if (error as NSError).code == NSURLErrorCancelled { return }
        showNoNetwork()
    }

    func webView(
        _ webView: WKWebView,
        decidePolicyFor navigationResponse: WKNavigationResponse,
        decisionHandler: @escaping (WKNavigationResponsePolicy) -> Void
    ) {
        if let response = navigationResponse.response as? HTTPURLResponse,
           let contentDisposition = response.value(forHTTPHeaderField: "Content-Disposition"),
           contentDisposition.contains("attachment") {
            decisionHandler(.download)
            return
        }
        if !navigationResponse.canShowMIMEType {
            decisionHandler(.download)
            return
        }
        decisionHandler(.allow)
    }

    func webView(_ webView: WKWebView, navigationAction: WKNavigationAction, didBecome download: WKDownload) {
        download.delegate = self
    }

    func webView(_ webView: WKWebView, navigationResponse: WKNavigationResponse, didBecome download: WKDownload) {
        download.delegate = self
    }
}

// MARK: - WKUIDelegate

extension ViewController: WKUIDelegate {

    func webView(
        _ webView: WKWebView,
        createWebViewWith configuration: WKWebViewConfiguration,
        for navigationAction: WKNavigationAction,
        windowFeatures: WKWindowFeatures
    ) -> WKWebView? {
        if navigationAction.targetFrame == nil {
            webView.load(navigationAction.request)
        }
        return nil
    }

    func webView(
        _ webView: WKWebView,
        runJavaScriptAlertPanelWithMessage message: String,
        initiatedByFrame frame: WKFrameInfo,
        completionHandler: @escaping () -> Void
    ) {
        let alert = UIAlertController(title: nil, message: message, preferredStyle: .alert)
        alert.addAction(UIAlertAction(title: "OK", style: .default) { _ in completionHandler() })
        present(alert, animated: true)
    }

    func webView(
        _ webView: WKWebView,
        runJavaScriptConfirmPanelWithMessage message: String,
        initiatedByFrame frame: WKFrameInfo,
        completionHandler: @escaping (Bool) -> Void
    ) {
        let alert = UIAlertController(title: nil, message: message, preferredStyle: .alert)
        alert.addAction(UIAlertAction(title: "Hủy", style: .cancel) { _ in completionHandler(false) })
        alert.addAction(UIAlertAction(title: "OK", style: .default) { _ in completionHandler(true) })
        present(alert, animated: true)
    }

    func webView(
        _ webView: WKWebView,
        runJavaScriptTextInputPanelWithPrompt prompt: String,
        defaultText: String?,
        initiatedByFrame frame: WKFrameInfo,
        completionHandler: @escaping (String?) -> Void
    ) {
        let alert = UIAlertController(title: nil, message: prompt, preferredStyle: .alert)
        alert.addTextField { $0.text = defaultText }
        alert.addAction(UIAlertAction(title: "Hủy", style: .cancel) { _ in completionHandler(nil) })
        alert.addAction(UIAlertAction(title: "OK", style: .default) { _ in
            completionHandler(alert.textFields?.first?.text)
        })
        present(alert, animated: true)
    }
}

// MARK: - WKScriptMessageHandler (bridge web ↔ native)

extension ViewController: WKScriptMessageHandler {
    func userContentController(_ userContentController: WKUserContentController,
                               didReceive message: WKScriptMessage) {
        switch message.name {
        case "scanBarcode":
            presentBarcodeScanner()
        case "clearNotifications":
            UIApplication.shared.applicationIconBadgeNumber = 0
            UNUserNotificationCenter.current().removeAllDeliveredNotifications()
            UNUserNotificationCenter.current().removeAllPendingNotificationRequests()
        case "setEmpcd":
            if let empcd = message.body as? String, !empcd.isEmpty, empcd != ViewController.currentEmpcd {
                ViewController.currentEmpcd = empcd
                checkAppVersion()   // gọi lại với empcd để lấy đúng link App Store riêng của NV
            }
        case "saveCredentials":
            bioLog("web goi saveCredentials")
            if let dict = message.body as? [String: Any],
               let empcd = dict["empcd"] as? String, let pwd = dict["password"] as? String,
               !empcd.isEmpty, !pwd.isEmpty {
                pendingCredentials = KeychainCredentialHelper.Credentials(empcd: empcd, password: pwd)
            }
        case "clearCredentials":
            bioLog("web goi clearCredentials  <-- ai do xoa mat khau da luu")
            KeychainCredentialHelper.clear()
            pendingCredentials = nil
            // Đăng xuất -> xoá cookie + số liệu khỏi widget, không để người sau cầm máy thấy
            // số của người trước.
            WidgetBridge.clearSession()
        default: break
        }
    }
}

// MARK: - Quét mã vạch gốc (TPM)

extension ViewController {

    /// Web gọi: window.webkit.messageHandlers.scanBarcode.postMessage(null). Kết quả trả về trang qua
    /// window.onNativeBarcode(code) / onNativeBarcodeCancel() / onNativeBarcodeError(msg).
    fileprivate func presentBarcodeScanner() {
        guard presentedViewController == nil else { return }
        let scanner = BarcodeScannerViewController()
        scanner.modalPresentationStyle = .fullScreen
        scanner.onResult = { [weak self, weak scanner] code in
            scanner?.dismiss(animated: true) { self?.callWeb("onNativeBarcode", code) }
        }
        scanner.onCancel = { [weak self, weak scanner] in
            scanner?.dismiss(animated: true) { self?.callWeb("onNativeBarcodeCancel", "") }
        }
        scanner.onError = { [weak self, weak scanner] message in
            scanner?.dismiss(animated: true) { self?.callWeb("onNativeBarcodeError", message) }
        }
        present(scanner, animated: true)
    }

    fileprivate func callWeb(_ function: String, _ argument: String) {
        let quoted = ViewController.jsStringLiteral(argument)
        webView.evaluateJavaScript("if(window.\(function))window.\(function)(\(quoted));", completionHandler: nil)
    }

    // "x" -> "\"x\"" đã escape chuẩn JSON, an toàn chèn thẳng vào chuỗi JS evaluateJavaScript.
    fileprivate static func jsStringLiteral(_ s: String) -> String {
        guard let data = try? JSONSerialization.data(withJSONObject: [s]),
              let array = String(data: data, encoding: .utf8) else { return "\"\"" }
        return String(array.dropFirst().dropLast())   // ["x"] -> "x"
    }
}

// MARK: - WKDownloadDelegate

extension ViewController: WKDownloadDelegate {

    func download(
        _ download: WKDownload,
        decideDestinationUsing response: URLResponse,
        suggestedFilename: String,
        completionHandler: @escaping (URL?) -> Void
    ) {
        let fileURL = FileManager.default.temporaryDirectory.appendingPathComponent(suggestedFilename)
        try? FileManager.default.removeItem(at: fileURL)
        lastDownloadedFileURL = fileURL
        completionHandler(fileURL)
    }

    func downloadDidFinish(_ download: WKDownload) {
        guard let fileURL = lastDownloadedFileURL else { return }
        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            let activityVC = UIActivityViewController(activityItems: [fileURL], applicationActivities: nil)
            if let popover = activityVC.popoverPresentationController {
                popover.sourceView = self.view
                popover.sourceRect = CGRect(x: self.view.bounds.midX,
                                            y: self.view.bounds.midY,
                                            width: 0, height: 0)
                popover.permittedArrowDirections = []
            }
            self.present(activityVC, animated: true)
        }
    }

    func download(_ download: WKDownload, didFailWithError error: Error, resumeData: Data?) {
        DispatchQueue.main.async { [weak self] in
            let alert = UIAlertController(
                title: "Lỗi tải xuống",
                message: error.localizedDescription,
                preferredStyle: .alert
            )
            alert.addAction(UIAlertAction(title: "OK", style: .default))
            self?.present(alert, animated: true)
        }
    }
}

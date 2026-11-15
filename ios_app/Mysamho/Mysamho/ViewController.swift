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

        var btnConfig = UIButton.Configuration.filled()
        btnConfig.title = "Đã hiểu / Got it"
        btnConfig.baseBackgroundColor = .systemBlue
        btnConfig.baseForegroundColor = .white
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

        overlay.alpha = 0
        UIView.animate(withDuration: 0.3) { overlay.alpha = 1 }
    }

    private func dismissOverlay() {
        UIView.animate(withDuration: 0.2, animations: {
            self.overlayView?.alpha = 0
        }) { _ in
            self.overlayView?.removeFromSuperview()
            self.overlayView = nil
        }
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

    private func showSplash() {
        let splash = UIView(frame: view.bounds)
        splash.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        splash.backgroundColor = .white

        let logo = UIImageView(image: UIImage(named: "SplashLogo"))
        logo.contentMode = .scaleAspectFit
        logo.translatesAutoresizingMaskIntoConstraints = false
        logo.layer.cornerRadius = 24
        logo.clipsToBounds = true
        splash.addSubview(logo)

        NSLayoutConstraint.activate([
            logo.centerXAnchor.constraint(equalTo: splash.centerXAnchor),
            logo.centerYAnchor.constraint(equalTo: splash.centerYAnchor),
            logo.widthAnchor.constraint(equalToConstant: 120),
            logo.heightAnchor.constraint(equalToConstant: 120)
        ])

        view.addSubview(splash)
        splashView = splash
        didDismissSplash = false
    }

    // Chỉ tắt 1 lần — gọi lại (vd didFinish bắn nhiều lần khi điều hướng trang khác sau Home) không
    // làm gì vì splashView đã nil.
    private func dismissSplash() {
        guard !didDismissSplash, let splash = splashView else { return }
        didDismissSplash = true
        UIView.animate(withDuration: 0.25, animations: {
            splash.alpha = 0
        }, completion: { _ in
            splash.removeFromSuperview()
        })
        splashView = nil
    }

    private func hideNoNetwork() {
        noNetworkView.isHidden = true
        webView?.isHidden = false
    }

    // MARK: - Web Loading

    private func loadWebApp() {
        let target = pendingNotificationTap ? notificationURL() : appURL
        guard let url = URL(string: target) else { return }
        diagLog("loadWebApp -> \(target)")
        webView.load(URLRequest(url: url))
    }

    private func notificationURL() -> String {
        if let id = pendingNotiId, !id.isEmpty {
            return "\(appURL)/Notification?open=\(id)"
        }
        return "\(appURL)/Notification"
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

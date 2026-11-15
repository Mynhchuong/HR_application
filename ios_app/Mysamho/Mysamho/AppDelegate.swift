import UIKit
import UserNotifications
import FirebaseCore
import FirebaseMessaging

extension Notification.Name {
    static let didTapPushNotification = Notification.Name("didTapPushNotification")
    // Push đến LÚC APP ĐANG MỞ (foreground) — khác với didTapPushNotification (user đã bấm vào noti).
    // ViewController nghe cái này để gọi window.onNativeNotificationReceived() cho web tự refresh badge
    // ngay, không cần NV F5/chuyển trang mới thấy số mới (yêu cầu 2026-10-26).
    static let didReceiveForegroundPush = Notification.Name("didReceiveForegroundPush")
}

@main
class AppDelegate: UIResponder, UIApplicationDelegate {

    private static let tokenKey = "fcm_token"

    // Khi app bị kill và user tap notification → save flag để ViewController consume sau khi load
    static var pendingNotificationTap: Bool = false
    static var pendingNotiId: String?
    /// Mã LINK_ACTION + TARGET_VAL của thông báo vừa bấm — ghép vào /Notification/Go để nhảy
    /// THẲNG tới màn hình của nó thay vì đổ về hộp thư chung.
    static var pendingNotiAction: String?
    static var pendingNotiTarget: String?

    func application(
        _ application: UIApplication,
        didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?
    ) -> Bool {
        diagLog("didFinishLaunching launchOptions=\(String(describing: launchOptions?.keys.map { $0.rawValue }))")
        FirebaseApp.configure()

        Messaging.messaging().delegate = self
        UNUserNotificationCenter.current().delegate = self

        // Detect launch from notification tap (app was killed)
        if let notiOptions = launchOptions?[.remoteNotification] as? [AnyHashable: Any],
           notiOptions["link_action"] != nil {
            AppDelegate.pendingNotificationTap = true
            AppDelegate.pendingNotiId     = notiOptions["noti_id"] as? String
            AppDelegate.pendingNotiAction = notiOptions["link_action"] as? String
            AppDelegate.pendingNotiTarget = notiOptions["target"] as? String
        }

        #if DEBUG
        // Công tắc chẩn đoán (chỉ Debug): mô phỏng "app bị tắt hẳn rồi mở bằng cách bấm thông báo" khi chạy với DIAG_COLD_TAP=<noti_id>.
        if let id = ProcessInfo.processInfo.environment["DIAG_COLD_TAP"] {
            AppDelegate.pendingNotificationTap = true
            AppDelegate.pendingNotiId = id
            diagLog("DIAG_COLD_TAP simulated id=\(id)")
        }
        #endif

        UNUserNotificationCenter.current().requestAuthorization(
            options: [.alert, .badge, .sound]
        ) { granted, _ in
            guard granted else { return }
            DispatchQueue.main.async {
                application.registerForRemoteNotifications()
            }
        }
        return true
    }

    func application(
        _ application: UIApplication,
        didRegisterForRemoteNotificationsWithDeviceToken deviceToken: Data
    ) {
        Messaging.messaging().apnsToken = deviceToken
    }

    func application(
        _ application: UIApplication,
        didFailToRegisterForRemoteNotificationsWithError error: Error
    ) {
        print("APNs registration failed: \(error)")
    }

    static func savedFcmToken() -> String? {
        UserDefaults.standard.string(forKey: tokenKey)
    }

    // MARK: - UISceneSession Lifecycle

    func application(
        _ application: UIApplication,
        configurationForConnecting connectingSceneSession: UISceneSession,
        options: UIScene.ConnectionOptions
    ) -> UISceneConfiguration {
        return UISceneConfiguration(name: "Default Configuration", sessionRole: connectingSceneSession.role)
    }

    func application(
        _ application: UIApplication,
        didDiscardSceneSessions sceneSessions: Set<UISceneSession>
    ) {
    }
}

// MARK: - MessagingDelegate

extension AppDelegate: MessagingDelegate {
    func messaging(_ messaging: Messaging, didReceiveRegistrationToken fcmToken: String?) {
        guard let token = fcmToken else { return }
        UserDefaults.standard.set(token, forKey: AppDelegate.tokenKey)
    }
}

// MARK: - UNUserNotificationCenterDelegate

extension AppDelegate: UNUserNotificationCenterDelegate {

    // Hiển thị notification khi app đang mở (foreground)
    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        willPresent notification: UNNotification,
        withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void
    ) {
        NotificationCenter.default.post(name: .didReceiveForegroundPush, object: nil)
        // Có thông báo mới = có thể vừa có đơn được duyệt / đơn mới cần duyệt -> vẽ lại widget.
        WidgetBridge.reloadAfterPush()
        completionHandler([.banner, .sound, .badge])
    }

    // User tap vào notification → navigate đến trang Notification
    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        didReceive response: UNNotificationResponse,
        withCompletionHandler completionHandler: @escaping () -> Void
    ) {
        let userInfo = response.notification.request.content.userInfo
        diagLog("didReceive response keys=\(userInfo.keys.map { "\($0)" }) link_action=\(String(describing: userInfo["link_action"]))")
        if userInfo["link_action"] != nil {
            // Lưu flag cho trường hợp ViewController chưa load (cold launch from killed)
            AppDelegate.pendingNotificationTap = true
            AppDelegate.pendingNotiId     = userInfo["noti_id"] as? String
            AppDelegate.pendingNotiAction = userInfo["link_action"] as? String
            AppDelegate.pendingNotiTarget = userInfo["target"] as? String
            NotificationCenter.default.post(name: .didTapPushNotification, object: nil)
        }
        completionHandler()
    }
}


// Nhật ký chẩn đoán (CHỈ bản Debug) — ghi Documents/diag.log để lấy từ máy bằng devicectl khi cần dò lỗi màn hình trắng
// khi bấm thông báo (báo 2026-10-28). Bản Release không ghi gì.
// Log chẩn đoán luồng đăng nhập sinh trắc học. Giữ lại vì 2 lỗi nặng nhất (Keychain trả -25300,
// URL trang Đăng nhập không chứa "/Account/Login") CHỈ lộ ra khi chạy máy thật và in log ra —
// đọc code không thể thấy. #if DEBUG để bản phát hành không in gì.
func bioLog(_ msg: String) {
    #if DEBUG
    print("BIO: " + msg)
    #endif
}

func diagLog(_ msg: String) {
    #if DEBUG
    let df = DateFormatter(); df.dateFormat = "HH:mm:ss.SSS"
    let line = "\(df.string(from: Date())) \(msg)\n"
    guard let dir = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first else { return }
    let url = dir.appendingPathComponent("diag.log")
    if let h = try? FileHandle(forWritingTo: url) {
        h.seekToEndOfFile(); h.write(line.data(using: .utf8)!); try? h.close()
    } else {
        try? line.write(to: url, atomically: true, encoding: .utf8)
    }
    #endif
}

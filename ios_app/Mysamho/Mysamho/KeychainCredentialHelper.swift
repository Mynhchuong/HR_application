import Foundation
import Security
import LocalAuthentication

/// Lưu mã NV + mật khẩu trong iOS Keychain để tự đăng nhập lại qua Face ID/Touch ID — session web hết
/// hạn cứng sau 24h (Program.cs, SlidingExpiration=false) nên NV phải gõ tay cả mã NV lẫn mật khẩu
/// MỖI NGÀY (yêu cầu 2026-10-26). Dùng Keychain (không phải UserDefaults) vì đây là nơi iOS khuyến
/// nghị cho credential — mã hoá phần cứng, đọc ra bắt buộc qua xác thực sinh trắc học
/// (`kSecAccessControlBiometryCurrentSet`: đổi vân tay/khuôn mặt đăng ký trên máy thì entry cũ tự
/// không đọc được nữa, đúng ý định bảo mật).
enum KeychainCredentialHelper {
    private static let service = "vn.esamho.hrapp.credentials"
    private static let account = "mysamho_login"

    struct Credentials {
        let empcd: String
        let password: String
    }

    // MARK: - Save (ghi KHÔNG cần sinh trắc học — chỉ ĐỌC mới cần, giống Keychain của Safari)

    static func save(_ creds: Credentials) {
        guard let data = try? JSONEncoder().encode(["empcd": creds.empcd, "password": creds.password]) else { return }

        // Máy KHÔNG đặt khoá màn hình -> không tạo được ràng buộc sinh trắc học. Trước đây vẫn lưu tiếp
        // (bỏ qua access control) nghĩa là mật khẩu nằm trong Keychain mà KHÔNG có gì khoá — sai.
        // Giờ không tạo được thì KHÔNG LƯU; dù sao máy đó cũng không dùng được Face ID/vân tay.
        guard let access = SecAccessControlCreateWithFlags(
            nil, kSecAttrAccessibleWhenPasscodeSetThisDeviceOnly, .biometryCurrentSet, nil) else { return }

        let query: [String: Any] = [
            kSecClass as String:            kSecClassGenericPassword,
            kSecAttrService as String:      service,
            kSecAttrAccount as String:      account,
        ]
        SecItemDelete(query as CFDictionary)   // ghi đè — xoá entry cũ trước nếu có

        var addQuery = query
        addQuery[kSecValueData as String] = data
        addQuery[kSecAttrAccessControl as String] = access
        // Trước đây bỏ qua kết quả -> lưu hỏng mà không ai biết (truy bug 2026-11-13).
        let addStatus = SecItemAdd(addQuery as CFDictionary, nil)
        UserDefaults.standard.set(addStatus == errSecSuccess, forKey: savedFlagKey)
        bioLog("save SecItemAdd status=\(addStatus)")
    }

    static func clear() {
        let query: [String: Any] = [
            kSecClass as String:       kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
        SecItemDelete(query as CFDictionary)
        UserDefaults.standard.set(false, forKey: savedFlagKey)
    }

    // Cờ "đã lưu" lưu riêng ở UserDefaults — xem hasStoredCredentials() bên dưới để biết vì sao.
    private static let savedFlagKey = "bio_credentials_saved"

    // Có credential đã lưu không.
    //
    // BUG 2026-11-13 (tìm ra bằng log trên máy thật): bản cũ hỏi Keychain với
    // kSecUseAuthenticationUISkip, tưởng rằng entry cần sinh trắc học sẽ trả errSecInteractionNotAllowed.
    // THỰC TẾ iOS trả errSecItemNotFound (-25300) — entry gắn .biometryCurrentSet bị LOẠI KHỎI kết quả
    // tìm kiếm khi yêu cầu bỏ qua giao diện xác thực. Log thật:
    //     BIO: save SecItemAdd status=0          <- lưu THÀNH CÔNG
    //     BIO: hasStoredCredentials status=-25300 <- ngay sau đó lại báo KHÔNG CÓ
    // Hậu quả: guard ở ViewController luôn thất bại -> KHÔNG BAO GIỜ hỏi Face ID, dù đã lưu đúng.
    //
    // Nên không dùng kết quả Keychain làm căn cứ nữa. Ghi 1 cờ boolean lúc lưu/xoá thành công và đọc cờ
    // đó. Cờ không chứa thông tin nhạy cảm (chỉ true/false); mật khẩu vẫn nằm trong Keychain và vẫn bắt
    // buộc Face ID/vân tay mới đọc ra được.
    static func hasStoredCredentials() -> Bool {
        return UserDefaults.standard.bool(forKey: savedFlagKey)
    }

    // MARK: - Read (bắt buộc xác thực sinh trắc học để lấy được data)

    static func readWithBiometrics(reason: String, completion: @escaping (Credentials?) -> Void) {
        let context = LAContext()
        var authError: NSError?
        guard context.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: &authError) else {
            completion(nil)
            return
        }

        context.evaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, localizedReason: reason) { success, _ in
            guard success else { completion(nil); return }

            let query: [String: Any] = [
                kSecClass as String:            kSecClassGenericPassword,
                kSecAttrService as String:      service,
                kSecAttrAccount as String:      account,
                kSecReturnData as String:       true,
                kSecUseAuthenticationContext as String: context,
            ]
            var item: CFTypeRef?
            let status = SecItemCopyMatching(query as CFDictionary, &item)
            guard status == errSecSuccess,
                  let data = item as? Data,
                  let dict = try? JSONDecoder().decode([String: String].self, from: data),
                  let empcd = dict["empcd"], let pwd = dict["password"] else {
                DispatchQueue.main.async { completion(nil) }
                return
            }
            DispatchQueue.main.async { completion(Credentials(empcd: empcd, password: pwd)) }
        }
    }
}

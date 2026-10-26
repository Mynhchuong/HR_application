package com.samho.mysamho;

import android.content.Context;
import android.content.SharedPreferences;

import androidx.security.crypto.EncryptedSharedPreferences;
import androidx.security.crypto.MasterKeys;

import java.io.IOException;
import java.security.GeneralSecurityException;

/**
 * Lưu mã NV + mật khẩu để tự đăng nhập lại qua vân tay/Face Unlock — session web hết hạn cứng sau
 * 24h (Program.cs, SlidingExpiration=false) nên NV phải gõ tay cả mã NV lẫn mật khẩu MỖI NGÀY
 * (yêu cầu 2026-10-26). Dùng {@link EncryptedSharedPreferences} (mã hoá bằng key lưu trong Android
 * Keystore, phần cứng) thay vì SharedPreferences thường — không phải để "khoá bằng sinh trắc học"
 * (Android không có flag tương đương biometryCurrentSet của iOS Keychain), mà việc ĐỌC ra chỉ được
 * gọi SAU KHI {@link androidx.biometric.BiometricPrompt} xác thực thành công (xem MainActivity.java),
 * giữ đúng tinh thần "phải vân tay mới lấy được mật khẩu đã lưu".
 */
final class BiometricCredentialHelper {

    private static final String PREF_NAME  = "mysamho_credentials";
    private static final String KEY_EMPCD  = "empcd";
    private static final String KEY_PASSWD = "password";

    private BiometricCredentialHelper() {}

    private static SharedPreferences prefs(Context ctx) {
        try {
            String masterKeyAlias = MasterKeys.getOrCreate(MasterKeys.AES256_GCM_SPEC);
            return EncryptedSharedPreferences.create(
                    PREF_NAME, masterKeyAlias, ctx,
                    EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
                    EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM);
        } catch (GeneralSecurityException | IOException e) {
            return null;
        }
    }

    static void save(Context ctx, String empcd, String password) {
        SharedPreferences p = prefs(ctx);
        if (p == null) return;
        p.edit().putString(KEY_EMPCD, empcd).putString(KEY_PASSWD, password).apply();
    }

    static void clear(Context ctx) {
        SharedPreferences p = prefs(ctx);
        if (p == null) return;
        p.edit().clear().apply();
    }

    static boolean hasStoredCredentials(Context ctx) {
        SharedPreferences p = prefs(ctx);
        return p != null && p.contains(KEY_EMPCD) && p.contains(KEY_PASSWD);
    }

    static String getEmpcd(Context ctx) {
        SharedPreferences p = prefs(ctx);
        return p == null ? null : p.getString(KEY_EMPCD, null);
    }

    static String getPassword(Context ctx) {
        SharedPreferences p = prefs(ctx);
        return p == null ? null : p.getString(KEY_PASSWD, null);
    }
}

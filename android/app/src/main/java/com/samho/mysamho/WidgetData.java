package com.samho.mysamho;

import android.content.Context;
import android.content.SharedPreferences;
import android.webkit.CookieManager;

import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;

/**
 * Kho dữ liệu dùng chung giữa app và widget màn hình chính.
 *
 * Xác thực: dùng LẠI cookie phiên đăng nhập của WebView (CookieManager), không nhúng API key.
 * HR_api chỉ chặn bằng 1 API key dùng chung nên nếu widget gọi thẳng API thì key nằm trong APK,
 * ai dịch ngược cũng tra được dữ liệu của mọi mã NV. Gọi qua /Widget/Data của HR_web thì server tự
 * nhận ra người đăng nhập từ cookie, widget chỉ thấy đúng dữ liệu của chính chủ máy.
 *
 * Luôn ghi lại bản chụp (snapshot) gần nhất: mất mạng hay hết phiên thì widget vẫn hiện số cũ kèm
 * mốc thời gian, không bị trắng trơn.
 */
public final class WidgetData {

    private static final String PREF      = "mysamho_widget";
    private static final String K_JSON    = "snapshot_json";
    private static final String K_AT      = "snapshot_at";      // epoch millis lúc lấy được
    private static final String K_STALE   = "snapshot_stale";   // true = lần gọi gần nhất thất bại
    private static final String K_BASE    = "base_url";

    // Khớp với MainActivity — widget chạy ở tiến trình khác, không đọc được biến của Activity.
    private static final String FALLBACK_BASE = "https://hr.esamho.com/HR_Web";

    private WidgetData() {}

    private static SharedPreferences prefs(Context c) {
        return c.getApplicationContext().getSharedPreferences(PREF, Context.MODE_PRIVATE);
    }

    /** MainActivity gọi mỗi khi chọn xong mạng nội bộ/ngoài, để widget gọi đúng địa chỉ. */
    public static void saveBaseUrl(Context c, String baseUrl) {
        if (baseUrl == null || baseUrl.isEmpty()) return;
        prefs(c).edit().putString(K_BASE, baseUrl).apply();
    }

    public static String baseUrl(Context c) {
        return prefs(c).getString(K_BASE, FALLBACK_BASE);
    }

    public static JSONObject cached(Context c) {
        String s = prefs(c).getString(K_JSON, null);
        if (s == null) return null;
        try { return new JSONObject(s); } catch (Exception e) { return null; }
    }

    public static long cachedAt(Context c) { return prefs(c).getLong(K_AT, 0L); }
    public static boolean isStale(Context c) { return prefs(c).getBoolean(K_STALE, false); }

    /**
     * Gọi mạng lấy dữ liệu mới. PHẢI chạy ở luồng nền.
     * @return true nếu lấy được bản mới (đã lưu), false nếu thất bại (giữ nguyên bản cũ).
     */
    public static boolean refreshBlocking(Context c) {
        HttpURLConnection conn = null;
        try {
            String base   = baseUrl(c);
            String cookie = CookieManager.getInstance().getCookie(base);
            if (cookie == null || cookie.isEmpty()) {   // chưa đăng nhập bao giờ
                markStale(c);
                return false;
            }

            conn = (HttpURLConnection) new URL(base + "/Widget/Data").openConnection();
            conn.setRequestMethod("GET");
            conn.setRequestProperty("Cookie", cookie);
            conn.setRequestProperty("Accept", "application/json");
            conn.setInstanceFollowRedirects(false);   // hết phiên -> 302 về trang login, KHÔNG đi theo
            conn.setConnectTimeout(8000);
            conn.setReadTimeout(8000);

            int code = conn.getResponseCode();
            if (code != 200) { markStale(c); return false; }

            StringBuilder sb = new StringBuilder();
            try (BufferedReader r = new BufferedReader(new InputStreamReader(conn.getInputStream(), "UTF-8"))) {
                String ln;
                while ((ln = r.readLine()) != null) sb.append(ln);
            }

            JSONObject jo = new JSONObject(sb.toString());
            if (!jo.optBoolean("ok", false)) { markStale(c); return false; }

            // Badge trên icon Android = SỐ THÔNG BÁO còn trong khay (launcher tự đếm), không phải
            // một con số ta đặt được. Nên khi người dùng đã đọc hết trên web/app thì phải dọn khay,
            // nếu không icon vẫn đeo chấm/số dù không còn gì chưa đọc.
            if (jo.optInt("unread", -1) == 0) {
                android.app.NotificationManager nm = (android.app.NotificationManager)
                        c.getSystemService(Context.NOTIFICATION_SERVICE);
                if (nm != null) nm.cancelAll();
            }

            prefs(c).edit()
                    .putString(K_JSON, jo.toString())
                    .putLong(K_AT, System.currentTimeMillis())
                    .putBoolean(K_STALE, false)
                    .apply();
            return true;
        } catch (Exception e) {
            markStale(c);
            return false;
        } finally {
            if (conn != null) conn.disconnect();
        }
    }

    private static void markStale(Context c) {
        prefs(c).edit().putBoolean(K_STALE, true).apply();
    }

    /** Đăng xuất -> xoá sạch, không để số liệu của người cũ nằm lại trên màn hình chính. */
    public static void clear(Context c) {
        prefs(c).edit().remove(K_JSON).remove(K_AT).remove(K_STALE).apply();
    }
}

package com.samho.mysamho;

import android.content.Context;
import android.content.SharedPreferences;

import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;

/**
 * Kho dữ liệu dùng chung giữa app và widget màn hình chính.
 *
 * Xác thực: dùng LẠI cookie phiên đăng nhập mà app chép sang (xem saveCookie), không nhúng API key.
 * HR_api chỉ chặn bằng 1 API key dùng chung nên nếu widget gọi thẳng API thì key nằm trong APK,
 * ai dịch ngược cũng tra được dữ liệu của mọi mã NV. Gọi qua /Widget/Data của HR_web thì server tự
 * nhận ra người đăng nhập từ cookie, widget chỉ thấy đúng dữ liệu của chính chủ máy.
 *
 * Chọn mạng: widget TỰ dò mỗi lần làm mới (nội bộ công ty trước, không được thì ra mạng ngoài),
 * không dùng địa chỉ mà app chọn lần cuối — nhân viên rời công ty ra về thì địa chỉ cũ đã sai, mà
 * widget phải chạy được cả lúc app đang tắt hẳn.
 *
 * Luôn ghi lại bản chụp (snapshot) gần nhất: mất mạng hay hết phiên thì widget vẫn hiện số cũ kèm
 * mốc thời gian, không bị trắng trơn.
 */
public final class WidgetData {

    private static final String PREF      = "mysamho_widget";
    private static final String K_JSON    = "snapshot_json";
    private static final String K_AT      = "snapshot_at";      // epoch millis lúc lấy được
    private static final String K_STALE   = "snapshot_stale";   // true = lần gọi gần nhất thất bại
    // Khớp với MainActivity — widget không đọc được biến của Activity nên phải khai lại.
    public static final String INTERNAL_BASE = "http://sa1-hanaro.esamho.com/hr_Web";
    public static final String EXTERNAL_BASE = "https://hr.esamho.com/HR_Web";

    // Cookie lưu RIÊNG theo từng địa chỉ: 2 địa chỉ là 2 tên miền khác nhau nên trình duyệt giữ 2
    // cookie khác nhau, lấy cookie của bên này gắn cho bên kia thì server không nhận ra phiên.
    private static String cookieKey(String base) {
        return INTERNAL_BASE.equals(base) ? "cookie_internal" : "cookie_external";
    }

    private WidgetData() {}

    private static SharedPreferences prefs(Context c) {
        return c.getApplicationContext().getSharedPreferences(PREF, Context.MODE_PRIVATE);
    }

    /**
     * MainActivity chép cookie phiên của WebView sang đây sau mỗi lần tải xong trang, kèm địa chỉ
     * mà cookie đó thuộc về.
     *
     * KHÔNG để widget tự gọi CookieManager.getInstance(): widget chạy ở luồng nền, và tiến trình
     * app có thể vừa bị thông báo đẩy đánh thức dậy — lúc đó chưa có Activity nào, WebView chưa hề
     * được khởi tạo. Gọi CookieManager trong hoàn cảnh đó không lấy được gì (có máy còn ném lỗi),
     * nên widget sẽ không bao giờ làm mới được khi app đang tắt — đúng tình huống cần nó nhất.
     * Làm giống bản iOS: app chủ động đẩy cookie ra chỗ dùng chung.
     */
    public static void saveCookie(Context c, String base, String cookie) {
        if (base == null || cookie == null || cookie.isEmpty()) return;
        prefs(c).edit().putString(cookieKey(base), cookie).apply();
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
        // Thứ tự thử: mạng nội bộ công ty trước (nhanh hơn, và là đường app vẫn ưu tiên), rồi tới
        // mạng ngoài. Đang dùng 4G/5G thì chắc chắn không với tới mạng nội bộ -> bỏ qua luôn cho
        // khỏi phải chờ hết thời gian chờ. Đúng cách autoSelectNetworkAndStart bên MainActivity làm.
        boolean mobile = isOnMobileData(c);
        if (!mobile && tryBase(c, INTERNAL_BASE, 2500)) return true;
        if (tryBase(c, EXTERNAL_BASE, 8000)) return true;

        markStale(c);
        return false;
    }

    /** Gọi 1 địa chỉ bằng đúng cookie của chính nó. true = lấy được bản mới và đã lưu. */
    private static boolean tryBase(Context c, String base, int timeoutMs) {
        String cookie = prefs(c).getString(cookieKey(base), null);
        if (cookie == null || cookie.isEmpty()) return false;   // chưa từng đăng nhập ở mạng này

        HttpURLConnection conn = null;
        try {
            conn = (HttpURLConnection) new URL(base + "/Widget/Data").openConnection();
            conn.setRequestMethod("GET");
            conn.setRequestProperty("Cookie", cookie);
            conn.setRequestProperty("Accept", "application/json");
            conn.setInstanceFollowRedirects(false);   // hết phiên -> 302 về trang login, KHÔNG đi theo
            conn.setConnectTimeout(timeoutMs);
            conn.setReadTimeout(timeoutMs);

            if (conn.getResponseCode() != 200) return false;

            StringBuilder sb = new StringBuilder();
            try (BufferedReader r = new BufferedReader(new InputStreamReader(conn.getInputStream(), "UTF-8"))) {
                String ln;
                while ((ln = r.readLine()) != null) sb.append(ln);
            }

            JSONObject jo = new JSONObject(sb.toString());
            if (!jo.optBoolean("ok", false)) return false;

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
            return false;
        } finally {
            if (conn != null) conn.disconnect();
        }
    }

    /** 4G/5G (không kèm wifi/ethernet) thì khỏi thử mạng nội bộ. */
    private static boolean isOnMobileData(Context c) {
        try {
            android.net.ConnectivityManager cm = (android.net.ConnectivityManager)
                    c.getSystemService(Context.CONNECTIVITY_SERVICE);
            if (cm == null) return false;
            android.net.Network n = cm.getActiveNetwork();
            if (n == null) return false;
            android.net.NetworkCapabilities caps = cm.getNetworkCapabilities(n);
            return caps != null
                    && caps.hasTransport(android.net.NetworkCapabilities.TRANSPORT_CELLULAR)
                    && !caps.hasTransport(android.net.NetworkCapabilities.TRANSPORT_WIFI)
                    && !caps.hasTransport(android.net.NetworkCapabilities.TRANSPORT_ETHERNET);
        } catch (Exception e) {
            return false;
        }
    }

    private static void markStale(Context c) {
        prefs(c).edit().putBoolean(K_STALE, true).apply();
    }

    /** Đăng xuất -> xoá sạch, không để số liệu của người cũ nằm lại trên màn hình chính. */
    public static void clear(Context c) {
        prefs(c).edit().remove(K_JSON).remove(K_AT).remove(K_STALE)
                .remove(cookieKey(INTERNAL_BASE)).remove(cookieKey(EXTERNAL_BASE)).apply();
    }
}

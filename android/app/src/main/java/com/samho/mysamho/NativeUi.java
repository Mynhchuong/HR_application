package com.samho.mysamho;

import android.app.Activity;
import android.content.DialogInterface;
import android.content.res.ColorStateList;
import android.os.Build;
import android.text.InputType;
import android.view.HapticFeedbackConstants;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.JsPromptResult;
import android.webkit.JsResult;
import android.webkit.WebView;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.TextView;

import androidx.appcompat.app.AlertDialog;

import com.google.android.material.dialog.MaterialAlertDialogBuilder;
import com.google.android.material.snackbar.Snackbar;

import org.json.JSONObject;

/**
 * Giao diện GỐC cho thông báo / xác nhận / cảnh báo của web (yêu cầu 2026-11-21: khách review app thấy
 * toast/modal kiểu web là biết ngay app chỉ là WebView).
 *
 * Web gọi qua 1 bridge generic: Android.nativeUI(json) — xem HR_web/wwwroot/js/native-ui.js. Lớp này CHỈ vẽ
 * giao diện, không biết nghiệp vụ (đúng NATIVE_HELPER_RULES.md). Bản iOS tương ứng: NativeUI.swift.
 *   {action:"toast",   message, type: success|error|warning|info}
 *   {action:"confirm", id, title, message, okText, cancelText, destructive} → NativeUI._done(id, true|false)
 *   {action:"alert",   id, title, message, okText}                         → NativeUI._done(id, true)
 *   {action:"haptic",  style: success|warning|error|light}
 * Ngoài ra thay hộp thoại mặc định của alert()/confirm()/prompt() JS (có dòng "Trang tại http://... cho biết").
 */
final class NativeUi {
    private final Activity activity;
    private final WebView webView;

    NativeUi(Activity activity, WebView webView) {
        this.activity = activity;
        this.webView = webView;
    }

    /** Gọi từ @JavascriptInterface (luồng nền) — tự chuyển về luồng giao diện. */
    /** Web báo ngón tay đang ở chỗ được phép "kéo để tải lại" không (native-ui.js, mỗi lần chạm). */
    volatile boolean pullAllowed = true;

    void handle(String json) {
        final JSONObject o;
        try { o = new JSONObject(json == null ? "{}" : json); } catch (Exception e) { return; }
        // Đặt ngay trên luồng gọi (không chờ luồng giao diện) để kịp trước khi cử chỉ kéo được nhận diện
        if ("pull".equals(o.optString("action"))) { pullAllowed = o.optBoolean("allow", true); return; }
        activity.runOnUiThread(() -> {
            if (gone()) return;
            try {
            switch (o.optString("action")) {
                case "toast":   toast(o.optString("message"), o.optString("type", "success")); break;
                case "confirm": confirm(o); break;
                case "alert":   alert(o); break;
                case "haptic":  haptic(o.optString("style", "light")); break;
                default: break;
            }
            } catch (Exception e) {
                // Lỗi vẽ giao diện gốc không được làm crash app — web vẫn chạy bình thường
                android.util.Log.w("NativeUi", "handle failed", e);
            }
        });
    }

    // ── Thông báo ngắn: Snackbar Material, icon màu theo loại, nổi trên thanh điều hướng dưới của web ──────
    private void toast(String message, String type) {
        if (message == null || message.isEmpty()) return;
        int icon, color;
        switch (type) {
            case "error":   icon = R.drawable.ic_ui_error;   color = 0xFFFF6B6B; break;
            case "warning": icon = R.drawable.ic_ui_warning; color = 0xFFFFC145; break;
            case "info":    icon = R.drawable.ic_ui_info;    color = 0xFF6CB8FF; break;
            default:        icon = R.drawable.ic_ui_check_circle; color = 0xFF4CD486; break;
        }
        View root = activity.findViewById(R.id.main);
        if (root == null) root = webView;
        Snackbar sb = Snackbar.make(root, message,
                "error".equals(type) || message.length() > 70 ? Snackbar.LENGTH_LONG : Snackbar.LENGTH_SHORT);
        View v = sb.getView();
        TextView tv = v.findViewById(com.google.android.material.R.id.snackbar_text);
        if (tv != null) {
            tv.setMaxLines(4);
            tv.setCompoundDrawablesRelativeWithIntrinsicBounds(icon, 0, 0, 0);
            tv.setCompoundDrawablePadding(dp(12));
            tv.setCompoundDrawableTintList(ColorStateList.valueOf(color));
        }
        // Đẩy lên khỏi thanh điều hướng dưới của web (~64dp) để không che nút
        ViewGroup.LayoutParams lp = v.getLayoutParams();
        if (lp instanceof ViewGroup.MarginLayoutParams) {
            ViewGroup.MarginLayoutParams m = (ViewGroup.MarginLayoutParams) lp;
            m.bottomMargin = m.bottomMargin + dp(72);
            v.setLayoutParams(m);
        }
        sb.show();
        haptic("error".equals(type) ? "error" : "warning".equals(type) ? "warning" : "success".equals(type) ? "success" : "light");
    }

    // ── Hộp thoại xác nhận / thông báo (Material 3) ──────────────────────────────────────────────────────
    private void confirm(JSONObject o) {
        final String id = o.optString("id");
        final boolean[] answered = { false };
        MaterialAlertDialogBuilder b = new MaterialAlertDialogBuilder(activity)
                .setMessage(o.optString("message"))
                .setPositiveButton(nz(o.optString("okText"), "OK"), (d, w) -> { answered[0] = true; done(id, true); })
                .setNegativeButton(nz(o.optString("cancelText"), "Hủy"), (d, w) -> { answered[0] = true; done(id, false); })
                .setOnDismissListener(d -> { if (!answered[0]) done(id, false); });   // bấm ra ngoài / nút Back
        if (!o.optString("title").isEmpty()) b.setTitle(o.optString("title"));
        AlertDialog dlg = b.show();
        if (o.optBoolean("destructive")) {
            dlg.getButton(DialogInterface.BUTTON_POSITIVE).setTextColor(0xFFDC2626);
            haptic("warning");
        }
    }

    private void alert(JSONObject o) {
        final String id = o.optString("id");
        MaterialAlertDialogBuilder b = new MaterialAlertDialogBuilder(activity)
                .setMessage(o.optString("message"))
                .setPositiveButton(nz(o.optString("okText"), "OK"), null)
                .setOnDismissListener(d -> done(id, true));
        if (!o.optString("title").isEmpty()) b.setTitle(o.optString("title"));
        b.show();
    }

    private void done(String id, boolean ok) {
        if (id == null || id.isEmpty() || gone()) return;
        webView.evaluateJavascript("window.NativeUI&&NativeUI._done(" + JSONObject.quote(id) + "," + ok + ")", null);
    }

    // ── Rung phản hồi ────────────────────────────────────────────────────────────────────────────────────
    void haptic(String style) {
        int c;
        switch (style) {
            case "success": c = Build.VERSION.SDK_INT >= 30 ? HapticFeedbackConstants.CONFIRM : HapticFeedbackConstants.VIRTUAL_KEY; break;
            case "error":
            case "warning": c = Build.VERSION.SDK_INT >= 30 ? HapticFeedbackConstants.REJECT : HapticFeedbackConstants.LONG_PRESS; break;
            default:        c = HapticFeedbackConstants.KEYBOARD_TAP; break;
        }
        webView.performHapticFeedback(c);
    }

    // ── alert()/confirm()/prompt() của JS: hộp thoại Material, không còn dòng "Trang tại http://..." ─────────
    private boolean gone() { return activity.isFinishing() || activity.isDestroyed(); }

    boolean jsAlert(String message, JsResult result) {
        if (gone()) { result.cancel(); return true; }   // màn hình đang đóng: show dialog sẽ crash (BadTokenException)
        new MaterialAlertDialogBuilder(activity)
                .setMessage(message)
                .setPositiveButton(android.R.string.ok, (d, w) -> result.confirm())
                .setOnCancelListener(d -> result.confirm())
                .show();
        return true;
    }

    boolean jsConfirm(String message, JsResult result) {
        if (gone()) { result.cancel(); return true; }
        new MaterialAlertDialogBuilder(activity)
                .setMessage(message)
                .setPositiveButton(android.R.string.ok, (d, w) -> result.confirm())
                .setNegativeButton(android.R.string.cancel, (d, w) -> result.cancel())
                .setOnCancelListener(d -> result.cancel())
                .show();
        return true;
    }

    boolean jsPrompt(String message, String defaultValue, JsPromptResult result) {
        if (gone()) { result.cancel(); return true; }
        final EditText input = new EditText(activity);
        input.setInputType(InputType.TYPE_CLASS_TEXT);
        if (defaultValue != null) input.setText(defaultValue);
        FrameLayout box = new FrameLayout(activity);
        box.setPadding(dp(20), dp(4), dp(20), 0);
        box.addView(input);
        new MaterialAlertDialogBuilder(activity)
                .setMessage(message)
                .setView(box)
                .setPositiveButton(android.R.string.ok, (d, w) -> result.confirm(input.getText().toString()))
                .setNegativeButton(android.R.string.cancel, (d, w) -> result.cancel())
                .setOnCancelListener(d -> result.cancel())
                .show();
        return true;
    }

    private static String nz(String s, String def) { return s == null || s.isEmpty() ? def : s; }

    private int dp(int v) { return Math.round(v * activity.getResources().getDisplayMetrics().density); }
}

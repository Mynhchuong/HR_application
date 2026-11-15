package com.samho.mysamho;

import android.content.Context;
import android.content.Intent;
import android.graphics.drawable.Icon;
import android.os.Build;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

/**
 * Nút tắt hiện ra khi giữ icon My Samho trên màn hình chính.
 *
 * Đặt theo ROLE: công nhân cần tạo đơn cho mình, quản lý cần duyệt đơn của người khác — hai nhóm
 * này gần như không dùng chung chức năng nào, để chung 1 bộ thì ai cũng phải lướt qua mục không
 * liên quan. Role đọc từ bản chụp mà widget đã lấy về (WidgetData), nên không tốn thêm lần gọi mạng.
 *
 * Android 7.1 (API 25) trở lên mới có nút tắt; máy cũ hơn thì bỏ qua, không lỗi gì.
 */
public final class AppShortcuts {

    private AppShortcuts() {}

    private static String L(String lang, String vi, String en, String ko) {
        if ("ko".equals(lang)) return ko;
        if ("en".equals(lang)) return en;
        return vi;
    }

    /** Gọi sau mỗi lần làm mới dữ liệu widget — role có thể đổi (đổi người đăng nhập trên máy chung). */
    public static void refresh(Context ctx) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.N_MR1) return;

        android.content.pm.ShortcutManager mgr = ctx.getSystemService(android.content.pm.ShortcutManager.class);
        if (mgr == null) return;

        org.json.JSONObject d = WidgetData.cached(ctx);
        String role = d != null ? d.optString("role", "worker") : "worker";
        String lang = d != null ? d.optString("lang", "vi")     : "vi";

        List<android.content.pm.ShortcutInfo> list = new ArrayList<>();
        if ("worker".equals(role)) {
            list.add(make(ctx, "leave", L(lang, "Xin nghỉ phép", "Request leave", "휴가 신청"),
                    "/Leave/LeaveMyRequests", R.drawable.ic_w_leave));
            list.add(make(ctx, "gate", L(lang, "Đăng ký ra cổng", "Gate pass", "외출 신청"),
                    "/GatePass/GpMyRequests", R.drawable.ic_w_gate));
            list.add(make(ctx, "pay", L(lang, "Phiếu lương", "Payslip", "급여명세서"),
                    "/Payslip/Index", R.drawable.ic_w_news));
        } else {
            // Expat có màn hình duyệt RIÊNG (…ForExpat) — dùng nhầm màn hình của quản lý người Việt
            // thì giao diện ra tiếng Việt hết.
            boolean expat = "expat".equals(role);
            list.add(make(ctx, "appr_leave", L(lang, "Duyệt nghỉ phép", "Approve leave", "휴가 승인"),
                    expat ? "/Leave/LeaveApprovalForExpat" : "/Leave/TeamSchedule", R.drawable.ic_w_approve));
            list.add(make(ctx, "appr_gate", L(lang, "Duyệt ra cổng", "Approve gate pass", "외출 승인"),
                    expat ? "/GatePass/GpListForExpat" : "/GatePass/GpListForSupervisor", R.drawable.ic_w_gate));
            list.add(make(ctx, "calendar", L(lang, "Lịch nghỉ nhóm", "Team calendar", "팀 일정"),
                    expat ? "/Leave/TeamCalendarForExpat" : "/Leave/TeamCalendar", R.drawable.ic_w_leave));
        }

        try {
            // Hệ thống giới hạn số nút tắt (thường 4) — cắt bớt cho chắc thay vì để ném lỗi.
            int max = Math.min(list.size(), mgr.getMaxShortcutCountPerActivity());
            mgr.setDynamicShortcuts(new ArrayList<>(list.subList(0, max)));
        } catch (Exception ignored) {
            // Máy một số hãng chặn đặt nút tắt; không có nút tắt thì thôi, app vẫn chạy bình thường.
        }
    }

    private static android.content.pm.ShortcutInfo make(Context ctx, String id, String label,
                                                        String path, int iconRes) {
        Intent i = new Intent(ctx, MainActivity.class);
        i.setAction(Intent.ACTION_VIEW);                 // bắt buộc phải có action, nếu không hệ thống từ chối
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        i.putExtra("open_path", path);

        return new android.content.pm.ShortcutInfo.Builder(ctx, id)
                .setShortLabel(label)
                .setLongLabel(label)
                .setIcon(Icon.createWithResource(ctx, iconRes))
                .setIntent(i)
                .build();
    }

    /** Đăng xuất -> bỏ nút tắt, không để người sau cầm máy thấy lối tắt của role người trước. */
    public static void clear(Context ctx) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.N_MR1) return;
        android.content.pm.ShortcutManager mgr = ctx.getSystemService(android.content.pm.ShortcutManager.class);
        if (mgr == null) return;
        try { mgr.removeAllDynamicShortcuts(); } catch (Exception ignored) {}
    }
}

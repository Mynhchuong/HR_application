package com.samho.mysamho;

import android.app.PendingIntent;
import android.appwidget.AppWidgetManager;
import android.appwidget.AppWidgetProvider;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.view.View;
import android.widget.RemoteViews;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Widget màn hình chính My Samho.
 *
 * Nội dung theo role (chốt với sếp 2026-11-15):
 *   - Công nhân : thông báo quan trọng · đơn của mình duyệt tới đâu · bản tin Samho mới nhất
 *   - Quản lý   : bao nhiêu đơn cần mình duyệt · hôm nay bao nhiêu NV nghỉ · bao nhiêu đơn ra cổng chờ ký
 *   - Expat     : giống quản lý, chỉ khác ngôn ngữ
 *
 * Làm mới: lúc hệ thống gọi onUpdate (lưới an toàn 30 phút), lúc mở/quay lại app, và lúc có thông
 * báo đẩy. KHÔNG tự hẹn giờ riêng để khỏi hao pin — các mốc trên đã phủ gần hết tình huống thật.
 */
public class MySamhoWidgetProvider extends AppWidgetProvider {

    /** Tự bắn để ép vẽ lại ngay (app gọi sau khi đăng nhập / nhận push / quay lại foreground). */
    public static final String ACTION_REFRESH = "com.samho.mysamho.WIDGET_REFRESH";

    // Widget chạy ngoài vòng đời Activity nên phải có executor riêng; 1 luồng là đủ vì mỗi lần
    // làm mới chỉ có đúng 1 request mạng.
    private static final ExecutorService IO = Executors.newSingleThreadExecutor();

    @Override
    public void onUpdate(Context ctx, AppWidgetManager mgr, int[] ids) {
        renderAll(ctx, false);          // vẽ ngay bằng dữ liệu đã lưu, không bắt người dùng chờ mạng
        refreshThenRender(ctx);
    }

    @Override
    public void onReceive(Context ctx, Intent intent) {
        super.onReceive(ctx, intent);
        if (ACTION_REFRESH.equals(intent.getAction())) refreshThenRender(ctx);
    }

    /** App gọi mỗi khi có gì đó có thể làm đổi số liệu (đăng nhập xong, nhận push, quay lại app). */
    public static void requestRefresh(Context ctx) {
        Intent i = new Intent(ctx, MySamhoWidgetProvider.class);
        i.setAction(ACTION_REFRESH);
        ctx.sendBroadcast(i);
    }

    private void refreshThenRender(Context ctx) {
        final Context app = ctx.getApplicationContext();
        IO.execute(() -> {
            WidgetData.refreshBlocking(app);
            renderAll(app, true);
        });
    }

    private static void renderAll(Context ctx, boolean afterNetwork) {
        AppWidgetManager mgr = AppWidgetManager.getInstance(ctx);
        int[] ids = mgr.getAppWidgetIds(new ComponentName(ctx, MySamhoWidgetProvider.class));
        if (ids == null || ids.length == 0) return;      // người dùng chưa đặt widget nào
        RemoteViews rv = build(ctx);
        for (int id : ids) mgr.updateAppWidget(id, rv);
    }

    // ── Nhãn theo ngôn ngữ ──────────────────────────────────────────────────
    // Widget nằm ngoài WebView nên không dùng được bộ dịch của web; 3 ngôn ngữ, mỗi nhãn 1 dòng.
    private static String L(String lang, String vi, String en, String ko) {
        if ("ko".equals(lang)) return ko;
        if ("en".equals(lang)) return en;
        return vi;
    }

    /**
     * Ngôn ngữ dùng khi CHƯA đăng nhập (chưa biết tài khoản của ai).
     * Lúc đó server chưa trả "lang" nên phải đoán theo ngôn ngữ của máy — mặc định cứng tiếng Việt
     * sẽ làm anh em Hàn/nước ngoài thấy dòng "Chạm để đăng nhập" bằng tiếng Việt ngay trên màn hình
     * chính, trong khi họ chưa có cách nào đổi.
     */
    private static String deviceLang() {
        String l = java.util.Locale.getDefault().getLanguage();
        if ("ko".equals(l)) return "ko";
        if ("vi".equals(l)) return "vi";
        return "en";        // còn lại dùng tiếng Anh, đúng quy ước của phần Expat trên web
    }

    private static RemoteViews build(Context ctx) {
        RemoteViews rv = new RemoteViews(ctx.getPackageName(), R.layout.widget_mysamho);

        JSONObject d    = WidgetData.cached(ctx);
        String     lang = d != null ? d.optString("lang", "vi") : deviceLang();

        // Bấm vào bất kỳ đâu -> mở app. Dùng FLAG_UPDATE_CURRENT|IMMUTABLE theo yêu cầu Android 12+.
        Intent open = new Intent(ctx, MainActivity.class);
        open.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        rv.setOnClickPendingIntent(R.id.wRoot, PendingIntent.getActivity(
                ctx, 0, open, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE));

        // Chưa từng đăng nhập (hoặc vừa đăng xuất) -> mời đăng nhập, không hiện số 0 gây hiểu nhầm.
        if (d == null) {
            rv.setTextViewText(R.id.wHeader, ctx.getString(R.string.widget_title));
            rv.setViewVisibility(R.id.wManager, View.GONE);
            rv.setViewVisibility(R.id.wWorker, View.VISIBLE);
            rv.setImageViewResource(R.id.wIcoNotice, R.drawable.ic_w_logo);
            rv.setTextViewText(R.id.wNotice, L(lang,
                    "Chạm để đăng nhập", "Tap to sign in", "탭하여 로그인"));
            rv.setTextViewText(R.id.wMyReq, L(lang,
                    "Xem thông báo và đơn của bạn ngay trên màn hình chính",
                    "See your notifications and requests right here",
                    "알림과 신청 현황을 홈 화면에서 바로 확인"));
            rv.setTextViewText(R.id.wBulletin, "");
            rv.setTextViewText(R.id.wFooter, "");
            rv.setViewVisibility(R.id.wUnread, View.GONE);
            return rv;
        }

        String role = d.optString("role", "worker");
        String name = d.optString("fullName", "");
        rv.setTextViewText(R.id.wHeader, name.isEmpty() ? ctx.getString(R.string.widget_title) : name);

        int unread = d.optInt("unread", 0);
        if (unread > 0) {
            rv.setViewVisibility(R.id.wUnread, View.VISIBLE);
            rv.setTextViewText(R.id.wUnread, unread > 99 ? "99+" : String.valueOf(unread));
        } else {
            rv.setViewVisibility(R.id.wUnread, View.GONE);
        }

        if ("worker".equals(role)) {
            rv.setViewVisibility(R.id.wWorker,  View.VISIBLE);
            rv.setViewVisibility(R.id.wManager, View.GONE);
            buildWorker(rv, d, lang);
        } else {
            rv.setViewVisibility(R.id.wWorker,  View.GONE);
            rv.setViewVisibility(R.id.wManager, View.VISIBLE);
            buildManager(rv, d, lang);
        }

        rv.setTextViewText(R.id.wFooter, footer(ctx, lang));
        return rv;
    }

    private static void buildWorker(RemoteViews rv, JSONObject d, String lang) {
        // ── Thông báo quan trọng ────────────────────────────────────────────
        JSONArray notices = d.optJSONArray("notices");
        String noticeText;
        if (notices != null && notices.length() > 0) {
            JSONObject n = notices.optJSONObject(0);
            String title = n != null ? n.optString("title", "") : "";
            boolean high = n != null && n.optBoolean("high", false);
            // Icon thay cho ký tự "⚠" dán vào chuỗi: cùng tác dụng báo gấp nhưng không ăn mất
            // chỗ của tiêu đề, và không bị font thiết bị vẽ thành ô vuông.
            rv.setImageViewResource(R.id.wIcoNotice,
                    high ? R.drawable.ic_w_alert : R.drawable.ic_w_logo);
            noticeText = title;
            if (notices.length() > 1)
                noticeText += L(lang,
                        "  (+" + (notices.length() - 1) + " nữa)",
                        "  (+" + (notices.length() - 1) + " more)",
                        "  (+" + (notices.length() - 1) + "건)");
        } else {
            rv.setImageViewResource(R.id.wIcoNotice, R.drawable.ic_w_logo);
            noticeText = L(lang, "Không có thông báo mới", "No new notifications", "새 알림 없음");
        }
        rv.setTextViewText(R.id.wNotice, noticeText);

        // ── Đơn của mình: duyệt tới đâu ─────────────────────────────────────
        JSONArray reqs = d.optJSONArray("myReqs");
        int pending = 0, approved = 0, rejected = 0;
        if (reqs != null) {
            for (int i = 0; i < reqs.length(); i++) {
                JSONObject r = reqs.optJSONObject(i);
                if (r == null) continue;
                String st = r.optString("status", "PENDING");
                if ("APPROVED".equals(st))      approved++;
                else if ("REJECTED".equals(st)) rejected++;
                else                            pending++;
            }
        }
        StringBuilder sb = new StringBuilder(L(lang, "Đơn của bạn: ", "Your requests: ", "내 신청: "));
        if (pending + approved + rejected == 0) {
            sb.append(L(lang, "chưa có đơn nào", "none yet", "없음"));
        } else {
            sb.append(pending).append(L(lang, " chờ duyệt", " pending", "건 대기"));
            if (approved > 0) sb.append(" · ").append(approved).append(L(lang, " đã duyệt", " approved", "건 승인"));
            if (rejected > 0) sb.append(" · ").append(rejected).append(L(lang, " từ chối", " rejected", "건 반려"));
        }
        rv.setTextViewText(R.id.wMyReq, sb.toString());

        // ── Bản tin Samho ───────────────────────────────────────────────────
        JSONObject b = d.optJSONObject("bulletin");
        rv.setTextViewText(R.id.wBulletin, b != null
                ? b.optString("title", "")
                : L(lang, "Chưa có bản tin mới", "No new bulletin", "새 소식 없음"));
    }

    private static void buildManager(RemoteViews rv, JSONObject d, String lang) {
        JSONObject c = d.optJSONObject("counters");
        int need  = c != null ? c.optInt("needApprove", 0) : 0;
        int gate  = c != null ? c.optInt("gatePass", 0)    : 0;
        int leave = c != null ? c.optInt("leaveToday", 0)  : 0;

        rv.setTextViewText(R.id.wNumApprove, String.valueOf(need));
        rv.setTextViewText(R.id.wLblApprove, L(lang, "Cần bạn duyệt", "To approve", "결재 대기"));

        rv.setTextViewText(R.id.wNumGate, String.valueOf(gate));
        rv.setTextViewText(R.id.wLblGate, L(lang, "Ra cổng chờ ký", "Gate passes", "외출 결재"));

        rv.setTextViewText(R.id.wNumLeave, String.valueOf(leave));
        rv.setTextViewText(R.id.wLblLeave, L(lang, "Nghỉ hôm nay", "On leave today", "오늘 휴가"));
    }

    private static String footer(Context ctx, String lang) {
        long at = WidgetData.cachedAt(ctx);
        if (at == 0) return "";

        // Hết phiên / mất mạng: nói thẳng số đang cũ, đừng để người dùng tin là số thật lúc này.
        if (WidgetData.isStale(ctx))
            return L(lang, "Số liệu cũ — mở app để cập nhật",
                           "Outdated — open app to refresh",
                           "오래된 정보 — 앱에서 새로고침");

        long mins = (System.currentTimeMillis() - at) / 60000L;
        if (mins < 1)  return L(lang, "Vừa cập nhật", "Just updated", "방금 업데이트");
        if (mins < 60) return L(lang, "Cập nhật " + mins + " phút trước",
                                      "Updated " + mins + "m ago",
                                      mins + "분 전 업데이트");
        long hrs = mins / 60;
        return L(lang, "Cập nhật " + hrs + " giờ trước",
                       "Updated " + hrs + "h ago",
                       hrs + "시간 전 업데이트");
    }
}

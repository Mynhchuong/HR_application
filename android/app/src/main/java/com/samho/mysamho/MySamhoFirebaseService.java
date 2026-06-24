package com.samho.mysamho;

import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.os.Build;

import androidx.core.app.NotificationCompat;

import com.google.firebase.messaging.FirebaseMessagingService;
import com.google.firebase.messaging.RemoteMessage;

import java.util.concurrent.atomic.AtomicInteger;

public class MySamhoFirebaseService extends FirebaseMessagingService {

    private static final String CHANNEL_ID   = "mysamho_noti";
    private static final String PREF_NAME    = "mysamho_prefs";
    private static final String KEY_FCM_TOKEN = "fcm_token";

    // Monotonic counter để tránh collision khi nhiều noti tới gần nhau
    private static final AtomicInteger NOTI_ID_GEN = new AtomicInteger(1000);

    // Gọi khi Firebase cấp token mới (lần đầu cài hoặc token hết hạn)
    @Override
    public void onNewToken(String token) {
        super.onNewToken(token);
        // Lưu token — web app sẽ lấy qua JavascriptInterface khi đăng nhập
        SharedPreferences prefs = getSharedPreferences(PREF_NAME, Context.MODE_PRIVATE);
        prefs.edit().putString(KEY_FCM_TOKEN, token).apply();
    }

    // Gọi khi app đang mở (foreground) và nhận notification
    @Override
    public void onMessageReceived(RemoteMessage remoteMessage) {
        super.onMessageReceived(remoteMessage);

        String title = remoteMessage.getNotification() != null
            ? remoteMessage.getNotification().getTitle() : "My Samho";
        String body  = remoteMessage.getNotification() != null
            ? remoteMessage.getNotification().getBody()  : "";

        String linkAction = remoteMessage.getData().get("link_action");
        showNotification(title, body, linkAction);
    }

    private void showNotification(String title, String body, String linkAction) {
        NotificationManager mgr = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationChannel ch = new NotificationChannel(
                CHANNEL_ID, "Thông báo My Samho", NotificationManager.IMPORTANCE_HIGH);
            mgr.createNotificationChannel(ch);
        }

        Intent intent = new Intent(this, MainActivity.class);
        intent.addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP);
        if (linkAction != null && !linkAction.isEmpty()) {
            intent.putExtra("link_action", linkAction);
        }
        PendingIntent pi = PendingIntent.getActivity(this, 0, intent,
            PendingIntent.FLAG_ONE_SHOT | PendingIntent.FLAG_IMMUTABLE);

        NotificationCompat.Builder builder = new NotificationCompat.Builder(this, CHANNEL_ID)
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentTitle(title)
            .setContentText(body)
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setContentIntent(pi);

        mgr.notify(NOTI_ID_GEN.incrementAndGet(), builder.build());
    }

    // Static helper: lấy token đã lưu
    public static String getSavedToken(Context ctx) {
        return ctx.getSharedPreferences(PREF_NAME, Context.MODE_PRIVATE)
                  .getString(KEY_FCM_TOKEN, null);
    }
}

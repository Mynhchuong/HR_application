package com.samho.mysamho;

import android.Manifest;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.MediaStore;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Toast;
import androidx.appcompat.app.AlertDialog;
import android.content.DialogInterface;

import android.content.Context;
import android.net.ConnectivityManager;
import android.net.NetworkInfo;
import android.view.View;
import android.widget.Button;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.graphics.drawable.Drawable;
import java.io.InputStream;
import java.util.Random;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.widget.ProgressBar;
import android.webkit.DownloadListener;
import android.app.DownloadManager;
import android.os.Environment;
import android.webkit.URLUtil;
import android.webkit.CookieManager;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;

import androidx.activity.EdgeToEdge;
import androidx.activity.OnBackPressedCallback;
import androidx.annotation.Nullable;
import androidx.appcompat.app.AppCompatActivity;
import androidx.core.app.ActivityCompat;
import androidx.core.content.ContextCompat;
import androidx.core.content.FileProvider;
import androidx.core.graphics.Insets;
import androidx.core.view.ViewCompat;
import androidx.core.view.WindowInsetsCompat;

import java.io.BufferedReader;
import java.io.File;
import java.io.IOException;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;
import org.json.JSONObject;

public class MainActivity extends AppCompatActivity {

    private WebView webView;
    private LinearLayout layoutNoInternet;
    private Button btnRetry;
    private ProgressBar progressBar;
    private String selectedBaseUrl = "http://sa1-hanaro.esamho.com/hr_Web"; // Mặc định là mạng nội bộ
    private ValueCallback<Uri[]> mFilePathCallback;
    private WebChromeClient.FileChooserParams mPendingChooserParams;
    private Uri mCameraPhotoUri;
    private static final int FILE_CHOOSER_RESULT_CODE = 100;
    private static final int CAMERA_PERMISSION_REQUEST_CODE = 101;
    private static final int NOTIFICATION_PERMISSION_REQUEST_CODE = 102;
    private static final int AUDIO_PERMISSION_REQUEST_CODE = 103;
    private String pendingLinkAction = null; // từ FCM notification tap
    private boolean pendingVideoCaptureAfterPermission = false;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        EdgeToEdge.enable(this);
        setContentView(R.layout.activity_main);
        ViewCompat.setOnApplyWindowInsetsListener(findViewById(R.id.main), (v, insets) -> {
            Insets systemBars = insets.getInsets(WindowInsetsCompat.Type.systemBars());
            v.setPadding(systemBars.left, systemBars.top, systemBars.right, systemBars.bottom);
            return insets;
        });

        createNotificationChannel();
        requestNotificationPermission();
        checkAppVersion();

        webView = findViewById(R.id.webView);
        layoutNoInternet = findViewById(R.id.layoutNoInternet);
        btnRetry = findViewById(R.id.btnRetry);
        progressBar = findViewById(R.id.progressBar);

        btnRetry.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                checkNetworkAndLoadUrl();
            }
        });
        
        // Cấu hình WebView
        WebSettings webSettings = webView.getSettings();
        webSettings.setJavaScriptEnabled(true);
        webSettings.setDomStorageEnabled(true);
        webSettings.setAllowFileAccess(true);
        webSettings.setUserAgentString("MySamhoMobile/Android");
        webSettings.setTextZoom(100);
        webSettings.setUseWideViewPort(true);
        webSettings.setLoadWithOverviewMode(true);

        // Lưu link_action từ notification tap (nếu có)
        pendingLinkAction = getIntent().getStringExtra("link_action");

        // Expose FCM token to web app via Android.getFcmToken()
        webView.addJavascriptInterface(new WebAppInterface(this), "Android");
        
        webView.setWebViewClient(new WebViewClient() {
            @SuppressWarnings("deprecation")
            @Override
            public void onReceivedError(WebView view, int errorCode, String description, String failingUrl) {
                super.onReceivedError(view, errorCode, description, failingUrl);
                if (!isFinishing() && !isDestroyed()) {
                    runOnUiThread(() -> showNoInternetLayout());
                }
            }

            @Override
            public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
                super.onReceivedError(view, request, error);
                if (request.isForMainFrame() && !isFinishing() && !isDestroyed()) {
                    runOnUiThread(() -> showNoInternetLayout());
                }
            }

            @Override
            public void onPageFinished(WebView view, String url) {
                super.onPageFinished(view, url);
                // Nếu user tap notification khi app đang tắt → navigate tới trang thông báo sau khi load xong
                if (pendingLinkAction != null && url != null && !url.contains("/Notification")) {
                    String notiUrl = selectedBaseUrl + "/Notification";
                    pendingLinkAction = null;
                    view.loadUrl(notiUrl);
                }
            }
        });
        
        webView.setWebChromeClient(new WebChromeClient() {
            @Override
            public void onProgressChanged(WebView view, int newProgress) {
                if (isFinishing() || isDestroyed()) return;
                runOnUiThread(() -> {
                    if (newProgress == 100) {
                        progressBar.setVisibility(View.GONE);
                    } else {
                        progressBar.setVisibility(View.VISIBLE);
                        progressBar.setProgress(newProgress);
                    }
                });
            }

            @Override
            public boolean onShowFileChooser(WebView webView, ValueCallback<Uri[]> filePathCallback, FileChooserParams fileChooserParams) {
                if (mFilePathCallback != null) {
                    mFilePathCallback.onReceiveValue(null);
                }
                mFilePathCallback = filePathCallback;
                mPendingChooserParams = fileChooserParams;

                boolean isCapture = fileChooserParams.isCaptureEnabled();
                boolean acceptsImage = mimeTypesAccept(fileChooserParams, "image/");
                boolean acceptsVideo = mimeTypesAccept(fileChooserParams, "video/");

                if (isCapture) {
                    // Site yêu cầu mở camera trực tiếp → hỏi user chụp ảnh hay quay phim
                    if (checkCameraPermission()) {
                        showCameraOptionDialog();
                    } else {
                        requestCameraPermission();
                    }
                } else if (acceptsImage || acceptsVideo) {
                    // Web cho phép ảnh/video → hỏi rõ Chụp ảnh / Quay phim / Chọn từ thư viện
                    showMediaSourceDialog(acceptsImage, acceptsVideo);
                } else {
                    // Pick từ gallery/file
                    try {
                        startActivityForResult(fileChooserParams.createIntent(), FILE_CHOOSER_RESULT_CODE);
                    } catch (Exception e) {
                        mFilePathCallback = null;
                        return false;
                    }
                }
                return true;
            }
        });

        // Hỗ trợ tải file (Phiếu lương, tài liệu...)
        webView.setDownloadListener(new DownloadListener() {
            @Override
            public void onDownloadStart(String url, String userAgent, String contentDisposition, String mimetype, long contentLength) {
                try {
                    DownloadManager.Request request = new DownloadManager.Request(Uri.parse(url));
                    request.setMimeType(mimetype);
                    String cookies = CookieManager.getInstance().getCookie(url);
                    request.addRequestHeader("cookie", cookies);
                    request.addRequestHeader("User-Agent", userAgent);
                    request.setDescription("Đang tải file từ HR Samho...");
                    request.setTitle(URLUtil.guessFileName(url, contentDisposition, mimetype));
                    request.setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED);
                    request.setDestinationInExternalPublicDir(Environment.DIRECTORY_DOWNLOADS, URLUtil.guessFileName(url, contentDisposition, mimetype));
                    
                    DownloadManager dm = (DownloadManager) getSystemService(DOWNLOAD_SERVICE);
                    dm.enqueue(request);
                    Toast.makeText(getApplicationContext(), "Đang tải xuống...", Toast.LENGTH_SHORT).show();
                } catch (Exception e) {
                    Toast.makeText(getApplicationContext(), "Không thể tải file: " + e.getMessage(), Toast.LENGTH_LONG).show();
                }
            }
        });
        
        // Load trang web
        if (savedInstanceState != null) {
            // Xoay máy / recreate — onRestoreInstanceState sẽ restore lại WebView history
            // Khôi phục baseUrl đã chọn (nếu có)
            String savedBase = savedInstanceState.getString("selected_base_url");
            if (savedBase != null) selectedBaseUrl = savedBase;
            layoutNoInternet.setVisibility(View.GONE);
            webView.setVisibility(View.VISIBLE);
        } else if (isNetworkAvailable()) {
            showNetworkSelectionDialog();
        } else {
            showNoInternetLayout();
        }

        // Xử lý nút Back của điện thoại
        getOnBackPressedDispatcher().addCallback(this, new OnBackPressedCallback(true) {
            @Override
            public void handleOnBackPressed() {
                if (webView.canGoBack()) {
                    webView.goBack();
                } else {
                    setEnabled(false); 
                    getOnBackPressedDispatcher().onBackPressed();
                }
            }
        });
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        String linkAction = intent.getStringExtra("link_action");
        if (linkAction != null && webView != null && selectedBaseUrl != null) {
            webView.loadUrl(selectedBaseUrl + "/Notification");
        }
    }

    private void checkNetworkAndLoadUrl() {
        if (isNetworkAvailable()) {
            layoutNoInternet.setVisibility(View.GONE);
            webView.setVisibility(View.VISIBLE);
            if (webView.getUrl() == null || webView.getUrl().startsWith("file://") || webView.getUrl().startsWith("data:")) {
                webView.loadUrl(selectedBaseUrl);
            } else {
                webView.reload();
            }
        } else {
            showNoInternetLayout();
        }
    }

    private void showNetworkSelectionDialog() {
        View dialogView = getLayoutInflater().inflate(R.layout.dialog_network_selection, null);
        AlertDialog.Builder builder = new AlertDialog.Builder(this);
        builder.setView(dialogView);
        builder.setCancelable(false);

        AlertDialog dialog = builder.create();
        if (dialog.getWindow() != null) {
            dialog.getWindow().setBackgroundDrawableResource(android.R.color.transparent);
        }

        ImageView dialogImage = dialogView.findViewById(R.id.dialogImage);
        Button btnInternal = dialogView.findViewById(R.id.btnInternal);
        Button btnExternal = dialogView.findViewById(R.id.btnExternal);

        // Hiển thị hình ngẫu nhiên (downsample để tránh OOM/canvas crash)
        int[] images = {R.drawable.a_hoi, R.drawable.chi};
        int randomImageId = images[new Random().nextInt(images.length)];
        Bitmap scaled = loadScaledBitmap(randomImageId, 800, 800);
        if (scaled != null) {
            dialogImage.setImageBitmap(scaled);
        } else {
            dialogImage.setImageResource(randomImageId);
        }

        btnInternal.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                btnInternal.setEnabled(false);
                btnExternal.setEnabled(false);
                btnInternal.setText("Đang kiểm tra...");
                new Thread(() -> {
                    boolean ok = checkInternalNetwork();
                    runOnUiThread(() -> {
                        if (ok) {
                            selectedBaseUrl = "http://sa1-hanaro.esamho.com/hr_Web";
                            startApp();
                            dialog.dismiss();
                        } else {
                            btnInternal.setEnabled(true);
                            btnExternal.setEnabled(true);
                            btnInternal.setText("Mạng nội bộ Samho");
                            new AlertDialog.Builder(MainActivity.this)
                                .setTitle("Không kết nối được mạng nội bộ")
                                .setMessage("Vui lòng kết nối vào một trong hai mạng WiFi của công ty:\n\n• Juniper_Secured\n• Pluto_Secured")
                                .setPositiveButton("OK", null)
                                .show();
                        }
                    });
                }).start();
            }
        });

        btnExternal.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                selectedBaseUrl = "http://hr.esamho.com/hr_Web";
                startApp();
                dialog.dismiss();
            }
        });

        dialog.show();
    }

    private boolean checkInternalNetwork() {
        try {
            java.net.URL url = new java.net.URL("http://sa1-hanaro.esamho.com/hr_Web");
            java.net.HttpURLConnection conn = (java.net.HttpURLConnection) url.openConnection();
            conn.setConnectTimeout(3000);
            conn.setReadTimeout(3000);
            conn.setRequestMethod("HEAD");
            int code = conn.getResponseCode();
            conn.disconnect();
            return code < 500;
        } catch (Exception e) {
            return false;
        }
    }

    private void startApp() {
        layoutNoInternet.setVisibility(View.GONE);
        webView.setVisibility(View.VISIBLE);
        webView.loadUrl(selectedBaseUrl);
    }

    private void showNoInternetLayout() {
        webView.setVisibility(View.GONE);
        layoutNoInternet.setVisibility(View.VISIBLE);
    }

    private boolean isNetworkAvailable() {
        ConnectivityManager connectivityManager = (ConnectivityManager) getSystemService(Context.CONNECTIVITY_SERVICE);
        if (connectivityManager != null) {
            NetworkInfo activeNetworkInfo = connectivityManager.getActiveNetworkInfo();
            return activeNetworkInfo != null && activeNetworkInfo.isConnected();
        }
        return false;
    }

    private void requestNotificationPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS)
                    != PackageManager.PERMISSION_GRANTED) {
                ActivityCompat.requestPermissions(this,
                    new String[]{Manifest.permission.POST_NOTIFICATIONS},
                    NOTIFICATION_PERMISSION_REQUEST_CODE);
            }
        }
    }

    private boolean checkCameraPermission() {
        return ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED;
    }

    private void requestCameraPermission() {
        ActivityCompat.requestPermissions(this, new String[]{Manifest.permission.CAMERA}, CAMERA_PERMISSION_REQUEST_CODE);
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == NOTIFICATION_PERMISSION_REQUEST_CODE) {
            // Không cần xử lý gì thêm — FCM tự hoạt động nếu permission được cấp
            return;
        }
        if (requestCode == CAMERA_PERMISSION_REQUEST_CODE) {
            if (grantResults.length > 0 && grantResults[0] == PackageManager.PERMISSION_GRANTED) {
                showCameraOptionDialog();
            } else {
                Toast.makeText(this, "Quyền Camera bị từ chối", Toast.LENGTH_SHORT).show();
                cancelFileChooser();
            }
            return;
        }
        if (requestCode == AUDIO_PERMISSION_REQUEST_CODE) {
            // Dù grant hay deny đều cho phép quay (deny → video sẽ không có tiếng)
            if (pendingVideoCaptureAfterPermission) {
                pendingVideoCaptureAfterPermission = false;
                launchCameraVideo();
            }
        }
    }

    private void cancelFileChooser() {
        if (mFilePathCallback != null) {
            mFilePathCallback.onReceiveValue(null);
            mFilePathCallback = null;
        }
        mPendingChooserParams = null;
    }

    // Check accept MIME types có chứa prefix nào không (ví dụ "image/" hay "video/")
    private boolean mimeTypesAccept(WebChromeClient.FileChooserParams params, String prefix) {
        String[] types = params.getAcceptTypes();
        if (types == null) return false;
        for (String t : types) {
            if (t == null) continue;
            t = t.trim().toLowerCase();
            if (t.startsWith(prefix)) return true;
            if (t.equals("*/*")) return true;
        }
        return false;
    }

    // Hỏi user: chụp ảnh / quay phim / chọn từ thư viện
    private void showMediaSourceDialog(boolean acceptsImage, boolean acceptsVideo) {
        java.util.List<String> labels  = new java.util.ArrayList<>();
        java.util.List<Integer> actions = new java.util.ArrayList<>(); // 0=photo, 1=video, 2=gallery
        if (acceptsImage) { labels.add("📷 Chụp ảnh");        actions.add(0); }
        if (acceptsVideo) { labels.add("🎥 Quay phim");       actions.add(1); }
        labels.add("🖼️ Chọn từ thư viện / File"); actions.add(2);

        new AlertDialog.Builder(this)
                .setTitle("Chọn nguồn")
                .setItems(labels.toArray(new String[0]), (dialog, which) -> {
                    int action = actions.get(which);
                    if (action == 0) {
                        if (checkCameraPermission()) launchCameraPhoto();
                        else requestCameraPermission();
                    } else if (action == 1) {
                        if (!checkCameraPermission()) { requestCameraPermission(); return; }
                        if (checkAudioPermission()) launchCameraVideo();
                        else {
                            pendingVideoCaptureAfterPermission = true;
                            ActivityCompat.requestPermissions(this,
                                    new String[]{Manifest.permission.RECORD_AUDIO},
                                    AUDIO_PERMISSION_REQUEST_CODE);
                        }
                    } else {
                        // Chọn từ thư viện — dùng intent từ chooserParams
                        if (mPendingChooserParams != null) {
                            try {
                                startActivityForResult(mPendingChooserParams.createIntent(), FILE_CHOOSER_RESULT_CODE);
                            } catch (Exception e) { cancelFileChooser(); }
                        } else { cancelFileChooser(); }
                    }
                })
                .setOnCancelListener(d -> cancelFileChooser())
                .show();
    }

    private boolean checkAudioPermission() {
        return ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED;
    }

    private void showCameraOptionDialog() {
        String[] options = { "📷 Chụp ảnh", "🎥 Quay phim" };
        new AlertDialog.Builder(this)
                .setTitle("Dùng camera để...")
                .setItems(options, (dialog, which) -> {
                    if (which == 0) {
                        launchCameraPhoto();
                    } else {
                        if (checkAudioPermission()) {
                            launchCameraVideo();
                        } else {
                            pendingVideoCaptureAfterPermission = true;
                            ActivityCompat.requestPermissions(this,
                                    new String[]{Manifest.permission.RECORD_AUDIO},
                                    AUDIO_PERMISSION_REQUEST_CODE);
                        }
                    }
                })
                .setOnCancelListener(d -> cancelFileChooser())
                .show();
    }

    private void launchCameraPhoto() {
        Intent takePictureIntent = new Intent(MediaStore.ACTION_IMAGE_CAPTURE);
        if (takePictureIntent.resolveActivity(getPackageManager()) == null) {
            Toast.makeText(this, "Không có app camera", Toast.LENGTH_SHORT).show();
            cancelFileChooser();
            return;
        }
        File photoFile = null;
        try {
            photoFile = File.createTempFile("camera_image_", ".jpg", getExternalCacheDir());
        } catch (IOException ignored) { }

        if (photoFile != null) {
            mCameraPhotoUri = FileProvider.getUriForFile(this,
                    getApplicationContext().getPackageName() + ".fileprovider",
                    photoFile);
            takePictureIntent.putExtra(MediaStore.EXTRA_OUTPUT, mCameraPhotoUri);
            takePictureIntent.addFlags(Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
            startActivityForResult(takePictureIntent, FILE_CHOOSER_RESULT_CODE);
        } else {
            cancelFileChooser();
        }
    }

    private void launchCameraVideo() {
        Intent takeVideoIntent = new Intent(MediaStore.ACTION_VIDEO_CAPTURE);
        if (takeVideoIntent.resolveActivity(getPackageManager()) == null) {
            Toast.makeText(this, "Không có app quay phim", Toast.LENGTH_SHORT).show();
            cancelFileChooser();
            return;
        }
        File videoFile = null;
        try {
            videoFile = File.createTempFile("camera_video_", ".mp4", getExternalCacheDir());
        } catch (IOException ignored) { }

        if (videoFile != null) {
            mCameraPhotoUri = FileProvider.getUriForFile(this,
                    getApplicationContext().getPackageName() + ".fileprovider",
                    videoFile);
            takeVideoIntent.putExtra(MediaStore.EXTRA_OUTPUT, mCameraPhotoUri);
            takeVideoIntent.addFlags(Intent.FLAG_GRANT_WRITE_URI_PERMISSION);
            takeVideoIntent.putExtra(MediaStore.EXTRA_VIDEO_QUALITY, 1);          // high
            takeVideoIntent.putExtra(MediaStore.EXTRA_DURATION_LIMIT, 60);        // max 60s
            takeVideoIntent.putExtra(MediaStore.EXTRA_SIZE_LIMIT, 50L * 1024 * 1024); // 50MB
            startActivityForResult(takeVideoIntent, FILE_CHOOSER_RESULT_CODE);
        } else {
            cancelFileChooser();
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (webView != null) webView.onResume();
    }

    @Override
    protected void onPause() {
        super.onPause();
        if (webView != null) webView.onPause();
    }

    @Override
    protected void onSaveInstanceState(@androidx.annotation.NonNull Bundle outState) {
        super.onSaveInstanceState(outState);
        if (webView != null) webView.saveState(outState);
        if (selectedBaseUrl != null) outState.putString("selected_base_url", selectedBaseUrl);
    }

    @Override
    protected void onRestoreInstanceState(@androidx.annotation.NonNull Bundle savedInstanceState) {
        super.onRestoreInstanceState(savedInstanceState);
        if (webView != null) webView.restoreState(savedInstanceState);
    }

    @Override
    protected void onDestroy() {
        if (webView != null) {
            webView.stopLoading();
            webView.setWebViewClient(null);
            webView.setWebChromeClient(null);
            webView.destroy();
            webView = null;
        }
        super.onDestroy();
    }

    private Bitmap loadScaledBitmap(int resId, int maxWidth, int maxHeight) {
        try {
            BitmapFactory.Options options = new BitmapFactory.Options();
            options.inJustDecodeBounds = true;
            BitmapFactory.decodeResource(getResources(), resId, options);
            options.inSampleSize = calculateInSampleSize(options, maxWidth, maxHeight);
            options.inJustDecodeBounds = false;
            return BitmapFactory.decodeResource(getResources(), resId, options);
        } catch (Exception e) {
            return null;
        }
    }

    private int calculateInSampleSize(BitmapFactory.Options options, int reqWidth, int reqHeight) {
        int inSampleSize = 1;
        if (options.outHeight > reqHeight || options.outWidth > reqWidth) {
            final int halfHeight = options.outHeight / 2;
            final int halfWidth = options.outWidth / 2;
            while ((halfHeight / inSampleSize) >= reqHeight && (halfWidth / inSampleSize) >= reqWidth) {
                inSampleSize *= 2;
            }
        }
        return inSampleSize;
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, @Nullable Intent data) {
        if (requestCode == FILE_CHOOSER_RESULT_CODE) {
            if (mFilePathCallback == null) {
                super.onActivityResult(requestCode, resultCode, data);
                return;
            }

            Uri[] results = null;

            if (resultCode == RESULT_OK) {
                if (data == null || data.getData() == null) {
                    if (mCameraPhotoUri != null) {
                        results = new Uri[]{mCameraPhotoUri};
                    }
                } else {
                    String dataString = data.getDataString();
                    if (dataString != null) {
                        results = new Uri[]{Uri.parse(dataString)};
                    }
                }
            }

            mFilePathCallback.onReceiveValue(results);
            mFilePathCallback = null;
            mPendingChooserParams = null;
        } else {
            super.onActivityResult(requestCode, resultCode, data);
        }
    }

    private void createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationChannel ch = new NotificationChannel(
                "mysamho_noti", "Thông báo My Samho", NotificationManager.IMPORTANCE_HIGH);
            ch.enableVibration(true);
            NotificationManager mgr = getSystemService(NotificationManager.class);
            mgr.createNotificationChannel(ch);
        }
    }

    // ───────────────────────────────────────────────────────────────
    // App version check — bắt cập nhật nếu version hiện tại < min trên server
    // ───────────────────────────────────────────────────────────────
    private void checkAppVersion() {
        new Thread(() -> {
            try {
                String currentVersion;
                try {
                    currentVersion = getPackageManager()
                            .getPackageInfo(getPackageName(), 0).versionName;
                } catch (PackageManager.NameNotFoundException e) { currentVersion = "0.0"; }

                // /hr_Web → /HR_api/apiHR
                String apiBase = selectedBaseUrl.replace("/hr_Web", "/HR_api/apiHR");
                String urlStr  = apiBase + "/AppVersion/check?platform=android&version=" + currentVersion;

                HttpURLConnection conn = (HttpURLConnection) new URL(urlStr).openConnection();
                conn.setRequestMethod("GET");
                conn.setRequestProperty("X-Api-Key", "ac36484900c47f33feb65c507a7706ccf1bb0d22f18a13f9aea117a2a21e6dc9");
                conn.setConnectTimeout(5000);
                conn.setReadTimeout(5000);

                StringBuilder sb = new StringBuilder();
                try (BufferedReader br = new BufferedReader(new InputStreamReader(conn.getInputStream()))) {
                    String line; while ((line = br.readLine()) != null) sb.append(line);
                }

                JSONObject json = new JSONObject(sb.toString());
                if (!json.optBoolean("success", false)) return;
                if (!json.optBoolean("forceUpdate", false)) return;

                String latest = json.optString("latestVersion", "?");
                String note   = json.optString("releaseNote", "Vui lòng cập nhật để tiếp tục sử dụng.");
                String update = json.optString("updateUrl", "");

                runOnUiThread(() -> showForceUpdateDialog(latest, note, update));
            } catch (Exception ignored) {
                // Không có mạng / API down → bỏ qua, không chặn user
            }
        }).start();
    }

    private void showForceUpdateDialog(String latestVersion, String note, String updateUrl) {
        new AlertDialog.Builder(this)
            .setTitle("Có bản cập nhật mới (v" + latestVersion + ")")
            .setMessage(note)
            .setCancelable(false)
            .setPositiveButton("Cập nhật ngay", (d, w) -> {
                try {
                    startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(updateUrl)));
                } catch (Exception ignored) {}
                finish();   // bắt buộc thoát app, không cho dùng tiếp
            })
            .setNegativeButton("Thoát", (d, w) -> finish())
            .show();
    }

    // JavaScript interface — web app gọi Android.getFcmToken() / clearNotifications()
    public static class WebAppInterface {
        private final Context mContext;
        WebAppInterface(Context ctx) { mContext = ctx; }

        @android.webkit.JavascriptInterface
        public String getFcmToken() {
            return MySamhoFirebaseService.getSavedToken(mContext);
        }

        // Gọi khi user logout — xoá hết noti FCM còn trong tray của thiết bị
        @android.webkit.JavascriptInterface
        public void clearAllNotifications() {
            NotificationManager mgr = (NotificationManager) mContext.getSystemService(Context.NOTIFICATION_SERVICE);
            if (mgr != null) mgr.cancelAll();
        }
    }
}
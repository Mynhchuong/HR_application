package com.samho.mysamho;

import android.Manifest;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
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

import java.io.File;
import java.io.IOException;

public class MainActivity extends AppCompatActivity {

    private WebView webView;
    private LinearLayout layoutNoInternet;
    private Button btnRetry;
    private ProgressBar progressBar;
    private String selectedBaseUrl = "http://192.168.1.24/HR_Web"; // Mặc định là mạng nội bộ
    private ValueCallback<Uri[]> mFilePathCallback;
    private Uri mCameraPhotoUri;
    private static final int FILE_CHOOSER_RESULT_CODE = 100;
    private static final int CAMERA_PERMISSION_REQUEST_CODE = 101;

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
        webSettings.setUserAgentString("MySamhoMobile");
        
        webView.setWebViewClient(new WebViewClient() {
            @SuppressWarnings("deprecation")
            @Override
            public void onReceivedError(WebView view, int errorCode, String description, String failingUrl) {
                super.onReceivedError(view, errorCode, description, failingUrl);
                showNoInternetLayout();
            }

            @Override
            public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
                super.onReceivedError(view, request, error);
                if (request.isForMainFrame()) {
                    showNoInternetLayout();
                }
            }
        });
        
        webView.setWebChromeClient(new WebChromeClient() {
            @Override
            public void onProgressChanged(WebView view, int newProgress) {
                if (newProgress == 100) {
                    progressBar.setVisibility(View.GONE);
                } else {
                    progressBar.setVisibility(View.VISIBLE);
                    progressBar.setProgress(newProgress);
                }
            }

            @Override
            public boolean onShowFileChooser(WebView webView, ValueCallback<Uri[]> filePathCallback, FileChooserParams fileChooserParams) {
                if (mFilePathCallback != null) {
                    mFilePathCallback.onReceiveValue(null);
                }
                mFilePathCallback = filePathCallback;

                boolean isCapture = fileChooserParams.isCaptureEnabled();

                if (isCapture) {
                    if (checkCameraPermission()) {
                        launchCamera();
                    } else {
                        requestCameraPermission();
                    }
                } else {
                    Intent intent = fileChooserParams.createIntent();
                    try {
                        startActivityForResult(intent, FILE_CHOOSER_RESULT_CODE);
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
                    request.allowScanningByMediaScanner();
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
        if (isNetworkAvailable()) {
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

        // Hiển thị hình ngẫu nhiên
        int[] images = {R.drawable.a_hoi, R.drawable.chi};
        int randomImageId = images[new Random().nextInt(images.length)];
        dialogImage.setImageResource(randomImageId);

        btnInternal.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                selectedBaseUrl = "http://192.168.1.24/HR_Web";
                startApp();
                dialog.dismiss();
            }
        });

        btnExternal.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                selectedBaseUrl = "http://103.82.204.247/HR_Web";
                startApp();
                dialog.dismiss();
            }
        });

        dialog.show();
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

    private boolean checkCameraPermission() {
        return ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED;
    }

    private void requestCameraPermission() {
        ActivityCompat.requestPermissions(this, new String[]{Manifest.permission.CAMERA}, CAMERA_PERMISSION_REQUEST_CODE);
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == CAMERA_PERMISSION_REQUEST_CODE) {
            if (grantResults.length > 0 && grantResults[0] == PackageManager.PERMISSION_GRANTED) {
                launchCamera();
            } else {
                Toast.makeText(this, "Quyền Camera bị từ chối", Toast.LENGTH_SHORT).show();
                if (mFilePathCallback != null) {
                    mFilePathCallback.onReceiveValue(null);
                    mFilePathCallback = null;
                }
            }
        }
    }

    private void launchCamera() {
        Intent takePictureIntent = new Intent(MediaStore.ACTION_IMAGE_CAPTURE);
        if (takePictureIntent.resolveActivity(getPackageManager()) != null) {
            File photoFile = null;
            try {
                photoFile = File.createTempFile("camera_image_", ".jpg", getExternalCacheDir());
            } catch (IOException ex) {
                // Error occurred
            }

            if (photoFile != null) {
                mCameraPhotoUri = FileProvider.getUriForFile(this,
                        getApplicationContext().getPackageName() + ".fileprovider",
                        photoFile);
                takePictureIntent.putExtra(MediaStore.EXTRA_OUTPUT, mCameraPhotoUri);
                startActivityForResult(takePictureIntent, FILE_CHOOSER_RESULT_CODE);
            } else {
                if (mFilePathCallback != null) {
                    mFilePathCallback.onReceiveValue(null);
                    mFilePathCallback = null;
                }
            }
        } else {
            if (mFilePathCallback != null) {
                mFilePathCallback.onReceiveValue(null);
                mFilePathCallback = null;
            }
        }
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
        } else {
            super.onActivityResult(requestCode, resultCode, data);
        }
    }
}
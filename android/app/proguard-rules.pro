# Giữ lại các thuộc tính quan trọng cho WebView và JavaScript
-keepattributes EnclosingMethod,InnerClasses,Signature,Annotations,SourceFile,LineNumberTable

# Nếu bạn có dùng @JavascriptInterface sau này, quy tắc này rất quan trọng
-keepclassmembers class * {
    @android.webkit.JavascriptInterface <methods>;
}

# Giữ lại các class của AndroidX và Material để tránh lỗi runtime
-keep class androidx.appcompat.** { *; }
-keep class com.google.android.material.** { *; }

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.google.services)
}

android {
    namespace = "com.samho.mysamho"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.samho.mysamho"
        minSdk = 24
        targetSdk = 36
        versionCode = 12
        versionName = "10.2"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_11
        targetCompatibility = JavaVersion.VERSION_11
    }
}

dependencies {
    implementation(platform(libs.firebase.bom))
    implementation(libs.firebase.messaging)
    implementation(libs.firebase.analytics)
    implementation(libs.appcompat)
    implementation(libs.material)
    implementation(libs.activity)
    implementation(libs.constraintlayout)
    implementation(libs.play.services.code.scanner) // Quét mã vạch tem máy (TPM) bằng Google Code Scanner
    implementation(libs.biometric)        // Đăng nhập lại bằng vân tay/Face Unlock
    implementation(libs.security.crypto)  // EncryptedSharedPreferences — lưu mã NV/mật khẩu an toàn
    testImplementation(libs.junit)
    androidTestImplementation(libs.ext.junit)
    androidTestImplementation(libs.espresso.core)
}
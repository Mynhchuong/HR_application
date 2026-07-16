namespace HR_web.Models.Training;

// Model cho partial _TrainingHeroBanner.cshtml — banner lớn dùng riêng cho 4 trang "cổng vào module"
// (MyClasses học viên/giáo viên, TrainingAdmin Index, Certificates), có ảnh minh họa bên phải.
public class TrainingHeroBannerVM
{
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public string Gradient { get; set; } = "primary"; // primary|success|warning|info|dark|danger
    public string? IllustrationImg { get; set; } // vd "~/assets/img/illustrations/hocsinh.png", null = không có ảnh
}

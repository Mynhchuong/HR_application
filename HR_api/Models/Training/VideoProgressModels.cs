namespace HR_api.Models.Training;

// HR_TRAINING_VIDEO_PROGRESS — tiến độ xem video từng buổi (đào tạo online)
public class UpdateVideoProgressRequest
{
    public int    MATERIAL_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public int    POSITION_SEC { get; set; }
    public int    DURATION_SEC { get; set; }
    public bool   IS_ENDED { get; set; }     // true khi client bắn event 'ended'
}

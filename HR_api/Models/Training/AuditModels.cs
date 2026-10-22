namespace HR_api.Models.Training;

// HR_TRAINING_AUDIT — log các hành động "force" nhạy cảm (phục hồi enrollment đã drop,
// xóa học viên đã COMPLETED/đã cấp chứng chỉ) — trước đây chỉ INSERT, không có UI xem lại.
public class TrainingAuditItem
{
    public int      ID             { get; set; }
    public string   ACTION         { get; set; } = "";
    public int?     CLASS_ID       { get; set; }
    public string?  CLASS_NAME     { get; set; }
    public int?     SESSION_ID     { get; set; }
    public int?     TEST_ID        { get; set; }
    public int      AFFECTED_COUNT { get; set; }
    public string?  PAYLOAD        { get; set; }
    public string   ACTOR_EMPCD    { get; set; } = "";
    public string?  ACTOR_NAME     { get; set; }
    public DateTime INST_DT        { get; set; }
}

public class TrainingAuditPage
{
    public List<TrainingAuditItem> DATA  { get; set; } = new();
    public int TOTAL       { get; set; }
    public int PAGE        { get; set; }
    public int PAGE_SIZE   { get; set; }
    public int TOTAL_PAGES { get; set; }
}

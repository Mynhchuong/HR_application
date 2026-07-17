namespace HR_web.Models.Shared;

// Model cho partial _QaHeroCard.cshtml — thẻ hero gradient xanh lá dùng chung cho 2 trang
// Q&A của Training (Training/QA.cshtml học viên hỏi, TrainingTeach/QA.cshtml giáo viên trả lời)
// thay vì mỗi trang tự khai báo cùng 1 đoạn gradient.
public class QaHeroCardVM
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
}

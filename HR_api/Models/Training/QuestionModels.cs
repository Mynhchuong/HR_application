namespace HR_api.Models.Training;

// HR_TRAINING_QUESTION (§9) — Q&A flat list, không nested reply
public class QuestionModel
{
    public int    ID { get; set; }
    public int    CLASS_ID { get; set; }
    public string ASKED_BY { get; set; } = "";
    public string? ASKED_BY_NAME { get; set; }
    public string QUESTION_TEXT { get; set; } = "";
    public DateTime ASKED_DT { get; set; }
    public string? ANSWERED_BY { get; set; }
    public string? ANSWERED_BY_NAME { get; set; }
    public string? ANSWER_TEXT { get; set; }
    public DateTime? ANSWERED_DT { get; set; }
    public int    IS_DELETED { get; set; }
}

public class AskQuestionRequest
{
    public int    CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";              // asker
    public string QUESTION_TEXT { get; set; } = "";
}

public class AnswerQuestionRequest
{
    public int    ID { get; set; }
    public string ANSWER_TEXT { get; set; } = "";
    public string LOGIN_USER { get; set; } = "";         // teacher EMPCD
}

public class DeleteQuestionRequest
{
    public int    ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

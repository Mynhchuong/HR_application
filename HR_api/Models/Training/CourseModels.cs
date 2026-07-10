namespace HR_api.Models.Training;

// HR_TRAINING_COURSE — giáo trình template (xem create_training.sql + training_rules.md §2)
public class CourseModel
{
    public int    ID { get; set; }
    public string TITLE { get; set; } = "";
    public string? DESCRIPTION { get; set; }
    public string? OBJECTIVES { get; set; }
    public string? CATEGORY { get; set; }

    // STANDARD | EXPRESS (§2.1)
    public string COURSE_MODE { get; set; } = "STANDARD";
    public int?   DEFAULT_DURATION_MIN { get; set; }

    // Auto-open recurring (§2.2 + §15b Cách 3) — áp dụng cả STANDARD lẫn EXPRESS (2026-07-06)
    public int    AUTO_OPEN_MONTHLY { get; set; }
    public int?   AUTO_OPEN_DAY { get; set; }

    public decimal? DEFAULT_PASS_SCORE { get; set; }
    public decimal? DEFAULT_MIN_ATTEND_PCT { get; set; }

    public int    IS_ACTIVE { get; set; } = 1;

    public string? INST_ID { get; set; }
    public DateTime? INST_DT { get; set; }
    public string? UPDT_ID { get; set; }
    public DateTime? UPDT_DT { get; set; }

    // Computed cho UI list — không lưu DB
    public int? CLASS_COUNT { get; set; }
    public int? TEMPLATE_SESSION_COUNT { get; set; }
}

// Template session của Course (STANDARD mode) — clone ra Class sessions khi HR mở đợt mới.
public class CourseSessionTemplateModel
{
    public int    ID { get; set; }
    public int    COURSE_ID { get; set; }
    public int    SESSION_NO { get; set; }
    public int    DAY_OFFSET { get; set; }    // ngày lệch từ START_DATE (0 = ngày đầu)
    public string START_TIME { get; set; } = "0800";   // HHMM
    public string END_TIME { get; set; } = "1130";
    public string? TOPIC { get; set; }
    public string? LOCATION { get; set; }
}

public class SaveCourseRequest
{
    public int?   ID { get; set; }             // null = create, có ID = update
    public string TITLE { get; set; } = "";
    public string? DESCRIPTION { get; set; }
    public string? OBJECTIVES { get; set; }
    public string? CATEGORY { get; set; }
    public string COURSE_MODE { get; set; } = "STANDARD";
    public int?   DEFAULT_DURATION_MIN { get; set; }
    public int    AUTO_OPEN_MONTHLY { get; set; }
    public int?   AUTO_OPEN_DAY { get; set; }
    public decimal? DEFAULT_PASS_SCORE { get; set; }
    public decimal? DEFAULT_MIN_ATTEND_PCT { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class SaveSessionTemplateRequest
{
    public int    COURSE_ID { get; set; }
    public List<CourseSessionTemplateModel> SESSIONS { get; set; } = new();
    public string LOGIN_USER { get; set; } = "";
}

public class ArchiveCourseRequest
{
    public int    ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

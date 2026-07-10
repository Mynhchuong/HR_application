namespace HR_api.Models.Training;

// HR_TRAINING_MATERIAL (§8) — level COURSE hoặc CLASS
public class MaterialModel
{
    public int    ID { get; set; }
    public string MATERIAL_LEVEL { get; set; } = "CLASS";   // COURSE | CLASS
    public int?   COURSE_ID { get; set; }
    public int?   CLASS_ID { get; set; }

    public string TITLE { get; set; } = "";
    public string FILE_NAME { get; set; } = "";
    public string FILE_TYPE { get; set; } = "PDF";          // PDF | DOCX | MP4 | IMG | LINK
    public string? FILE_URL { get; set; }

    public int    IS_REQUIRED { get; set; }
    public int    DISPLAY_ORDER { get; set; }

    public string? INST_ID { get; set; }
    public DateTime? INST_DT { get; set; }

    // Denormalised for user view
    public int?   VIEW_COUNT { get; set; }     // tổng NV đã xem (report §8)
    public int?   HAS_VIEWED { get; set; }     // 1 nếu current user đã view (0 else)
}

public class SaveMaterialRequest
{
    public int?   ID { get; set; }
    public string MATERIAL_LEVEL { get; set; } = "CLASS";
    public int?   COURSE_ID { get; set; }
    public int?   CLASS_ID { get; set; }
    public string TITLE { get; set; } = "";
    public string FILE_NAME { get; set; } = "";
    public string FILE_TYPE { get; set; } = "PDF";
    public string? FILE_URL { get; set; }
    public int    IS_REQUIRED { get; set; }
    public int    DISPLAY_ORDER { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class DeleteMaterialRequest
{
    public int    ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class MaterialViewRequest
{
    public int    MATERIAL_ID { get; set; }
    public string EMPCD { get; set; } = "";
}

using System.Globalization;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace HR_web.Helpers;

/// <summary>
/// Thêm biểu đồ Excel THẬT (sửa được, hover được) vào file .xlsx do ClosedXML tạo — ClosedXML không hỗ trợ biểu đồ nên ghi
/// thẳng XML biểu đồ bằng Open XML SDK. Dữ liệu biểu đồ lấy từ các ô trong workbook (sheet "Dữ liệu biểu đồ"), không nhúng ảnh.
/// </summary>
public static class ExcelChartBuilder
{
    public enum Kind { Col, ColStacked, Bar, Line, Pie, Doughnut }

    public class Series
    {
        public string Name { get; set; } = "";
        public string ValRef { get; set; } = "";
        public string Color { get; set; } = "6366F1";
        public bool AsLine { get; set; }          // đường trong biểu đồ cột (cùng trục)
        public bool Labels { get; set; }          // hiện số trên cột/điểm
    }

    public class Spec
    {
        public string Title { get; set; } = "";
        public Kind Kind { get; set; }
        public string CatRef { get; set; } = "";
        public List<Series> Series { get; set; } = new();
        public string[] PointColors { get; set; } = Array.Empty<string>();   // pie/doughnut
        public int Col { get; set; }              // ô neo (0-based)
        public int Row { get; set; }
        public int ColSpan { get; set; } = 9;
        public int RowSpan { get; set; } = 18;
    }

    public static readonly string[] Palette = { "6366F1", "DC2626", "10B981", "F59E0B", "0EA5E9", "EC4899", "64748B", "8B5CF6", "14B8A6", "F97316" };

    public static byte[] AddCharts(byte[] xlsx, string sheetName, IReadOnlyList<Spec> specs)
    {
        if (specs.Count == 0) return xlsx;
        using var ms = new MemoryStream();
        ms.Write(xlsx, 0, xlsx.Length); ms.Position = 0;
        using (var doc = SpreadsheetDocument.Open(ms, true))
        {
            var wb = doc.WorkbookPart!;
            var sheet = wb.Workbook.Descendants<Sheet>().First(s => s.Name == sheetName);
            var ws = (WorksheetPart)wb.GetPartById(sheet.Id!);
            var dp = ws.AddNewPart<DrawingsPart>();

            var sheetCache = new Dictionary<string, Dictionary<string, string>>();
            Func<string, List<string>> readRange = rf => ReadRange(wb, rf, sheetCache);

            var anchors = new StringBuilder();
            int n = 1;
            foreach (var sp in specs)
            {
                var cp = dp.AddNewPart<ChartPart>();
                using (var w = new StreamWriter(cp.GetStream(FileMode.Create), new UTF8Encoding(false))) w.Write(ChartXml(sp, readRange));
                string rid = dp.GetIdOfPart(cp);
                anchors.Append($@"<xdr:twoCellAnchor><xdr:from><xdr:col>{sp.Col}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{sp.Row}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
<xdr:to><xdr:col>{sp.Col + sp.ColSpan}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{sp.Row + sp.RowSpan}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
<xdr:graphicFrame macro=""""><xdr:nvGraphicFramePr><xdr:cNvPr id=""{n + 1}"" name=""Chart {n}""/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr>
<xdr:xfrm><a:off x=""0"" y=""0""/><a:ext cx=""0"" cy=""0""/></xdr:xfrm><a:graphic><a:graphicData uri=""http://schemas.openxmlformats.org/drawingml/2006/chart"">
<c:chart xmlns:c=""http://schemas.openxmlformats.org/drawingml/2006/chart"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"" r:id=""{rid}""/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor>");
                n++;
            }
            string drawing = $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<xdr:wsDr xmlns:xdr=""http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"" xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"">{anchors}</xdr:wsDr>";
            using (var w = new StreamWriter(dp.GetStream(FileMode.Create), new UTF8Encoding(false))) w.Write(drawing);

            // <drawing> PHẢI đứng trước tableParts/legacyDrawing/picture/oleObjects/controls/extLst theo thứ tự
            // schema CT_Worksheet. ClosedXML luôn ghi sẵn <tableParts count="0"/>, nên Append() sẽ đặt <drawing>
            // SAU tableParts → file sai thứ tự: Excel tự vá nên vẫn thấy biểu đồ, nhưng Numbers (macOS) và
            // một số trình xem khác bỏ qua luôn, sheet "Biểu đồ" hiện trắng (lỗi phát hiện 2026-11-13).
            var dwg = new DocumentFormat.OpenXml.Spreadsheet.Drawing { Id = ws.GetIdOfPart(dp) };
            var mustFollow = ws.Worksheet.ChildElements.FirstOrDefault(e =>
                   e is DocumentFormat.OpenXml.Spreadsheet.TableParts
                || e is DocumentFormat.OpenXml.Spreadsheet.LegacyDrawing
                || e is DocumentFormat.OpenXml.Spreadsheet.LegacyDrawingHeaderFooter
                || e is DocumentFormat.OpenXml.Spreadsheet.DrawingHeaderFooter
                || e is DocumentFormat.OpenXml.Spreadsheet.Picture
                || e is DocumentFormat.OpenXml.Spreadsheet.OleObjects
                || e is DocumentFormat.OpenXml.Spreadsheet.Controls
                || e is DocumentFormat.OpenXml.Spreadsheet.WebPublishItems
                || e is DocumentFormat.OpenXml.Spreadsheet.WorksheetExtensionList);
            if (mustFollow != null) ws.Worksheet.InsertBefore(dwg, mustFollow);
            else                    ws.Worksheet.Append(dwg);
            ws.Worksheet.Save();
        }
        return ms.ToArray();
    }

    private static string E(string s) => SecurityElement.Escape(s) ?? "";

    // ── Cache dữ liệu trong chart ────────────────────────────────────────────
    // Excel tự tính lại số từ công thức nên chart vẫn hiện dù không có cache; NHƯNG Numbers (macOS)
    // và nhiều trình xem khác chỉ đọc phần cache, không tính công thức → chart ra TRẮNG
    // (lỗi người dùng báo 2026-11-13). Vì vậy mỗi chuỗi số liệu phải kèm <c:strCache>/<c:numCache>.
    private static string StrRef(string refStr, Func<string, List<string>> read)
    {
        var vals = read(refStr);
        var sb = new StringBuilder();
        sb.Append($"<c:strRef><c:f>{E(refStr)}</c:f><c:strCache><c:ptCount val=\"{vals.Count}\"/>");
        for (int i = 0; i < vals.Count; i++) sb.Append($"<c:pt idx=\"{i}\"><c:v>{E(vals[i])}</c:v></c:pt>");
        sb.Append("</c:strCache></c:strRef>");
        return sb.ToString();
    }

    private static string NumRef(string refStr, Func<string, List<string>> read)
    {
        var vals = read(refStr);
        var sb = new StringBuilder();
        sb.Append($"<c:numRef><c:f>{E(refStr)}</c:f><c:numCache><c:formatCode>General</c:formatCode><c:ptCount val=\"{vals.Count}\"/>");
        for (int i = 0; i < vals.Count; i++)
            if (double.TryParse(vals[i], NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                sb.Append($"<c:pt idx=\"{i}\"><c:v>{d.ToString(CultureInfo.InvariantCulture)}</c:v></c:pt>");
        sb.Append("</c:numCache></c:numRef>");
        return sb.ToString();
    }

    // Đọc giá trị các ô của 1 vùng dạng 'Tên sheet'!$A$2:$A$8 ngay trong file vừa lưu.
    // Làm ở đây để mọi nơi gọi AddCharts không phải truyền thêm dữ liệu — chart luôn khớp đúng ô đang trỏ tới.
    private static List<string> ReadRange(WorkbookPart wb, string refStr, Dictionary<string, Dictionary<string, string>> cache)
    {
        var result = new List<string>();
        try
        {
            int bang = refStr.LastIndexOf('!');
            if (bang < 0) return result;
            string sheetName = refStr.Substring(0, bang).Trim();
            if (sheetName.StartsWith("'") && sheetName.EndsWith("'"))
                sheetName = sheetName.Substring(1, sheetName.Length - 2).Replace("''", "'");

            string rng = refStr.Substring(bang + 1).Replace("$", "");
            var parts = rng.Split(':');
            if (!TryCell(parts[0], out int c1, out int r1)) return result;
            if (!TryCell(parts.Length > 1 ? parts[1] : parts[0], out int c2, out int r2)) return result;

            if (!cache.TryGetValue(sheetName, out var map))
            {
                map = new Dictionary<string, string>();
                var sh = wb.Workbook.Descendants<Sheet>().FirstOrDefault(x => x.Name == sheetName);
                if (sh != null)
                {
                    var wsp = (WorksheetPart)wb.GetPartById(sh.Id!);
                    var sst = wb.SharedStringTablePart?.SharedStringTable;
                    foreach (var cell in wsp.Worksheet.Descendants<Cell>())
                    {
                        var cr = cell.CellReference?.Value;
                        if (cr != null) map[cr] = CellText(cell, sst);
                    }
                }
                cache[sheetName] = map;
            }

            for (int r = r1; r <= r2; r++)
                for (int c = c1; c <= c2; c++)
                    result.Add(map.TryGetValue(ColName(c) + r, out var v) ? v : "");
        }
        catch { }
        return result;
    }

    private static string CellText(Cell cell, SharedStringTable? sst)
    {
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (sst != null && int.TryParse(cell.CellValue?.Text, out int ix) && ix >= 0 && ix < sst.ChildElements.Count)
                return sst.ChildElements[ix].InnerText;
            return "";
        }
        if (cell.DataType?.Value == CellValues.InlineString) return cell.InlineString?.Text?.Text ?? "";
        return cell.CellValue?.Text ?? "";
    }

    private static bool TryCell(string a1, out int col, out int row)
    {
        col = 0; row = 0;
        int i = 0;
        while (i < a1.Length && char.IsLetter(a1[i])) { col = col * 26 + (char.ToUpperInvariant(a1[i]) - 'A' + 1); i++; }
        return col > 0 && int.TryParse(a1.Substring(i), out row) && row > 0;
    }

    private static string ColName(int col)
    {
        var sb = new StringBuilder();
        while (col > 0) { int m = (col - 1) % 26; sb.Insert(0, (char)('A' + m)); col = (col - 1) / 26; }
        return sb.ToString();
    }


    private static string SerXml(Spec sp, Series se, int idx, bool line, Func<string, List<string>> read)
    {
        var sb = new StringBuilder();
        sb.Append($"<c:ser><c:idx val=\"{idx}\"/><c:order val=\"{idx}\"/><c:tx><c:v>{E(se.Name)}</c:v></c:tx>");
        if (line)
            sb.Append($"<c:spPr><a:ln w=\"28575\" cap=\"rnd\"><a:solidFill><a:srgbClr val=\"{se.Color}\"/></a:solidFill><a:round/></a:ln></c:spPr><c:marker><c:symbol val=\"circle\"/><c:size val=\"5\"/><c:spPr><a:solidFill><a:srgbClr val=\"{se.Color}\"/></a:solidFill></c:spPr></c:marker>");
        else
            sb.Append($"<c:spPr><a:solidFill><a:srgbClr val=\"{se.Color}\"/></a:solidFill></c:spPr><c:invertIfNegative val=\"0\"/>");
        if (se.Labels)
            sb.Append("<c:dLbls><c:numFmt formatCode=\"#,##0\" sourceLinked=\"0\"/><c:spPr><a:noFill/></c:spPr><c:txPr><a:bodyPr/><a:p><a:pPr><a:defRPr sz=\"800\"/></a:pPr><a:endParaRPr lang=\"vi-VN\"/></a:p></c:txPr>"
                    + (line ? "<c:dLblPos val=\"t\"/>" : "<c:dLblPos val=\"outEnd\"/>")
                    + "<c:showLegendKey val=\"0\"/><c:showVal val=\"1\"/><c:showCatName val=\"0\"/><c:showSerName val=\"0\"/><c:showPercent val=\"0\"/><c:showBubbleSize val=\"0\"/></c:dLbls>");
        sb.Append($"<c:cat>{StrRef(sp.CatRef, read)}</c:cat><c:val>{NumRef(se.ValRef, read)}</c:val>");
        if (line) sb.Append("<c:smooth val=\"0\"/>");
        sb.Append("</c:ser>");
        return sb.ToString();
    }

    private static string ChartXml(Spec sp, Func<string, List<string>> read)
    {
        var plot = new StringBuilder();
        bool pie = sp.Kind is Kind.Pie or Kind.Doughnut;

        if (pie)
        {
            var se = sp.Series[0];
            var dpts = new StringBuilder();
            for (int i = 0; i < sp.PointColors.Length; i++)
                dpts.Append($"<c:dPt><c:idx val=\"{i}\"/><c:bubble3D val=\"0\"/><c:spPr><a:solidFill><a:srgbClr val=\"{sp.PointColors[i]}\"/></a:solidFill><a:ln w=\"12700\"><a:solidFill><a:srgbClr val=\"FFFFFF\"/></a:solidFill></a:ln></c:spPr></c:dPt>");
            string ser = $"<c:ser><c:idx val=\"0\"/><c:order val=\"0\"/><c:tx><c:v>{E(se.Name)}</c:v></c:tx>{dpts}" +
                "<c:dLbls><c:numFmt formatCode=\"0%\" sourceLinked=\"0\"/><c:spPr><a:noFill/></c:spPr><c:txPr><a:bodyPr/><a:p><a:pPr><a:defRPr sz=\"900\" b=\"1\"/></a:pPr><a:endParaRPr lang=\"vi-VN\"/></a:p></c:txPr>" +
                "<c:showLegendKey val=\"0\"/><c:showVal val=\"0\"/><c:showCatName val=\"0\"/><c:showSerName val=\"0\"/><c:showPercent val=\"1\"/><c:showBubbleSize val=\"0\"/><c:showLeaderLines val=\"1\"/></c:dLbls>" +
                $"<c:cat>{StrRef(sp.CatRef, read)}</c:cat><c:val>{NumRef(se.ValRef, read)}</c:val></c:ser>";
            plot.Append(sp.Kind == Kind.Pie
                ? $"<c:pieChart><c:varyColors val=\"1\"/>{ser}<c:firstSliceAng val=\"0\"/></c:pieChart>"
                : $"<c:doughnutChart><c:varyColors val=\"1\"/>{ser}<c:firstSliceAng val=\"0\"/><c:holeSize val=\"55\"/></c:doughnutChart>");
        }
        else if (sp.Kind == Kind.Line)
        {
            int i = 0;
            plot.Append("<c:lineChart><c:grouping val=\"standard\"/><c:varyColors val=\"0\"/>");
            foreach (var se in sp.Series) plot.Append(SerXml(sp, se, i++, true, read));
            plot.Append("<c:marker val=\"1\"/><c:axId val=\"111\"/><c:axId val=\"112\"/></c:lineChart>");
        }
        else
        {
            string dir = sp.Kind == Kind.Bar ? "bar" : "col";
            string grouping = sp.Kind == Kind.ColStacked ? "stacked" : "clustered";
            int i = 0;
            plot.Append($"<c:barChart><c:barDir val=\"{dir}\"/><c:grouping val=\"{grouping}\"/><c:varyColors val=\"0\"/>");
            foreach (var se in sp.Series.Where(x => !x.AsLine)) plot.Append(SerXml(sp, se, i++, false, read));
            plot.Append($"<c:gapWidth val=\"60\"/>{(sp.Kind == Kind.ColStacked ? "<c:overlap val=\"100\"/>" : "")}<c:axId val=\"111\"/><c:axId val=\"112\"/></c:barChart>");
            var lines = sp.Series.Where(x => x.AsLine).ToList();
            if (lines.Count > 0)
            {
                plot.Append("<c:lineChart><c:grouping val=\"standard\"/><c:varyColors val=\"0\"/>");
                foreach (var se in lines) plot.Append(SerXml(sp, se, i++, true, read));
                plot.Append("<c:marker val=\"1\"/><c:axId val=\"111\"/><c:axId val=\"112\"/></c:lineChart>");
            }
        }

        string axes = "";
        if (!pie)
        {
            bool horiz = sp.Kind == Kind.Bar;
            axes = $@"<c:catAx><c:axId val=""111""/><c:scaling><c:orientation val=""{(horiz ? "maxMin" : "minMax")}""/></c:scaling><c:delete val=""0""/><c:axPos val=""{(horiz ? "l" : "b")}""/><c:numFmt formatCode=""General"" sourceLinked=""0""/><c:majorTickMark val=""none""/><c:minorTickMark val=""none""/><c:tickLblPos val=""nextTo""/><c:txPr><a:bodyPr rot=""{(horiz ? 0 : -2700000)}""/><a:p><a:pPr><a:defRPr sz=""800""/></a:pPr><a:endParaRPr lang=""vi-VN""/></a:p></c:txPr><c:crossAx val=""112""/><c:crosses val=""autoZero""/><c:auto val=""1""/><c:lblAlgn val=""ctr""/><c:lblOffset val=""100""/><c:noMultiLvlLbl val=""0""/></c:catAx>
<c:valAx><c:axId val=""112""/><c:scaling><c:orientation val=""minMax""/></c:scaling><c:delete val=""0""/><c:axPos val=""{(horiz ? "t" : "l")}""/><c:majorGridlines><c:spPr><a:ln w=""6350""><a:solidFill><a:srgbClr val=""E2E8F0""/></a:solidFill></a:ln></c:spPr></c:majorGridlines><c:numFmt formatCode=""#,##0"" sourceLinked=""0""/><c:majorTickMark val=""none""/><c:minorTickMark val=""none""/><c:tickLblPos val=""nextTo""/><c:txPr><a:bodyPr/><a:p><a:pPr><a:defRPr sz=""800""/></a:pPr><a:endParaRPr lang=""vi-VN""/></a:p></c:txPr><c:crossAx val=""111""/><c:crosses val=""{(horiz ? "max" : "autoZero")}""/><c:crossBetween val=""between""/></c:valAx>";
        }

        return $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<c:chartSpace xmlns:c=""http://schemas.openxmlformats.org/drawingml/2006/chart"" xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
<c:roundedCorners val=""0""/>
<c:chart><c:title><c:tx><c:rich><a:bodyPr/><a:p><a:pPr><a:defRPr sz=""1200"" b=""1""/></a:pPr><a:r><a:rPr lang=""vi-VN"" sz=""1200"" b=""1""/><a:t>{E(sp.Title)}</a:t></a:r></a:p></c:rich></c:tx><c:overlay val=""0""/></c:title>
<c:autoTitleDeleted val=""0""/><c:plotArea><c:layout/>{plot}{axes}</c:plotArea>
<c:legend><c:legendPos val=""b""/><c:overlay val=""0""/></c:legend><c:plotVisOnly val=""1""/><c:dispBlanksAs val=""gap""/></c:chart></c:chartSpace>";
    }
}

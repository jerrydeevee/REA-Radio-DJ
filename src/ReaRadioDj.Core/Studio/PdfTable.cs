using System.Globalization;
using System.Text;

namespace ReaRadioDj.Core.Studio;

/// <summary>One column of a <see cref="PdfTable"/>: heading and width in points.</summary>
public sealed record PdfColumn(string Heading, double Width, bool Right = false);

/// <summary>
/// A plain table as a PDF file: A4 landscape, Helvetica, a title and some lines above the table, the headings repeated
/// on every page and page numbers below. Written directly (PDF 1.4, standard fonts, WinAnsi text), so no extra library
/// is needed. Text that does not fit a column is cut off with an ellipsis.
/// </summary>
public static class PdfTable
{
    private const double PageW = 842, PageH = 595, Margin = 36, Row = 13, Size = 8, HeadSize = 8.5;

    public static byte[] Build(string title, IReadOnlyList<string> intro, IReadOnlyList<PdfColumn> columns,
        IEnumerable<IReadOnlyList<string>> rows, string footer)
    {
        var pages = new List<StringBuilder>();
        StringBuilder page = null!;
        double y = 0;
        double scale = Math.Min(1, (PageW - 2 * Margin) / columns.Sum(c => c.Width));

        void NewPage()
        {
            page = new StringBuilder();
            pages.Add(page);
            y = PageH - Margin;
            if (pages.Count == 1)
            {
                Text(page, Margin, y - 14, 16, true, title);
                y -= 24;
                foreach (var line in intro) { Text(page, Margin, y - 10, 9, false, line); y -= 13; }
                y -= 6;
            }
            // heading row on a grey band
            page.Append(Invariant($"0.88 g {Margin:0.##} {y - Row - 2:0.##} {PageW - 2 * Margin:0.##} {Row + 2:0.##} re f 0 g\n"));
            double x = Margin;
            foreach (var c in columns)
            {
                double w = c.Width * scale;
                Cell(page, x, y - Row + 1, w, HeadSize, true, c.Heading, c.Right);
                x += w;
            }
            y -= Row + 4;
        }

        NewPage();
        foreach (var r in rows)
        {
            if (y - Row < Margin + 14) NewPage();
            double x = Margin;
            for (int i = 0; i < columns.Count; i++)
            {
                double w = columns[i].Width * scale;
                Cell(page, x, y - Row + 3, w, Size, false, i < r.Count ? r[i] : "", columns[i].Right);
                x += w;
            }
            y -= Row;
        }

        for (int i = 0; i < pages.Count; i++)
        {
            Text(pages[i], Margin, Margin - 14, 7.5, false, footer);
            var no = $"pagina {i + 1} van {pages.Count}";
            Text(pages[i], PageW - Margin - Width(no, 7.5), Margin - 14, 7.5, false, no);
        }
        return Write(pages);
    }

    private static void Cell(StringBuilder sb, double x, double y, double width, double size, bool bold, string text, bool right)
    {
        double room = width - 6;
        text = Fit(text ?? "", size, room);
        double tx = right ? x + width - 3 - Width(text, size) : x + 3;
        Text(sb, tx, y, size, bold, text);
    }

    /// <summary>Cuts the text so it fits, ending in an ellipsis.</summary>
    public static string Fit(string text, double size, double room)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (Width(text, size) <= room) return text;
        while (text.Length > 0 && Width(text + "…", size) > room) text = text[..^1];
        return text.TrimEnd() + "…";
    }

    /// <summary>Approximate Helvetica width: narrow, normal and wide letters.</summary>
    public static double Width(string text, double size)
    {
        double units = 0;
        foreach (char c in text)
            units += c switch
            {
                'i' or 'l' or 'j' or '\'' or '|' or '.' or ',' or ':' or ';' or '!' => 0.24,
                'f' or 't' or 'r' or 'I' or ' ' or '(' or ')' or '-' => 0.32,
                'm' or 'w' or 'M' or 'W' or '@' => 0.85,
                >= 'A' and <= 'Z' => 0.68,
                >= '0' and <= '9' => 0.56,
                _ => 0.52,
            };
        return units * size;
    }

    private static void Text(StringBuilder sb, double x, double y, double size, bool bold, string text) =>
        sb.Append(Invariant($"BT /{(bold ? "F2" : "F1")} {size:0.##} Tf {x:0.##} {y:0.##} Td (")).Append(Escape(text)).Append(") Tj ET\n");

    private static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    // WinAnsi: Latin-1 as is, the usual typographic marks to their cp1252 places, the rest becomes '?'.
    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            int code = c switch
            {
                '€' => 0x80, '‚' => 0x82, '„' => 0x84, '…' => 0x85, '‘' => 0x91, '’' => 0x92, '“' => 0x93, '”' => 0x94,
                '•' => 0x95, '–' => 0x96, '—' => 0x97, '™' => 0x99, 'Š' => 0x8A, 'š' => 0x9A, 'Œ' => 0x8C, 'œ' => 0x9C,
                'Ž' => 0x8E, 'ž' => 0x9E, 'Ÿ' => 0x9F,
                _ when c < 0x20 => ' ',
                _ when c < 0x80 || (c >= 0xA0 && c <= 0xFF) => c,
                _ => '?',
            };
            if (code is '(' or ')' or '\\') sb.Append('\\');
            sb.Append((char)code);
        }
        return sb.ToString();
    }

    private static byte[] Write(List<StringBuilder> pages)
    {
        var latin = Encoding.Latin1;
        using var ms = new MemoryStream();
        var offsets = new List<long>();
        void Raw(string s) { var b = latin.GetBytes(s); ms.Write(b, 0, b.Length); }
        void Obj(string body) { offsets.Add(ms.Position); Raw($"{offsets.Count} 0 obj\n{body}\nendobj\n"); }

        Raw("%PDF-1.4\n%âãÏÓ\n");
        // 1 catalog, 2 pages, 3 and 4 fonts, then per page: content and page
        int first = 5;
        var kids = string.Join(' ', Enumerable.Range(0, pages.Count).Select(i => $"{first + 2 * i + 1} 0 R"));
        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        Obj($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        for (int i = 0; i < pages.Count; i++)
        {
            var content = pages[i].ToString();
            Obj($"<< /Length {latin.GetByteCount(content)} >>\nstream\n{content}endstream");
            Obj(Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageW} {PageH}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {first + 2 * i} 0 R >>"));
        }
        long xref = ms.Position;
        var sb = new StringBuilder($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append(o.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        sb.Append($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Raw(sb.ToString());
        return ms.ToArray();
    }
}

using System.Text;
using System.Text.RegularExpressions;
using ReaRadioDj.Core.Studio;
using Xunit;

namespace ReaRadioDj.Tests;

public class PdfTableTests
{
    [Fact]
    public void A_long_report_becomes_a_valid_pdf_with_several_pages()
    {
        var start = new DateTime(2026, 11, 1, 7, 0, 0);
        var rows = Enumerable.Range(0, 120).Select(i => new MusicReportRow(start.AddMinutes(4 * i), 200 + i, i % 2 == 0 ? "MØ" : "Beyoncé",
            $"Nummer (deel {i}) \\ met een hele lange titel die niet in de kolom past", "Componist", "Album", "Label", i % 3 == 0 ? "" : "NLA123456789",
            2020, "log", "Pop")).ToList();
        var pdf = MusicReport.Pdf(rows, "Radio Aa en Hunze", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 7), summary: false);
        var text = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        int pages = Regex.Matches(text, @"/Type /Page\b").Count;
        Assert.True(pages >= 3, $"120 rows need several pages, got {pages}");
        Assert.Contains($"/Count {pages}", text);
        Assert.Contains("(MØ) Tj", text);                         // Ø as WinAnsi
        Assert.Contains("Beyoncé", text);
        Assert.Contains("\\(deel 0\\)", text);                           // brackets escaped
        Assert.Contains("\u0085) Tj", text);                             // cut off with an ellipsis
        Assert.Contains($"pagina {pages} van {pages}", text);

        // every object starts where the cross-reference table says
        int xref = int.Parse(Regex.Match(text, @"startxref\n(\d+)").Groups[1].Value);
        var offsets = Regex.Matches(text[xref..], @"(\d{10}) 00000 n").Select(m => int.Parse(m.Groups[1].Value)).ToList();
        for (int i = 0; i < offsets.Count; i++) Assert.StartsWith($"{i + 1} 0 obj", text[offsets[i]..]);

        var summary = Encoding.Latin1.GetString(MusicReport.Pdf(rows, "Radio", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 7), summary: true));
        Assert.Contains(@"\(per nummer\)", summary);
    }

    [Fact]
    public void Text_is_cut_to_fit()
    {
        Assert.Equal("kort", PdfTable.Fit("kort", 8, 100));
        var cut = PdfTable.Fit(new string('x', 200), 8, 50);
        Assert.EndsWith("…", cut);
        Assert.True(PdfTable.Width(cut, 8) <= 50);
    }
}

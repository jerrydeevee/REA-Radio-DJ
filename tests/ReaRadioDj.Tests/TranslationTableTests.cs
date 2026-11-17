using System.Text;
using System.Text.RegularExpressions;
using ReaRadioDj.Contracts.Localization;

namespace ReaRadioDj.Tests;

/// <summary>
/// Guards the translation table: every fixed text the code asks for (Tr.T, Tr.F, Tr.In and the web pages' tr()) must be
/// in strings.tsv with a translation in every language. When this fails, run  python scripts/strings.py  and fill in
/// the new rows.
/// </summary>
public sealed partial class TranslationTableTests
{
    [GeneratedRegex("""(?:\bTr\.[TF]\(|\bTr\.In\([^,()]*,|(?<![\w.])L\()\s*"((?:[^"\\]|\\.)*)"(?!\s*\+)""")]
    private static partial Regex CsCall();

    [GeneratedRegex(@"\btr\(\s*""((?:[^""\\]|\\.)*)""|\btr\(\s*'((?:[^'\\]|\\.)*)'|data-tr=""([^""]*)""")]
    private static partial Regex WebCall();

    private static DirectoryInfo Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ReaRadioDj.sln"))) dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("ReaRadioDj.sln not found above " + AppContext.BaseDirectory);
    }

    private static string Unescape(string literal)
    {
        var sb = new StringBuilder(literal.Length);
        for (int i = 0; i < literal.Length; i++)
        {
            if (literal[i] != '\\' || i + 1 == literal.Length) { sb.Append(literal[i]); continue; }
            char c = literal[++i];
            sb.Append(c switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '0' => '\0', _ => c });
        }
        return sb.ToString();
    }

    private static IEnumerable<(string Text, string File)> Asked()
    {
        var src = Path.Combine(Root().FullName, "src");
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            var code = File.ReadAllText(file);
            if (!code.Contains("Tr.") && !code.Contains("L(\"")) continue;
            foreach (Match m in CsCall().Matches(code))
                yield return (Unescape(m.Groups[1].Value), Path.GetRelativePath(src, file));
        }
        var web = Path.Combine(src, "ReaRadioDj.Server", "wwwroot");
        foreach (var file in Directory.EnumerateFiles(web, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".js") || f.EndsWith(".html")))
        {
            if (file.EndsWith("tr.js")) continue;
            foreach (Match m in WebCall().Matches(File.ReadAllText(file)))
            {
                var g = m.Groups[1].Success ? m.Groups[1] : m.Groups[2].Success ? m.Groups[2] : m.Groups[3];
                yield return (Unescape(g.Value), Path.GetRelativePath(src, file));
            }
        }
    }

    [Fact]
    public void EveryAskedTextIsTranslatedInEveryLanguage()
    {
        var asked = Asked().Where(a => a.Text.Any(char.IsLetter)).DistinctBy(a => a.Text).ToList();
        Assert.True(asked.Count > 500, $"only {asked.Count} texts found: is the source scan still right?");
        var gaps = new List<string>();
        foreach (var (code, _) in Tr.Languages.Where(l => l.Code != "nl"))
        {
            var table = Tr.Table(code);
            gaps.AddRange(asked.Where(a => !table.ContainsKey(a.Text)).Select(a => $"{code}: \"{a.Text}\" ({a.File})"));
        }
        Assert.True(gaps.Count == 0, "Texts without translation (run python scripts/strings.py):\n" + string.Join("\n", gaps.Take(40)));
    }

    [Fact]
    public void EveryRowHasAllLanguages()
    {
        var keys = Tr.Table("en").Keys.Concat(Tr.Table("de").Keys).Concat(Tr.Table("es").Keys).ToHashSet();
        var gaps = Tr.Languages.Where(l => l.Code != "nl")
            .SelectMany(l => keys.Where(k => !Tr.Table(l.Code).ContainsKey(k)).Select(k => $"{l.Code}: \"{k}\""))
            .ToList();
        Assert.True(gaps.Count == 0, "Rows with a language missing:\n" + string.Join("\n", gaps.Take(40)));
    }
}

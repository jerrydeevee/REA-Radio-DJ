using System.Net.Http;
using System.Reflection;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.Services;

/// <summary>
/// Asks GitHub which release of REA-Radio-DJ is the newest. Only reads: nothing is downloaded or installed.
/// </summary>
public static class UpdateChecker
{
    public const string Repo = Views.AboutWindow.SourceUrl;
    private const string LatestApi = "https://api.github.com/repos/jerrydeevee/REA-Radio-DJ/releases/latest";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>The full version of this studio, e.g. "1.1.0-rc.1" (without the build part).</summary>
    public static string CurrentVersion
    {
        get
        {
            var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return ReleaseVersion.Parse(info)?.ToString() ?? Views.AboutWindow.Version;
        }
    }

    /// <summary>The newest release, or an error text in Dutch.</summary>
    public static async Task<(ReleaseInfo? Release, string? Error)> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, LatestApi);
            req.Headers.TryAddWithoutValidation("User-Agent", $"REA-Radio-DJ/{CurrentVersion} ( {Repo} )");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var resp = await Http.SendAsync(req, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return (null, "Er is nog geen uitgave gepubliceerd.");
            if (!resp.IsSuccessStatusCode) return (null, $"GitHub antwoordde met {(int)resp.StatusCode}.");
            var json = await resp.Content.ReadAsStringAsync(ct);
            var release = ReleaseInfo.Parse(json, Repo);
            return release is null ? (null, "Het antwoord van GitHub was niet te lezen.") : (release, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (null, "Geen verbinding met GitHub.");
        }
    }
}

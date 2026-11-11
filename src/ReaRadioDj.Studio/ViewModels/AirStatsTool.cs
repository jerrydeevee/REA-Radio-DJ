using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>One line of a list in the statistics: place, name, detail, value and a bar (0..100) against the first line.</summary>
public sealed record AirStatsRow(string Place, string Name, string Detail, string Value, double Bar)
{
    public bool HasBar => Bar > 0;
}

/// <summary>One hour of the day in the songs-per-hour picture.</summary>
public sealed record AirStatsHour(string Label, double Height, string Tip);

/// <summary>
/// Broadcast statistics of a day, week or month: totals of music against speech, the most played tracks and artists,
/// rotation per category, songs per hour and the songs that have not been played for a long time. From the as-run log.
/// </summary>
public sealed partial class AirStatsTool : StudioTool
{
    public AirStatsTool() : base("airstats", "Uitzendstatistiek") { }

    public string[] PeriodNames { get; } = ["Dag", "Week", "Maand"];
    public int[] IdleChoices { get; } = [30, 60, 90, 180, 365];
    public string[] IdleNames { get; } = ["30 dagen", "60 dagen", "90 dagen", "half jaar", "jaar"];

    [ObservableProperty] private int _period = 2;
    [ObservableProperty] private DateTime? _day = DateTime.Today;
    [ObservableProperty] private int _idleIndex = 1;
    [ObservableProperty] private string _periodText = "";
    [ObservableProperty] private string _totals = "";
    [ObservableProperty] private string _shareText = "";
    [ObservableProperty] private double _musicShare;
    [ObservableProperty] private double _speechShare;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _idleTitle = "Lang niet gedraaid";

    public ObservableCollection<AirStatsRow> TopTracks { get; } = [];
    public ObservableCollection<AirStatsRow> TopArtists { get; } = [];
    public ObservableCollection<AirStatsRow> Rotation { get; } = [];
    public ObservableCollection<AirStatsRow> Idle { get; } = [];
    public ObservableCollection<AirStatsHour> Hours { get; } = [];

    partial void OnPeriodChanged(int value) => _ = LoadAsync();
    partial void OnDayChanged(DateTime? value) => _ = LoadAsync();
    partial void OnIdleIndexChanged(int value) => _ = LoadAsync();
    protected override void OnFirstShow() => _ = LoadAsync();

    /// <summary>First and last day of the period around a day: that day, its week (Monday to Sunday) or its month.</summary>
    public static (DateOnly From, DateOnly To) Range(int period, DateOnly day)
    {
        switch (period)
        {
            case 0: return (day, day);
            case 1:
                var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
                return (monday, monday.AddDays(6));
            default:
                var first = new DateOnly(day.Year, day.Month, 1);
                return (first, first.AddMonths(1).AddDays(-1));
        }
    }

    private (DateOnly From, DateOnly To) Current => Range(Period, DateOnly.FromDateTime(Day ?? DateTime.Today));
    private int IdleDays => IdleChoices[Math.Clamp(IdleIndex, 0, IdleChoices.Length - 1)];

    private string Query
    {
        get
        {
            var (f, t) = Current;
            return $"from={f:yyyy-MM-dd}&to={t:yyyy-MM-dd}&top=20&idle={IdleDays}";
        }
    }

    public string CsvUrl => "/api/reports/airstats.csv?" + Query;
    public string CsvFileName { get { var (f, t) = Current; return $"uitzendstatistiek-{f:yyyy-MM-dd}-{t:yyyy-MM-dd}.csv"; } }

    [RelayCommand] private void Previous() => Shift(-1);
    [RelayCommand] private void Next() => Shift(1);

    private void Shift(int step)
    {
        var d = Day ?? DateTime.Today;
        Day = Period switch { 0 => d.AddDays(step), 1 => d.AddDays(7 * step), _ => d.AddMonths(step) };
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var (f, t) = Current;
        var nl = CultureInfo.GetCultureInfo("nl-NL");
        PeriodText = Period switch
        {
            0 => f.ToString("dddd d MMMM yyyy", nl),
            1 => $"week {ISOWeek.GetWeekOfYear(f.ToDateTime(TimeOnly.MinValue))} · {f.ToString("d MMM", nl)} t/m {t.ToString("d MMM yyyy", nl)}",
            _ => f.ToString("MMMM yyyy", nl),
        };
        IdleTitle = $"Niet gedraaid in {IdleNames[Math.Clamp(IdleIndex, 0, IdleNames.Length - 1)]}";

        var s = await Hub.Client.GetAsync<AirStatsDto>("/api/reports/airstats?" + Query);
        if (s is null) return;

        double total = s.MusicSeconds + s.SpeechSeconds + s.JingleSeconds + s.OtherSeconds;
        HasData = s.Items > 0;
        MusicShare = total > 0 ? s.MusicSeconds / total * 100 : 0;
        SpeechShare = total > 0 ? s.SpeechSeconds / total * 100 : 0;
        Totals = $"{s.Songs} nummers · {s.Items} regels in de as-run · {HourText(s.MusicSeconds)} muziek · {HourText(s.SpeechSeconds)} spraak · {HourText(s.JingleSeconds)} jingles";
        ShareText = total > 0
            ? $"muziek {Pct(s.MusicSeconds, total)} · spraak {Pct(s.SpeechSeconds, total)} · jingles {Pct(s.JingleSeconds, total)}"
              + (s.OtherSeconds > 0 ? $" · overig {Pct(s.OtherSeconds, total)}" : "")
            : "nog niets uitgezonden in deze periode";

        Fill(TopTracks, s.TopTracks.Select(x => (x.Name, x.Detail, (double)x.Plays, Times(x.Plays))));
        Fill(TopArtists, s.TopArtists.Select(x => (x.Name, HourText(x.Seconds), (double)x.Plays, Times(x.Plays))));
        Fill(Rotation, s.Rotation.Select(x => (x.Category,
            $"{x.Tracks} verschillende nummers · elk gem. {(x.Tracks > 0 ? (double)x.Plays / x.Tracks : 0).ToString("0.0", nl)}×",
            (double)x.Plays, Times(x.Plays))));
        Idle.Clear();
        int i = 0;
        foreach (var x in s.Idle)
            Idle.Add(new AirStatsRow((++i).ToString(CultureInfo.InvariantCulture), x.Title,
                string.IsNullOrWhiteSpace(x.Category) ? x.Artist : $"{x.Artist} · {x.Category}",
                x.LastPlayed is DateTime lp ? lp.ToString("d MMM yyyy", nl) : "nooit", 0));

        Hours.Clear();
        int max = Math.Max(1, s.SongsPerHour.DefaultIfEmpty(0).Max());
        for (int h = 0; h < s.SongsPerHour.Count; h++)
            Hours.Add(new AirStatsHour(h % 3 == 0 ? h.ToString("00", CultureInfo.InvariantCulture) : "",
                Math.Max(2, s.SongsPerHour[h] * 60.0 / max), $"{h:00}:00 – {h:00}:59 · {Times(s.SongsPerHour[h])}"));
    }

    private static void Fill(ObservableCollection<AirStatsRow> target, IEnumerable<(string Name, string Detail, double Amount, string Value)> rows)
    {
        target.Clear();
        var list = rows.ToList();
        double max = list.Count > 0 ? Math.Max(1, list.Max(r => r.Amount)) : 1;
        for (int i = 0; i < list.Count; i++)
            target.Add(new AirStatsRow((i + 1).ToString(CultureInfo.InvariantCulture), list[i].Name, list[i].Detail, list[i].Value,
                list[i].Amount / max * 100));
    }

    private static string Times(int n) => n == 1 ? "1 keer" : $"{n} keer";
    private static string Pct(double part, double total) => $"{Math.Round(part / total * 100)} %";

    public static string HourText(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalHours}:{t.Minutes:00} u";
    }
}

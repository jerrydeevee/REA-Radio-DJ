using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>One hour button in the day overview.</summary>
public sealed partial class DayHourVm(int hour, bool now, int events) : ObservableObject
{
    public int Hour { get; } = hour;
    public string Text { get; } = $"{hour:00}:00";
    public bool IsNow { get; } = now;
    /// <summary>Small badge with the number of fixed times in this hour.</summary>
    public string Badge { get; } = events > 0 ? $"·{events}" : "";
    [ObservableProperty] private bool _selected;
}

/// <summary>A run of consecutive hours with the same clock: a "programme" in the day overview.</summary>
public sealed class DayBlockVm(string name, IBrush brush, string span, IReadOnlyList<DayHourVm> hours, bool now)
{
    public string Name { get; } = name;
    public IBrush Brush { get; } = brush;
    public string Span { get; } = span;
    public IReadOnlyList<DayHourVm> Hours { get; } = hours;
    public bool IsNow { get; } = now;
}

/// <summary>One line in the agenda of the selected day or hour.</summary>
public sealed record DayLine(string Time, string Text, string Detail, IBrush Brush);

/// <summary>
/// Day overview: pick a date in the calendar and see the programme blocks (clocks from the week grid), the hours with
/// fixed times and, for a chosen hour, what the clock plays and which fixed times and external streams fire.
/// </summary>
public sealed partial class PlannerTool
{
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");
    private List<ClockDto> _dayClocks = [];
    private Dictionary<(int Day, int Hour), long> _dayGrid = [];

    public ObservableCollection<DayBlockVm> DayBlocks { get; } = [];
    public ObservableCollection<DayLine> DayAgenda { get; } = [];
    public ObservableCollection<DayLine> HourLines { get; } = [];
    [ObservableProperty] private DateTime? _dayDate = DateTime.Today;
    [ObservableProperty] private string _dayTitle = "";
    [ObservableProperty] private string _hourTitle = "";
    [ObservableProperty] private string _nowClock = "";
    private int _selectedHour = DateTime.Now.Hour;

    partial void OnDayDateChanged(DateTime? value) => BuildDay();

    [RelayCommand] private void Today() { DayDate = DateTime.Today; _selectedHour = DateTime.Now.Hour; BuildDay(); }
    [RelayCommand] private void PrevDay() => DayDate = (DayDate ?? DateTime.Today).AddDays(-1);
    [RelayCommand] private void NextDay() => DayDate = (DayDate ?? DateTime.Today).AddDays(1);

    [RelayCommand]
    private void PickHour(DayHourVm? h)
    {
        if (h is null) return;
        _selectedHour = h.Hour;
        foreach (var b in DayBlocks) foreach (var x in b.Hours) x.Selected = x.Hour == h.Hour;
        BuildHour();
    }

    /// <summary>Called after a reload with fresh clocks, grid, fixed times and stream watches.</summary>
    private void SetDayData(List<ClockDto> clocks, List<GridCellDto> grid)
    {
        _dayClocks = clocks;
        _dayGrid = grid.ToDictionary(g => (g.Day, g.Hour), g => g.ClockId);
        BuildDay();
    }

    private static int DayIndex(DateTime d) => ((int)d.DayOfWeek + 6) % 7;
    private static bool OnDay(int days, DateTime d) => (days & (1 << DayIndex(d))) != 0;

    private ClockDto? ClockAt(DateTime date, int hour) =>
        _dayGrid.TryGetValue((DayIndex(date), hour), out var id) ? _dayClocks.FirstOrDefault(c => c.Id == id) : null;

    private IEnumerable<(int Hour, TimedEventDto Event)> EventsOn(DateTime date) =>
        Events.Where(e => e.Enabled && OnDay(e.Days, date))
              .SelectMany(e => HourList(e.Hours).Select(h => (h, e)));

    private void BuildDay()
    {
        var date = (DayDate ?? DateTime.Today).Date;
        bool today = date == DateTime.Today;
        DayTitle = date.ToString("dddd d MMMM yyyy", Dutch);
        var now = DateTime.Now;
        NowClock = today ? $"Nu: {ClockAt(date, now.Hour)?.Name ?? "geen klok (vrije rotatie)"}" : "";

        var events = EventsOn(date).ToList();
        DayBlocks.Clear();
        int start = 0;
        while (start < 24)
        {
            var clock = ClockAt(date, start);
            int end = start;
            while (end + 1 < 24 && ClockAt(date, end + 1)?.Id == clock?.Id) end++;
            var hours = Enumerable.Range(start, end - start + 1)
                .Select(h => new DayHourVm(h, today && h == now.Hour, events.Count(e => e.Hour == h)) { Selected = h == _selectedHour })
                .ToList();
            bool isNow = today && now.Hour >= start && now.Hour <= end;
            DayBlocks.Add(new DayBlockVm(clock?.Name ?? "Geen klok — vrije rotatie", Brushes.FromHex(clock?.Color, "#475569"),
                start == 0 && end == 23 ? "hele dag" : $"{start:00}:00 – {(end + 1) % 24:00}:00", hours, isNow));
            start = end + 1;
        }

        // agenda of the day: every fixed time, in time order
        DayAgenda.Clear();
        foreach (var (hour, e) in events.OrderBy(x => x.Hour).ThenBy(x => x.Event.Minute).ThenBy(x => x.Event.Second))
            DayAgenda.Add(new DayLine($"{hour:00}:{e.Minute:00}:{e.Second:00}", e.Name, ActionText(e), Brushes.FromHex(TimingColor(e.Timing))));
        foreach (var w in Watches.Where(w => w.Enabled && OnDay(w.Days, date)))
            DayAgenda.Add(new DayLine(w.Hours.Trim() == "*" ? "hele dag" : $"uur {w.Hours}", w.Name,
                w.Mode == "overlay" ? "externe stream (overlay)" : "externe stream (neemt over)", Brushes.FromHex("#2DD4BF")));
        BuildHour();
    }

    private void BuildHour()
    {
        var date = (DayDate ?? DateTime.Today).Date;
        int h = _selectedHour;
        var clock = ClockAt(date, h);
        HourTitle = $"{h:00}:00 – {(h + 1) % 24:00}:00 · {clock?.Name ?? "geen klok"}";
        HourLines.Clear();
        foreach (var (_, e) in EventsOn(date).Where(x => x.Hour == h).OrderBy(x => x.Event.Minute).ThenBy(x => x.Event.Second))
            HourLines.Add(new DayLine($"{h:00}:{e.Minute:00}:{e.Second:00}", e.Name, ActionText(e), Brushes.FromHex(TimingColor(e.Timing))));
        if (clock is null)
        {
            HourLines.Add(new DayLine("", "Vrije rotatie", "geen klok in het weekgrid: de automatisering kiest uit alle muziek", Brushes.FromHex("#475569")));
            return;
        }
        int n = 1;
        foreach (var r in clock.Rules)
        {
            string what = r.Kind switch
            {
                "media" => $"vast item {r.MediaId}",
                "ai" or "aiBreak" => "AI-presentator",
                _ => Categories.FirstOrDefault(c => c.Id == r.CategoryId)?.Name ?? "categorie",
            };
            var color = Categories.FirstOrDefault(c => c.Id == r.CategoryId)?.Color;
            HourLines.Add(new DayLine($"{n++}.", what, string.IsNullOrWhiteSpace(r.Note) ? r.Mode : r.Note, Brushes.FromHex(color, clock.Color)));
        }
    }

    private string ActionText(TimedEventDto e)
    {
        string timing = e.Timing switch { "soft" => "zacht", "overlay" => "overlay", _ => "hard" };
        string what = e.Action switch
        {
            "playMedia" => "vast item",
            "playCategory" => Categories.FirstOrDefault(c => c.Id == e.CategoryId)?.Name ?? "categorie",
            "aiBreak" => "AI-presentator",
            "command" => $"commando: {e.Text}",
            "reminder" => $"herinnering: {e.Text}",
            "alert" => $"alarm: {e.Text}",
            _ => e.Action,
        };
        return $"{what} · {timing}";
    }

    private static string TimingColor(string timing) => timing switch { "soft" => "#3FA7FF", "overlay" => "#A78BFA", _ => "#E5484D" };

    /// <summary>Same notation as the engine: "*", "7", "7-9" or "6-9,12,17-19".</summary>
    private static IEnumerable<int> HourList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "*") return Enumerable.Range(0, 24);
        var set = new SortedSet<int>();
        foreach (var raw in text.Split(',', ';', ' '))
        {
            var part = raw.Trim();
            var dash = part.IndexOf('-');
            if (dash > 0 && int.TryParse(part[..dash], CultureInfo.InvariantCulture, out var a) && int.TryParse(part[(dash + 1)..], CultureInfo.InvariantCulture, out var b))
                for (int h = Math.Max(0, Math.Min(a, b)); h <= Math.Min(23, Math.Max(a, b)); h++) set.Add(h);
            else if (int.TryParse(part, CultureInfo.InvariantCulture, out var h) && h is >= 0 and < 24) set.Add(h);
        }
        return set;
    }
}

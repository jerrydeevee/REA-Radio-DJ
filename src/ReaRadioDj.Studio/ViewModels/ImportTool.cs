using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>Where one field of a log line is: a column number, or a start position and a length for fixed width.</summary>
public sealed partial class ImportField(string name) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] private decimal? _column = 0;
    [ObservableProperty] private decimal? _start = 0;
    [ObservableProperty] private decimal? _length = 0;
    [ObservableProperty] private bool _fixed;
    public bool Delimited => !Fixed;
    partial void OnFixedChanged(bool value) => OnPropertyChanged(nameof(Delimited));

    public void Set(ImportColumnDto c) { Column = c.Column; Start = c.Start; Length = c.Length; }
    public ImportColumnDto Get() => new((int)(Column ?? 0), (int)(Start ?? 0), (int)(Length ?? 0));
}

/// <summary>An hour that plays an imported log, for the list.</summary>
public sealed record ImportedHourRow(ImportedHourDto Hour)
{
    public string When => DateTime.TryParse(Hour.Date, out var d)
        ? $"{d:ddd d MMM} {Hour.Hour:00}:00" : $"{Hour.Date} {Hour.Hour:00}:00";
    public string Detail => $"{Hour.Items} items · {Hour.Source}";
}

/// <summary>
/// External music scheduling: read a log file from a music scheduler with an import profile, check it line by line
/// against the library and import it. An imported hour plays its lines instead of its clock. Also exports the
/// library as CSV for the scheduler.
/// </summary>
public sealed partial class ImportTool : StudioTool
{
    public ImportTool() : base("import", "Log-import")
    {
        Fields = [_time, _file, _mediaId, _artist, _title];
    }

    public static readonly string[] LayoutNames = ["Gescheiden door een teken", "Vaste kolombreedte"];
    public static readonly string[] SeparatorNames = ["; puntkomma", ", komma", "tab", "| streep"];
    private static readonly string[] SeparatorValues = [";", ",", "\\t", "|"];
    public IReadOnlyList<string> Layouts => LayoutNames;
    public IReadOnlyList<string> Separators => SeparatorNames;
    public IReadOnlyList<int> HoursOfDay { get; } = Enumerable.Range(0, 24).ToList();

    private readonly ImportField _time = new("Tijd"), _file = new("Bestand"), _mediaId = new("Nummer"), _artist = new("Artiest"), _title = new("Titel");
    public IReadOnlyList<ImportField> Fields { get; }

    public ObservableCollection<ImportProfileDto> Profiles { get; } = [];
    public ObservableCollection<ImportLineDto> Lines { get; } = [];
    public ObservableCollection<ImportedHourRow> Hours { get; } = [];

    // ---------------------------------------------------------------- importing

    [ObservableProperty] private ImportProfileDto? _useProfile;
    [ObservableProperty] private DateTime? _date = DateTime.Today.AddDays(1);
    [ObservableProperty] private int _startHour;
    [ObservableProperty] private string? _fileName;
    [ObservableProperty] private string _summary = "Kies een profiel en een logbestand.";
    [ObservableProperty] private string? _problem;
    private string? _text;

    public bool HasFile => _text is not null;

    /// <summary>Called by the view after the file was chosen and read.</summary>
    public void SetFile(string name, string text)
    {
        FileName = name;
        _text = text;
        OnPropertyChanged(nameof(HasFile));
        // A date in the file name (2026-11-10, 20261110 or 10-11-2026) is the date of the log.
        var m = System.Text.RegularExpressions.Regex.Match(name, @"(20\d\d)-?(\d\d)-?(\d\d)|(\d\d)-(\d\d)-(20\d\d)");
        if (m.Success)
        {
            int y = int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[6].Value);
            int mo = int.Parse(m.Groups[1].Success ? m.Groups[2].Value : m.Groups[5].Value);
            int d = int.Parse(m.Groups[1].Success ? m.Groups[3].Value : m.Groups[4].Value);
            if (mo is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(y, mo)) Date = new DateTime(y, mo, d);
        }
        _ = Run(false);
    }

    [RelayCommand] private Task Check() => Run(false);
    [RelayCommand] private Task Apply() => Run(true);

    private async Task Run(bool apply)
    {
        Problem = null;
        if (UseProfile is null) { Problem = "Kies eerst een importprofiel (tab Profielen)."; return; }
        if (_text is null) { Problem = "Kies eerst een logbestand."; return; }
        Summary = apply ? "bezig met importeren…" : "bezig met controleren…";
        var request = new ImportRequestDto(UseProfile.Id, _text, $"{Date ?? DateTime.Today:yyyy-MM-dd}", StartHour, FileName);
        var r = await Hub.Client.SendForAsync<ImportResultDto>(HttpMethod.Post, apply ? "/api/import/apply" : "/api/import/preview", request);
        if (r is null) { Summary = ""; Problem = "Lezen mislukt: engine niet bereikbaar of profiel ongeldig?"; return; }
        Lines.Clear();
        foreach (var l in r.Items) Lines.Add(l);
        string hours = r.Hours.Count == 0 ? "geen uren" : r.Hours.Count == 1 ? r.Hours[0] : $"{r.Hours[0]} t/m {r.Hours[^1]} ({r.Hours.Count} uur)";
        Summary = (r.Applied ? "Geïmporteerd: " : "Gecontroleerd: ") + $"{r.Matched} van {r.Lines} regels gevonden in de bibliotheek · {hours}"
            + (r.Applied ? "" : "\nNog niets opgeslagen: kies Importeren.");
        Problem = r.Error;
        if (r.Applied)
        {
            Hub.Status = $"log geïmporteerd: {r.Hours.Count} uur";
            await LoadHoursAsync();
        }
    }

    // ---------------------------------------------------------------- profiles

    [ObservableProperty] private ImportProfileDto? _selected;
    [ObservableProperty] private long _id;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private int _layout;
    [ObservableProperty] private int _separator;
    [ObservableProperty] private bool _hasHeader = true;
    [ObservableProperty] private string _skipPrefix = "";
    [ObservableProperty] private string _timeFormat = "HH:mm:ss";
    [ObservableProperty] private string _pathFrom = "";
    [ObservableProperty] private string _pathTo = "";
    [ObservableProperty] private string? _profileProblem;

    public bool IsDelimited => Layout == 0;

    partial void OnLayoutChanged(int value)
    {
        OnPropertyChanged(nameof(IsDelimited));
        foreach (var f in Fields) f.Fixed = value == 1;
    }

    partial void OnSelectedChanged(ImportProfileDto? value)
    {
        if (value is null) return;
        Id = value.Id;
        Name = value.Name;
        Layout = value.Layout == "fixed" ? 1 : 0;
        Separator = Math.Max(0, Array.IndexOf(SeparatorValues, value.Separator));
        HasHeader = value.HasHeader;
        SkipPrefix = value.SkipPrefix;
        TimeFormat = value.TimeFormat;
        PathFrom = value.PathFrom;
        PathTo = value.PathTo;
        _time.Set(value.Time); _file.Set(value.File); _mediaId.Set(value.MediaId); _artist.Set(value.Artist); _title.Set(value.Title);
        ProfileProblem = null;
    }

    protected override void OnFirstShow() => _ = LoadAllAsync();

    private async Task LoadAllAsync()
    {
        await LoadProfilesAsync();
        await LoadHoursAsync();
    }

    private async Task LoadProfilesAsync(long? select = null)
    {
        if (await Hub.Client.GetAsync<List<ImportProfileDto>>("/api/import/profiles") is not { } list) return;
        long? keep = select ?? Selected?.Id;
        long? use = UseProfile?.Id;
        Profiles.Clear();
        foreach (var p in list) Profiles.Add(p);
        Selected = Profiles.FirstOrDefault(p => p.Id == keep) ?? Profiles.FirstOrDefault();
        UseProfile = Profiles.FirstOrDefault(p => p.Id == (select ?? use)) ?? Profiles.FirstOrDefault();
        if (Selected is null) NewProfile();
    }

    private ImportProfileDto CurrentProfile() => new(Id, Name, Layout == 1 ? "fixed" : "delimited", SeparatorValues[Math.Clamp(Separator, 0, 3)],
        HasHeader, SkipPrefix, _time.Get(), TimeFormat, _file.Get(), _mediaId.Get(), _artist.Get(), _title.Get(), PathFrom, PathTo);

    [RelayCommand]
    private void NewProfile()
    {
        Selected = null;
        Id = 0;
        Name = "Nieuw profiel";
        Layout = 0;
        OnLayoutChanged(0);
        Separator = 0;
        HasHeader = true;
        SkipPrefix = "";
        TimeFormat = "HH:mm:ss";
        PathFrom = "";
        PathTo = "";
        _time.Set(new(1)); _file.Set(new(2)); _mediaId.Set(new()); _artist.Set(new(3)); _title.Set(new(4));
        ProfileProblem = null;
    }

    [RelayCommand]
    private async Task SaveProfile()
    {
        var saved = await Hub.Client.SendForAsync<ImportProfileDto>(HttpMethod.Post, "/api/import/profiles", CurrentProfile());
        if (saved is null) { ProfileProblem = "Opslaan mislukt: geef aan waar het bestand, het nummer, of artiest en titel staan."; return; }
        ProfileProblem = null;
        await LoadProfilesAsync(saved.Id);
        Hub.Status = $"importprofiel “{saved.Name}” opgeslagen";
    }

    [RelayCommand]
    private async Task DeleteProfile()
    {
        if (Id <= 0) { NewProfile(); return; }
        await Hub.Client.DeleteAsync($"/api/import/profiles/{Id}");
        Selected = null;
        await LoadProfilesAsync();
    }

    // ---------------------------------------------------------------- imported hours

    [ObservableProperty] private ImportedHourRow? _selectedHour;

    private async Task LoadHoursAsync()
    {
        if (await Hub.Client.GetAsync<List<ImportedHourDto>>("/api/import/hours") is not { } list) return;
        Hours.Clear();
        foreach (var h in list) Hours.Add(new ImportedHourRow(h));
    }

    [RelayCommand] private Task RefreshHours() => LoadHoursAsync();

    [RelayCommand]
    private async Task DeleteHour()
    {
        if (SelectedHour is not { } h) return;
        await Hub.Client.DeleteAsync($"/api/import/hours?date={h.Hour.Date}&hour={h.Hour.Hour}");
        await LoadHoursAsync();
    }

    [RelayCommand]
    private async Task DeleteDay()
    {
        if (SelectedHour is not { } h) return;
        await Hub.Client.DeleteAsync($"/api/import/hours?date={h.Hour.Date}");
        await LoadHoursAsync();
    }

    public const string ExportUrl = "/api/library/export.csv";
    public static string ExportFileName => $"bibliotheek-{DateTime.Today:yyyy-MM-dd}.csv";
}

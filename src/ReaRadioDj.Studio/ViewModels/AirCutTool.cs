using ReaRadioDj.Contracts.Localization;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>
/// Cuts a stretch out of the program recording (one MP3 per hour) into a new item in the library: the interview of
/// ten minutes ago, a call-in, a live session. Pick a day, a begin and an end time, a title; the engine does the rest.
/// </summary>
public sealed partial class AirCutTool : StudioTool
{
    public AirCutTool() : base("aircut", Tr.T("Uit de uitzending knippen")) { }

    private static readonly string[] TimeFormats = [@"h\:mm\:ss", @"h\:mm", @"hh\:mm\:ss", @"hh\:mm"];

    public ObservableCollection<string> Hours { get; } = [];
    [ObservableProperty] private DateTime? _day = DateTime.Today;
    [ObservableProperty] private string _fromText = "";
    [ObservableProperty] private string _toText = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _hoursText = "";
    [ObservableProperty] private bool _busy;

    protected override void OnFirstShow()
    {
        SetLast(10);
        _ = Refresh();
    }

    partial void OnDayChanged(DateTime? value) => _ = Refresh();

    [RelayCommand]
    private async Task Refresh()
    {
        var day = (Day ?? DateTime.Today).Date;
        var files = await Hub.Client.GetAsync<List<RecordedFileDto>>($"/api/recordings/program?date={day:yyyy-MM-dd}");
        Hours.Clear();
        if (files is null) { HoursText = Tr.T("De engine kent het knippen uit de uitzending nog niet: werk hem bij."); return; }
        foreach (var f in files) Hours.Add($"{f.Start:HH:mm:ss} – {f.End:HH:mm:ss}   {f.Name}");
        HoursText = files.Count == 0
            ? Tr.T("Van deze dag is geen uitzendopname. Zet Instellingen › Opnemen › Uitzending opnemen aan.")
            : Tr.F("Opgenomen op {0:dddd d MMMM}:", day);
    }

    /// <summary>The last minutes until now, on today.</summary>
    [RelayCommand]
    private void Last(string? minutes) => SetLast(int.TryParse(minutes, out var m) ? m : 10);

    private void SetLast(int minutes)
    {
        var now = DateTime.Now;
        Day = now.Date;
        FromText = now.AddMinutes(-minutes).ToString("HH:mm:ss");
        ToText = now.ToString("HH:mm:ss");
    }

    private DateTime? At(string text)
    {
        if (!TimeSpan.TryParseExact(text.Trim(), TimeFormats, CultureInfo.InvariantCulture, out var t) || t >= TimeSpan.FromDays(1)) return null;
        return (Day ?? DateTime.Today).Date + t;
    }

    [RelayCommand]
    private async Task Cut()
    {
        if (At(FromText) is not DateTime from) { Status = Tr.T("Begin: geef een tijd als 14:05 of 14:05:30."); return; }
        if (At(ToText) is not DateTime to) { Status = Tr.T("Einde: geef een tijd als 14:15 of 14:15:30."); return; }
        if (to <= from) to = to.AddDays(1);   // over midnight
        Busy = true;
        Status = $"Knippen: {from:HH:mm:ss} – {to:HH:mm:ss} …";
        try
        {
            var (r, error) = await Hub.Client.SendWithErrorAsync<RecordingCutDto>(HttpMethod.Post, "/api/recordings/cut",
                new RecordingCutRequest(from, to, Title, Artist));
            Status = r is null
                ? Tr.F("Niet gelukt: {0}", error)
                : Tr.F("In de bibliotheek (categorie Opnamen): {0}, {1:h\\:mm\\:ss}.", r.File, TimeSpan.FromSeconds(r.Seconds));
        }
        finally { Busy = false; }
    }
}

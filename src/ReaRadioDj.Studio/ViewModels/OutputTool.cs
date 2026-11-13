using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Controls;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>
/// Uitsturing: a large meter per mixer channel next to the program, so the level of everything is visible at a glance
/// from across the studio. Each channel has a lamp that is lit while it is open (switched to the transmitter, not muted,
/// fader up) and a dot while sound comes in; an open microphone lights red. With a mixing desk the lamps will follow the
/// desk's own channel switches once that desk is connected (hardware layer); until then they follow the software mixer.
/// </summary>
public sealed partial class OutputTool : StudioTool
{
    public OutputTool() : base("output", "Uitsturing") { }

    public ObservableCollection<OutputChannelVm> Channels { get; } = [];

    [ObservableProperty] private double _programLeft;
    [ObservableProperty] private double _programRight;
    [ObservableProperty] private string _programPeak = "–";
    [ObservableProperty] private string _lufs = "–";
    [ObservableProperty] private string _lufsIntegrated = "–";
    [ObservableProperty] private string _truePeak = "–";
    [ObservableProperty] private bool _limiter;
    [ObservableProperty] private string _openSummary = "";
    [ObservableProperty] private bool _micOpen;

    protected override void OnState(StateDto s)
    {
        var m = s.Mixer;
        ProgramLeft = m.ProgramLeft;
        ProgramRight = m.ProgramRight;
        ProgramPeak = OutputChannelVm.Db(Math.Max(m.ProgramLeft, m.ProgramRight));
        Lufs = m.Lufs is double l ? l.ToString("0.0", CultureInfo.InvariantCulture) : "–";
        LufsIntegrated = m.LufsIntegrated is double li ? li.ToString("0.0", CultureInfo.InvariantCulture) : "–";
        TruePeak = m.TruePeakDb is double tp ? tp.ToString("0.0", CultureInfo.InvariantCulture) : "–";
        Limiter = m.Limiter;
        MicOpen = m.Mic.Open;

        // keep the rows (and their meters' fall and peak hold) when the strips stay the same
        for (int i = 0; i < m.Strips.Count; i++)
        {
            var strip = m.Strips[i];
            int at = -1;
            for (int k = i; k < Channels.Count; k++) if (Channels[k].Id == strip.Id) { at = k; break; }
            if (at < 0) Channels.Insert(i, new OutputChannelVm(strip.Id));
            else if (at != i) Channels.Move(at, i);
            Channels[i].Update(strip, strip.Id == "mic" && m.Mic.Open);
        }
        while (Channels.Count > m.Strips.Count) Channels.RemoveAt(Channels.Count - 1);

        var open = Channels.Where(c => c.Open).Select(c => c.Name).ToList();
        OpenSummary = open.Count == 0 ? "Geen kanaal open" : "Open: " + string.Join(", ", open);
    }
}

public sealed partial class OutputChannelVm(string id) : ObservableObject
{
    private static readonly IBrush OpenBrush = new SolidColorBrush(Color.Parse("#3FB950"));
    private static readonly IBrush MicBrush = new SolidColorBrush(Color.Parse("#F25F5C"));
    private static readonly IBrush OffBrush = new SolidColorBrush(Color.Parse("#8B95A5"), 0.25);
    private static readonly IBrush SignalBrush = new SolidColorBrush(Color.Parse("#F5A524"));

    public string Id { get; } = id;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _route = "";
    [ObservableProperty] private double _left;
    [ObservableProperty] private double _right;
    [ObservableProperty] private string _peak = "–";
    [ObservableProperty] private bool _open;
    [ObservableProperty] private string _lampText = "DICHT";
    [ObservableProperty] private IBrush _lampBrush = OffBrush;
    [ObservableProperty] private IBrush _lampTextBrush = OffBrush;
    [ObservableProperty] private IBrush _signal = OffBrush;
    [ObservableProperty] private string _tip = "";

    /// <summary>Peak in whole dB for the readout under a meter.</summary>
    public static string Db(double linear) =>
        linear <= 0.0000001 || LevelMeter.ToDb(linear) <= LevelMeter.FloorDb ? "–" : LevelMeter.ToDb(linear).ToString("0", CultureInfo.InvariantCulture);

    public void Update(StripDto s, bool micOpen)
    {
        Name = s.Name;
        Left = s.LevelLeft;
        Right = s.LevelRight;
        Peak = Db(Math.Max(s.LevelLeft, s.LevelRight));
        bool isMic = s.Id == "mic";
        // open = it reaches the transmitter: switched on, not muted, fader up (a microphone: switched open)
        Open = isMic ? micOpen : s.OnAir && !s.Muted && s.Volume > 0.001 && s.Bus != "preview";
        LampText = Open ? (isMic ? "MIC OPEN" : "OPEN") : s.Muted ? "STIL" : "DICHT";
        LampBrush = Open ? (isMic ? MicBrush : OpenBrush) : OffBrush;
        LampTextBrush = Open ? Brushes.FromHex("#11161D") : Brushes.FromHex("#8B95A5");
        bool sound = LevelMeter.ToDb(Math.Max(s.LevelLeft, s.LevelRight)) > -45;
        Signal = sound ? SignalBrush : OffBrush;
        Route = s.Bus == "preview" ? "PFL-uitgang" : s.Source is not null ? "ingang" : "zender";
        string duck = s.Duck < 0.99 ? $" · gedimd tot {s.Duck * 100:0}%" : "";
        Tip = $"{s.Name}: fader {s.Volume * 100:0}%{(s.Muted ? " · gedempt" : "")}{(s.OnAir ? "" : " · niet naar de zender")}{duck}";
    }
}

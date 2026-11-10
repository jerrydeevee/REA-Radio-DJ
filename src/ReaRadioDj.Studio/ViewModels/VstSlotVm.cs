using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>One VST slot of a channel in the strip editor: choose a plugin, switch it on or off, set its parameters.</summary>
public sealed partial class VstSlotVm : ObservableObject
{
    private readonly string _channel;
    private readonly MixerTool _owner;
    private bool _loading;

    public VstSlotVm(MixerTool owner, string channel, VstSlotDto s)
    {
        _owner = owner;
        _channel = channel;
        Slot = s.Slot;
        Load(s);
    }

    public int Slot { get; }
    public string Title => $"Plek {Slot + 1}";
    public ObservableCollection<VstPluginDto> Plugins => _owner.VstPlugins;

    [ObservableProperty] private VstPluginDto? _plugin;
    [ObservableProperty] private bool _on;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _showParams;
    public ObservableCollection<VstParamVm> Params { get; } = [];

    public bool HasPlugin => !string.IsNullOrEmpty(Plugin?.Path);
    public bool HasError => Error.Length > 0;
    public bool HasParams => Params.Count > 0;
    public string ParamsText => ShowParams ? $"Parameters verbergen ({Params.Count})" : $"Parameters ({Params.Count})";

    private void Load(VstSlotDto s)
    {
        _loading = true;
        Plugin = s.Path.Length == 0
            ? Plugins.FirstOrDefault()
            : Plugins.FirstOrDefault(p => string.Equals(p.Path, s.Path, StringComparison.OrdinalIgnoreCase)) ?? AddUnlisted(s);
        On = s.On;
        Error = s.Error ?? "";
        Params.Clear();
        foreach (var p in s.Params ?? []) Params.Add(new VstParamVm(this, p));
        _loading = false;
        OnPropertyChanged(nameof(HasPlugin));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasParams));
        OnPropertyChanged(nameof(ParamsText));
    }

    // A plugin stored with the channel that is not (or no longer) in the scanned list still shows by name.
    private VstPluginDto AddUnlisted(VstSlotDto s)
    {
        var p = new VstPluginDto(s.Path, s.Name.Length > 0 ? s.Name : Path.GetFileNameWithoutExtension(s.Path), s.Vendor, 0, null);
        Plugins.Add(p);
        return p;
    }

    partial void OnShowParamsChanged(bool value) => OnPropertyChanged(nameof(ParamsText));

    partial void OnPluginChanged(VstPluginDto? value)
    {
        OnPropertyChanged(nameof(HasPlugin));
        if (_loading) return;
        // Choosing a plugin switches it on, so it is heard at once.
        _loading = true;
        On = !string.IsNullOrEmpty(value?.Path);
        _loading = false;
        _ = SendAsync();
    }

    partial void OnOnChanged(bool value)
    {
        if (!_loading) _ = SendAsync();
    }

    private async Task SendAsync()
    {
        var r = await StudioHub.Current.Client.SendForAsync<VstSlotDto>(HttpMethod.Put, $"/api/channels/{_channel}/vst/{Slot}",
            new VstSlotRequest(Plugin?.Path ?? "", On));
        if (r is null) { Error = "Niet gelukt (zie berichten)"; OnPropertyChanged(nameof(HasError)); return; }
        Load(r);
    }

    [RelayCommand] private void ToggleParams() => ShowParams = !ShowParams;

    private readonly SemaphoreSlim _sending = new(1, 1);

    internal async Task SendParamAsync(VstParamVm p)
    {
        int version = p.NextVersion();
        await _sending.WaitAsync();
        try
        {
            // When the operator moves faster than the engine answers, only the latest value of this parameter is sent.
            if (!p.IsLatest(version)) return;
            var r = await StudioHub.Current.Client.SendForAsync<VstParamDto>(HttpMethod.Put,
                $"/api/channels/{_channel}/vst/{Slot}/params/{p.Index}", new VstParamRequest((float)(p.Value / 100)));
            if (r is not null) p.Display = Format(r);
        }
        finally { _sending.Release(); }
    }

    internal static string Format(VstParamDto p)
    {
        // Many plugins print numbers like "0.000000"; show at most two decimals.
        string d = p.Display.Trim();
        if (double.TryParse(d, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n))
            d = Math.Round(n, 2).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
        return string.IsNullOrWhiteSpace(p.Unit) ? d : $"{d} {p.Unit.Trim()}";
    }
}

/// <summary>A parameter of a VST plugin as a slider (0..100 %) with the value as the plugin shows it.</summary>
public sealed partial class VstParamVm : ObservableObject
{
    private readonly VstSlotVm _slot;
    private readonly bool _loading;
    private int _version;

    public VstParamVm(VstSlotVm slot, VstParamDto p)
    {
        _slot = slot;
        Index = p.Index;
        Name = p.Name;
        _loading = true;
        Value = Math.Round(p.Value * 100, 1);
        Display = VstSlotVm.Format(p);
        _loading = false;
    }

    public int Index { get; }
    public string Name { get; }
    [ObservableProperty] private double _value;
    [ObservableProperty] private string _display = "";

    partial void OnValueChanged(double value)
    {
        if (!_loading) _ = _slot.SendParamAsync(this);
    }

    internal int NextVersion() => Interlocked.Increment(ref _version);
    internal bool IsLatest(int version) => version == Volatile.Read(ref _version);
}

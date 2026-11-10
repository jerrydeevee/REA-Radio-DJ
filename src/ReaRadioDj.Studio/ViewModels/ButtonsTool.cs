using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>One button of the Eigen knoppen window: a name, an engine command and a colour (kept in studio.json).</summary>
public sealed partial class CustomButtonVm : ObservableObject
{
    private readonly ButtonsTool _owner;

    public CustomButtonVm(ButtonsTool owner, CustomButton b)
    {
        _owner = owner;
        _label = b.Label;
        _command = b.Command;
        _colorIndex = Math.Max(0, Array.FindIndex(Appearance.KindColors, c => string.Equals(c.Hex, b.Color, StringComparison.OrdinalIgnoreCase)));
    }

    [ObservableProperty] private string _label;
    [ObservableProperty] private string _command;
    [ObservableProperty] private int _colorIndex;
    [ObservableProperty] private bool _isSelected;
    /// <summary>What the last press did: "✓" or the engine's error, shown for a few seconds.</summary>
    [ObservableProperty] private string _result = "";
    [ObservableProperty] private bool _failed;

    public IBrush Brush => Brushes.FromHex(Appearance.KindColors[Math.Clamp(ColorIndex, 0, Appearance.KindColors.Length - 1)].Hex);

    partial void OnLabelChanged(string value) => _owner.Persist();
    partial void OnCommandChanged(string value) => _owner.Persist();
    partial void OnColorIndexChanged(int value) { OnPropertyChanged(nameof(Brush)); _owner.Persist(); }

    public CustomButton ToModel() => new() { Label = Label, Command = Command, Color = Appearance.KindColors[Math.Clamp(ColorIndex, 0, Appearance.KindColors.Length - 1)].Hex };
}

/// <summary>
/// Eigen knoppen: a wall of buttons that each run an engine command (the same commands as hotkeys, TCP and OSC), e.g.
/// a jingle cart, the microphone, a lamp via HTTP or OSC, or REPLAN. "Bewerken" shows the editor; the buttons are kept
/// per studio computer.
/// </summary>
public sealed partial class ButtonsTool : StudioTool
{
    private bool _loading;

    public ButtonsTool() : base("buttons", "Eigen knoppen") { }

    public ObservableCollection<CustomButtonVm> Buttons { get; } = [];
    public ObservableCollection<CommandInfoDto> Commands { get; } = [];
    public IReadOnlyList<KindColorChoice> ColorChoices { get; } = [.. Appearance.KindColors.Select(c => new KindColorChoice(c.Name, Brushes.FromHex(c.Hex)))];

    [ObservableProperty] private bool _editing;
    [ObservableProperty] private CustomButtonVm? _selected;
    [ObservableProperty] private CommandInfoDto? _pickedCommand;

    public bool HasSelection => Selected is not null;
    public bool IsEmpty => Buttons.Count == 0;

    protected override void OnFirstShow()
    {
        _loading = true;
        foreach (var b in Hub.Settings.CustomButtons) Buttons.Add(new CustomButtonVm(this, b));
        _loading = false;
        OnPropertyChanged(nameof(IsEmpty));
        _ = LoadCommandsAsync();
    }

    private async Task LoadCommandsAsync()
    {
        if (await Hub.Client.GetAsync<List<CommandInfoDto>>("/api/commands") is not { } list) return;
        Commands.Clear();
        foreach (var c in list) Commands.Add(c);
    }

    partial void OnSelectedChanged(CustomButtonVm? oldValue, CustomButtonVm? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = Editing;
        OnPropertyChanged(nameof(HasSelection));
    }

    partial void OnEditingChanged(bool value)
    {
        if (Selected is not null) Selected.IsSelected = value;
        if (value && Selected is null && Buttons.Count > 0) Selected = Buttons[0];
    }

    /// <summary>Choosing a command from the list puts its syntax (up to the first placeholder) in the selected button.</summary>
    partial void OnPickedCommandChanged(CommandInfoDto? value)
    {
        if (value is null || Selected is null) return;
        var syntax = value.Syntax;
        int cut = syntax.IndexOfAny(['<', '[', '|']);
        Selected.Command = (cut < 0 ? syntax : syntax[..cut]).Trim() + (cut < 0 ? "" : " ");
        if (string.IsNullOrWhiteSpace(Selected.Label) || Selected.Label == "Nieuwe knop") Selected.Label = value.Description;
    }

    [RelayCommand]
    private async Task Press(CustomButtonVm? b)
    {
        if (b is null) return;
        if (Editing) { Selected = b; return; }
        if (string.IsNullOrWhiteSpace(b.Command)) { b.Failed = true; b.Result = "geen commando"; return; }
        var r = await Hub.Client.RunCommandAsync(b.Command.Trim());
        b.Failed = !r.Ok;
        b.Result = r.Ok ? "✓" : r.Text;
        if (!r.Ok) Hub.Status = $"{b.Label}: {r.Text}";
        var shown = b.Result;
        await Task.Delay(TimeSpan.FromSeconds(r.Ok ? 1.5 : 5));
        if (b.Result == shown) b.Result = "";
    }

    [RelayCommand]
    private void Add()
    {
        var b = new CustomButtonVm(this, new CustomButton { Label = "Nieuwe knop", Command = "", Color = Appearance.KindColors[0].Hex });
        Buttons.Add(b);
        Editing = true;
        Selected = b;
        Persist();
    }

    [RelayCommand]
    private void Remove()
    {
        if (Selected is not { } b) return;
        int i = Buttons.IndexOf(b);
        Buttons.Remove(b);
        Selected = Buttons.Count == 0 ? null : Buttons[Math.Min(i, Buttons.Count - 1)];
        Persist();
    }

    [RelayCommand]
    private void Move(string? direction)
    {
        if (Selected is not { } b) return;
        int i = Buttons.IndexOf(b), j = direction == "left" ? i - 1 : i + 1;
        if (j < 0 || j >= Buttons.Count) return;
        Buttons.Move(i, j);
        Persist();
    }

    /// <summary>Writes the buttons to studio.json (called after every change).</summary>
    public void Persist()
    {
        if (_loading) return;
        Hub.Settings.CustomButtons = [.. Buttons.Select(b => b.ToModel())];
        Hub.Settings.Save();
        OnPropertyChanged(nameof(IsEmpty));
    }
}

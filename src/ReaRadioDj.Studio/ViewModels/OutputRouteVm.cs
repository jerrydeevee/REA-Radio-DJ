using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>One mixer channel in the list "Uitgang per mengkanaal": its output and a test tone.</summary>
public sealed partial class OutputRouteVm : ObservableObject
{
    private readonly SettingsTool _owner;
    private bool _loading;

    public OutputRouteVm(SettingsTool owner, OutputRouteDto dto, IReadOnlyList<OutputChoiceDto> choices)
    {
        _owner = owner;
        Strip = dto.Strip;
        Name = dto.Name;
        Choices = choices;
        _loading = true;
        Target = choices.FirstOrDefault(c => c.Id == dto.Target) ?? choices[0];
        _loading = false;
    }

    public string Strip { get; }
    public string Name { get; }
    public IReadOnlyList<OutputChoiceDto> Choices { get; }

    [ObservableProperty] private OutputChoiceDto? _target;

    partial void OnTargetChanged(OutputChoiceDto? value)
    {
        if (!_loading && value is not null) _ = _owner.RouteAsync(this, value);
    }

    /// <summary>Puts the selection back without sending it again (after the engine refused it).</summary>
    public void Show(string target)
    {
        _loading = true;
        Target = Choices.FirstOrDefault(c => c.Id == target) ?? Choices[0];
        _loading = false;
    }

    [RelayCommand] private Task Test() => _owner.TestToneAsync(string.IsNullOrEmpty(Target?.Id) ? (Strip == "pfl" ? "preview" : "program") : Target.Id);
}

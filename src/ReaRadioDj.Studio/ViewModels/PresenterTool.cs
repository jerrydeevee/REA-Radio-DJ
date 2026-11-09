using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>One upcoming AI break; the script can be edited while it waits for approval.</summary>
public sealed partial class BreakVm : ObservableObject
{
    public long Id { get; init; }
    [ObservableProperty] private DateTime _startsAt;
    [ObservableProperty] private string _state = "";
    [ObservableProperty] private string _info = "";
    [ObservableProperty] private string _script = "";
    [ObservableProperty] private string _editScript = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _waiting;
    [ObservableProperty] private bool _busy;

    public string StateText => State switch
    {
        "pending" => "wacht",
        "rendering" => "bezig…",
        "awaitingApproval" => "goedkeuren",
        "ready" => "klaar",
        "failed" => "mislukt (jingle in de plaats)",
        _ => State,
    };

    partial void OnStateChanged(string value) => OnPropertyChanged(nameof(StateText));

    public void Update(LogItemDto d)
    {
        StartsAt = d.StartsAt;
        bool nowWaiting = d.BreakState == "awaitingApproval";
        if (nowWaiting && (!Waiting || EditScript.Length == 0)) EditScript = d.Script ?? "";
        Waiting = nowWaiting;
        State = d.BreakState ?? "";
        Script = d.Script ?? "";
        Error = d.Error;
        Info = string.Join(" · ", new[] { d.ScriptSource, d.Voice }.Where(x => !string.IsNullOrEmpty(x)));
        if (!nowWaiting) Busy = State == "rendering";
    }
}

/// <summary>
/// AI presenter: the upcoming breaks (approve, edit or rewrite the script before it is spoken) and the settings:
/// language model (any OpenAI-compatible server, such as Ollama or LM Studio on this PC), persona, length, approval
/// and post-processing. A trial script shows what the model writes for the next two songs.
/// </summary>
public sealed partial class PresenterTool : StudioTool
{
    public PresenterTool() : base("presenter", "AI-presentator") { }

    public ObservableCollection<BreakVm> Breaks { get; } = [];
    public ObservableCollection<string> Models { get; } = [];
    [ObservableProperty] private string _nowScript = "";

    // settings
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private string _presenterName = "";
    [ObservableProperty] private string _persona = "";
    [ObservableProperty] private decimal? _maxWords = 45;
    [ObservableProperty] private string _llmBaseUrl = "";
    [ObservableProperty] private string? _llmModel = "";
    [ObservableProperty] private string _llmApiKey = "";
    [ObservableProperty] private decimal? _temperature = 0.8m;
    [ObservableProperty] private bool _requireApproval;
    [ObservableProperty] private bool _postProcess = true;
    [ObservableProperty] private string _llmStatus = "";
    [ObservableProperty] private string _trial = "";
    [ObservableProperty] private bool _trying;

    protected override void OnFirstShow() => _ = LoadSettingsAsync();

    protected override void OnState(StateDto s)
    {
        NowScript = s.Engine.Current is { Type: "voice" } c ? c.Script ?? c.Title : "";
        var breaks = s.Upcoming.Where(u => u.Type == "aiBreak").ToList();
        for (int i = Breaks.Count - 1; i >= 0; i--)
            if (breaks.All(b => b.Id != Breaks[i].Id)) Breaks.RemoveAt(i);
        for (int i = 0; i < breaks.Count; i++)
        {
            var vm = Breaks.FirstOrDefault(b => b.Id == breaks[i].Id);
            if (vm is null) { vm = new BreakVm { Id = breaks[i].Id }; Breaks.Insert(Math.Min(i, Breaks.Count), vm); }
            vm.Update(breaks[i]);
        }
    }

    [RelayCommand] private Task AddNext() => Hub.Client.PostAsync("/api/log/break", new AddBreakRequest(0));
    [RelayCommand] private Task Remove(BreakVm? b) => b is null ? Task.CompletedTask : Hub.Client.DeleteAsync($"/api/log/{b.Id}");

    [RelayCommand]
    private async Task Approve(BreakVm? b)
    {
        if (b is null) return;
        b.Busy = true;
        if (await Hub.Client.PostAsync($"/api/log/{b.Id}/break/approve", new ApproveBreakRequest(b.EditScript)))
            Hub.Status = "AI-break goedgekeurd en ingesproken";
        b.Busy = false;
    }

    [RelayCommand]
    private async Task Rewrite(BreakVm? b)
    {
        if (b is null) return;
        b.EditScript = "";
        await Hub.Client.PostAsync($"/api/log/{b.Id}/break/rewrite");
    }

    // ---------------------------------------------------------------- settings

    private async Task LoadSettingsAsync()
    {
        if (await Hub.Client.GetAsync<PresenterSettingsDto>("/api/presenter/settings") is not { } d) return;
        Enabled = d.Enabled;
        PresenterName = d.PresenterName;
        Persona = d.Persona;
        MaxWords = d.MaxWords;
        LlmBaseUrl = d.LlmBaseUrl;
        LlmModel = d.LlmModel;
        LlmApiKey = d.LlmApiKey;
        Temperature = (decimal)d.Temperature;
        RequireApproval = d.RequireApproval;
        PostProcess = d.PostProcess;
        await RefreshModelsAsync();
    }

    /// <summary>Fills in the usual address of Ollama on this PC.</summary>
    [RelayCommand]
    private Task UseOllama()
    {
        LlmBaseUrl = "http://localhost:11434";
        return RefreshModelsAsync();
    }

    [RelayCommand]
    private async Task RefreshModelsAsync()
    {
        if (string.IsNullOrWhiteSpace(LlmBaseUrl)) { LlmStatus = "geen taalmodel: de presentator gebruikt sjabloonteksten"; return; }
        var r = await Hub.Client.GetAsync<LlmModelsDto>($"/api/presenter/models?url={Uri.EscapeDataString(LlmBaseUrl)}");
        if (r is null) return;
        var keep = LlmModel;
        Models.Clear();
        foreach (var m in r.Models) Models.Add(m);
        if (!string.IsNullOrEmpty(keep) && !Models.Contains(keep)) Models.Insert(0, keep);
        LlmModel = keep is { Length: > 0 } ? keep : Models.FirstOrDefault();
        LlmStatus = r.Ok ? $"verbonden · {r.Models.Count} modellen" : r.Error ?? "niet bereikbaar";
    }

    [RelayCommand]
    private async Task Save()
    {
        var d = new PresenterSettingsDto(Enabled, PresenterName, Persona, (int)(MaxWords ?? 45), LlmBaseUrl, LlmModel ?? "", LlmApiKey,
            (double)(Temperature ?? 0.8m), RequireApproval, PostProcess);
        if (await Hub.Client.SendForAsync<PresenterSettingsDto>(HttpMethod.Put, "/api/presenter/settings", d) is { } saved)
        {
            LlmApiKey = saved.LlmApiKey;
            Hub.Status = "AI-presentator: instellingen opgeslagen";
        }
    }

    /// <summary>Saves, then lets the model write a script for the next two songs (nothing is spoken or aired).</summary>
    [RelayCommand]
    private async Task Try()
    {
        if (Trying) return;
        Trying = true;
        Trial = "schrijven…";
        try
        {
            await Save();
            var r = await Hub.Client.SendForAsync<ScriptTryDto>(HttpMethod.Post, "/api/presenter/try", null);
            Trial = r is null ? "geen antwoord" : $"{r.Script}\n\n— {r.Source}, {r.Words} woorden, {r.Seconds:0.0} s";
        }
        finally { Trying = false; }
    }
}

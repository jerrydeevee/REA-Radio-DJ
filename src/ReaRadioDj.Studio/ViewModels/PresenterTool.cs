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
        string? extra = d.Extra switch { "weather" => "weer", "news" => "nieuws", "note" => "bericht", _ => null };
        Info = string.Join(" · ", new[] { d.ScriptSource, d.Voice, extra }.Where(x => !string.IsNullOrEmpty(x)));
        if (!nowWaiting) Busy = State == "rendering";
    }
}

/// <summary>An entry of a choice list with an optional id ("(geen)" / "(standaard)" have none).</summary>
public sealed record Choice(long? Id, string Name)
{
    public override string ToString() => Name;
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
    [ObservableProperty] private Choice? _bed;

    public ObservableCollection<Choice> BedChoices { get; } = [];
    public ObservableCollection<Choice> VoiceChoices { get; } = [];

    // weather, news, notes and outro
    [ObservableProperty] private Choice? _outro;
    [ObservableProperty] private bool _weatherEnabled;
    [ObservableProperty] private string _place = "";
    [ObservableProperty] private decimal? _latitude;
    [ObservableProperty] private decimal? _longitude;
    [ObservableProperty] private decimal? _weatherEvery = 30;
    [ObservableProperty] private bool _newsEnabled;
    [ObservableProperty] private string _newsFeeds = "";
    [ObservableProperty] private decimal? _newsEvery = 60;
    [ObservableProperty] private string _extrasPreview = "";
    public ObservableCollection<PresenterNoteDto> Notes { get; } = [];
    [ObservableProperty] private string _noteText = "";
    [ObservableProperty] private DateTime? _noteUntil;
    [ObservableProperty] private decimal? _noteUses = 1;

    private async Task LoadExtrasAsync()
    {
        if (await Hub.Client.GetAsync<PresenterExtrasDto>("/api/presenter/extras") is { } e)
        {
            Outro = BedChoices.FirstOrDefault(c => c.Id == e.OutroCategoryId) ?? BedChoices.FirstOrDefault();
            WeatherEnabled = e.WeatherEnabled;
            Place = e.Place;
            Latitude = (decimal)e.Latitude;
            Longitude = (decimal)e.Longitude;
            WeatherEvery = e.WeatherEveryMinutes;
            NewsEnabled = e.NewsEnabled;
            NewsFeeds = e.NewsFeeds;
            NewsEvery = e.NewsEveryMinutes;
        }
        await LoadNotesAsync();
    }

    [RelayCommand]
    private async Task SaveExtras()
    {
        var d = new PresenterExtrasDto(Outro?.Id, WeatherEnabled, Place, (double)(Latitude ?? 0), (double)(Longitude ?? 0),
            (int)(WeatherEvery ?? 30), NewsEnabled, NewsFeeds, (int)(NewsEvery ?? 60));
        if (await Hub.Client.PutAsync("/api/presenter/extras", d)) Hub.Status = "AI-presentator: weer, nieuws en outro opgeslagen";
    }

    /// <summary>Saves, then shows what the presenter would say about the weather and the news right now.</summary>
    [RelayCommand]
    private async Task PreviewExtras()
    {
        ExtrasPreview = "ophalen…";
        await SaveExtras();
        var r = await Hub.Client.GetAsync<ExtrasPreviewDto>("/api/presenter/extras/preview");
        ExtrasPreview = r is null ? "geen antwoord" : string.Join("\n", new[]
        {
            r.Weather is null ? null : "Weer: " + r.Weather,
            r.Headline is null ? null : "Nieuws: " + r.Headline,
            r.Error,
        }.Where(x => x is not null));
    }

    private async Task LoadNotesAsync()
    {
        if (await Hub.Client.GetAsync<List<PresenterNoteDto>>("/api/presenter/notes") is not { } list) return;
        Notes.Clear();
        foreach (var n in list) Notes.Add(n);
    }

    [RelayCommand]
    private async Task AddNote()
    {
        if (string.IsNullOrWhiteSpace(NoteText)) return;
        var until = NoteUntil is DateTime d ? d.Date.AddDays(1).AddSeconds(-1) : (DateTime?)null;   // through the chosen day
        if (await Hub.Client.PostAsync("/api/presenter/notes", new AddNoteRequest(NoteText.Trim(), until, (int)(NoteUses ?? 1))))
        {
            NoteText = "";
            await LoadNotesAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteNote(PresenterNoteDto? n)
    {
        if (n is not null && await Hub.Client.DeleteAsync($"/api/presenter/notes/{n.Id}")) await LoadNotesAsync();
    }

    // personas per daypart
    public ObservableCollection<PersonaDto> Personas { get; } = [];
    public ObservableCollection<DayChoice> PersonaDays { get; } = new(PlannerTool.DayNames.Select((n, i) => new DayChoice(i, n)));
    [ObservableProperty] private PersonaDto? _selectedPersona;
    [ObservableProperty] private string _personaName = "";
    [ObservableProperty] private bool _personaEnabled = true;
    [ObservableProperty] private string _personaHours = "*";
    [ObservableProperty] private string _personaPresenter = "";
    [ObservableProperty] private string _personaCharacter = "";
    [ObservableProperty] private decimal? _personaMaxWords;
    [ObservableProperty] private Choice? _personaVoice;
    [ObservableProperty] private Choice? _personaBed;
    [ObservableProperty] private string _personaNow = "";

    partial void OnSelectedPersonaChanged(PersonaDto? value)
    {
        if (value is null) return;
        PersonaName = value.Name;
        PersonaEnabled = value.Enabled;
        PersonaHours = value.Hours;
        PersonaPresenter = value.PresenterName;
        PersonaCharacter = value.Character;
        PersonaMaxWords = value.MaxWords;
        foreach (var d in PersonaDays) d.On = (value.Days & (1 << d.Index)) != 0;
        PersonaVoice = VoiceChoices.FirstOrDefault(c => c.Id == value.VoiceId) ?? VoiceChoices.FirstOrDefault();
        PersonaBed = BedChoices.FirstOrDefault(c => c.Id == value.BedCategoryId) ?? BedChoices.FirstOrDefault();
    }

    [RelayCommand]
    private void NewPersona()
    {
        SelectedPersona = null;
        PersonaName = "Nieuwe persona";
        PersonaEnabled = true;
        PersonaHours = "6-9";
        PersonaPresenter = "";
        PersonaCharacter = "";
        PersonaMaxWords = null;
        foreach (var d in PersonaDays) d.On = d.Index < 5;
        PersonaVoice = VoiceChoices.FirstOrDefault();
        PersonaBed = BedChoices.FirstOrDefault();
    }

    [RelayCommand]
    private async Task SavePersona()
    {
        int days = PersonaDays.Where(d => d.On).Sum(d => 1 << d.Index);
        var d = new PersonaDto(SelectedPersona?.Id ?? 0, PersonaName, PersonaEnabled, days, PersonaHours, PersonaPresenter, PersonaCharacter,
            PersonaVoice?.Id, PersonaMaxWords is decimal w ? (int)w : null, PersonaBed?.Id, SelectedPersona?.Sort ?? Personas.Count);
        bool ok = d.Id > 0
            ? await Hub.Client.PutAsync($"/api/presenter/personas/{d.Id}", d)
            : await Hub.Client.PostAsync("/api/presenter/personas", d);
        if (ok) { Hub.Status = $"persona '{d.Name}' opgeslagen"; await LoadPersonasAsync(d.Name); }
    }

    [RelayCommand]
    private async Task DeletePersona()
    {
        if (SelectedPersona is { } p && await Hub.Client.DeleteAsync($"/api/presenter/personas/{p.Id}")) await LoadPersonasAsync(null);
    }

    private async Task LoadPersonasAsync(string? select)
    {
        if (await Hub.Client.GetAsync<List<PersonaDto>>("/api/presenter/personas") is not { } list) return;
        Personas.Clear();
        foreach (var p in list) Personas.Add(p);
        PersonaNow = list.FirstOrDefault(p => p.OnAirNow) is { } now ? $"nu: {now.Name}" : "nu: algemene instellingen";
        SelectedPersona = Personas.FirstOrDefault(p => p.Name == select) ?? Personas.FirstOrDefault();
    }

    private async Task LoadChoicesAsync()
    {
        var cats = await Hub.Client.GetAsync<List<CategoryDto>>("/api/categories") ?? [];
        BedChoices.Clear();
        BedChoices.Add(new Choice(null, "(geen bed)"));
        foreach (var c in cats.Where(c => c.Kind != "song")) BedChoices.Add(new Choice(c.Id, c.Name));
        var voices = await Hub.Client.GetAsync<List<VoiceProfileDto>>("/api/voice/profiles") ?? [];
        VoiceChoices.Clear();
        VoiceChoices.Add(new Choice(null, "(standaardstem)"));
        foreach (var v in voices) VoiceChoices.Add(new Choice(v.Id, v.Name));
    }

    protected override void OnFirstShow() => _ = LoadAllAsync();

    private async Task LoadAllAsync()
    {
        await LoadChoicesAsync();
        await LoadSettingsAsync();
        await LoadPersonasAsync(null);
        await LoadExtrasAsync();
    }

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
        Bed = BedChoices.FirstOrDefault(c => c.Id == d.BedCategoryId) ?? BedChoices.FirstOrDefault();
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
            (double)(Temperature ?? 0.8m), RequireApproval, PostProcess, Bed?.Id);
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
            var r = await Hub.Client.SendForAsync<ScriptTryDto>(HttpMethod.Post, "/api/presenter/try", new ScriptTryRequest(null));
            Trial = r is null ? "geen antwoord" : Describe(r);
        }
        finally { Trying = false; }
    }

    /// <summary>A trial script written as the selected persona (after saving it).</summary>
    [RelayCommand]
    private async Task TryPersona()
    {
        if (Trying) return;
        Trying = true;
        Trial = "schrijven…";
        try
        {
            await SavePersona();
            var r = await Hub.Client.SendForAsync<ScriptTryDto>(HttpMethod.Post, "/api/presenter/try", new ScriptTryRequest(SelectedPersona?.Id));
            Trial = r is null ? "geen antwoord" : Describe(r);
        }
        finally { Trying = false; }
    }

    private static string Describe(ScriptTryDto r) =>
        $"{r.Script}\n\n— {(r.Persona is null ? "" : r.Persona + ", ")}{r.Source}, {r.Words} woorden, {r.Seconds:0.0} s";
}

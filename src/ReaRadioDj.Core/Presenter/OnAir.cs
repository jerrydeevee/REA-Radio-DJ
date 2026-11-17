using System.Globalization;
using ReaRadioDj.Contracts.Localization;

namespace ReaRadioDj.Core.Presenter;

/// <summary>
/// The language the AI presenter speaks on air (Presenter.Language: nl, en, de or es), apart from the language of the
/// studio. Instructions to the language model stay Dutch and say which language to write; sentences the engine says
/// itself (templates, the words around news and weather) come from strings.tsv through <see cref="Say"/>.
/// </summary>
public static class OnAir
{
    private static PresenterOptions? _options;

    /// <summary>Follow these options; a language changed in the studio counts from the next break.</summary>
    public static void Use(PresenterOptions options) => _options = options;

    private static readonly AsyncLocal<string?> _override = new();

    /// <summary>The on-air language code.</summary>
    public static string Code => _override.Value ?? Tr.Normalize(_options?.Language);

    /// <summary>For tests: another on-air language for this async flow only, until disposed.</summary>
    internal static IDisposable Speak(string code)
    {
        var before = _override.Value;
        _override.Value = Tr.Normalize(code);
        return new Restore(() => _override.Value = before);
    }

    private sealed class Restore(Action undo) : IDisposable { public void Dispose() => undo(); }

    /// <summary>Marks a Dutch word or sentence that is said on air later through <see cref="Say"/> (so scripts/strings.py
    /// collects it); returns it unchanged.</summary>
    public static string Line(string nl) => nl;

    /// <summary>A fixed Dutch sentence in the on-air language.</summary>
    public static string Say(string nl) => Tr.In(Code, nl);

    /// <summary>A fixed Dutch sentence in the on-air language with {0}, {1}… filled in.</summary>
    public static string Say(string nl, params object?[] args) => string.Format(Culture, Tr.In(Code, nl), args);

    public static CultureInfo Culture => Code switch
    {
        "en" => CultureInfo.GetCultureInfo("en-GB"),
        "de" => CultureInfo.GetCultureInfo("de-DE"),
        "es" => CultureInfo.GetCultureInfo("es-ES"),
        _ => CultureInfo.GetCultureInfo("nl-NL"),
    };

    /// <summary>The language's name as the Dutch instructions say it: "Nederlands", "Engels"…</summary>
    public static string NameInDutch => Code switch { "en" => "Engels", "de" => "Duits", "es" => "Spaans", _ => "Nederlands" };

    /// <summary>The line in the instructions that says which language to write.</summary>
    public static string WriteIn(string how) => Code == "nl"
        ? $"in {how} Nederlands"
        : $"in {how} {NameInDutch} (de instructies zijn Nederlands, maar alles wat je schrijft is {NameInDutch})";

    /// <summary>The rule against slipping into another language.</summary>
    public static string NoOtherLanguage => Code == "nl" ? "Engelse zinnen" : "zinnen in een andere taal dan het " + NameInDutch;

    /// <summary>Two example breaks in the on-air language, with made-up songs so they are never copied literally.</summary>
    public static string Examples(string station) => Code switch
    {
        "en" => $"""
            That was Summer Evening by The Coast Band. It's quarter past four and you're listening to {station}. Here's Lisa Moore with Never Alone Again!
            On {station} it's twenty past eight. After Rain in May by Tom Brink, here's one to sing along to: Lights On by The Cyclists.
            """,
        "de" => $"""
            Das war Sommerabend von der Küstenband. Es ist Viertel nach vier, du hörst {station}. Jetzt Lisa Moll mit Nie Mehr Allein!
            Bei {station} ist es zwanzig nach acht. Nach Regen im Mai von Tom Brink kommt einer zum Mitsingen: Licht An von den Radlern.
            """,
        "es" => $"""
            Eso fue Tarde de Verano de La Banda de la Costa. Son las cuatro y cuarto y estás escuchando {station}. ¡Ahora Lisa Mora con Nunca Más Sola!
            En {station} son las ocho y veinte. Después de Lluvia en Mayo de Tomás Brink, una para cantar: Luces Encendidas de Los Ciclistas.
            """,
        _ => $"""
            Dat was Zomeravond van De Kustband. Het is kwart over vier, je luistert naar {station}. Nu Lisa Mol met Nooit Meer Alleen!
            Op {station} is het tien voor half negen. Na Regen in Mei van Tom Brink zet ik er eentje op om lekker mee te zingen: Licht Aan van De Fietsers.
            """,
    };

    /// <summary>The time as radio says it, rounded to five minutes: "kwart over drie", "quarter past three", "Viertel nach drei", "las tres y cuarto".</summary>
    public static string SpokenTime(DateTime t) => SpokenTime(t, Code);

    public static string SpokenTime(DateTime t, string code)
    {
        int minute = (int)Math.Round(t.Minute / 5.0) * 5;
        int hour = t.Hour;
        if (minute == 60) { minute = 0; hour++; }
        return code switch
        {
            "en" => English(hour, minute),
            "de" => German(hour, minute),
            "es" => Spanish(hour, minute),
            _ => Dutch(hour, minute),
        };
    }

    private static string Word(string[] words, int hour) => words[((hour % 12) + 12) % 12];

    private static string Dutch(int hour, int minute)
    {
        string[] w = ["twaalf", "een", "twee", "drie", "vier", "vijf", "zes", "zeven", "acht", "negen", "tien", "elf"];
        string H(int h) => Word(w, h);
        return minute switch
        {
            0 => $"{H(hour)} uur",
            5 => $"vijf over {H(hour)}",
            10 => $"tien over {H(hour)}",
            15 => $"kwart over {H(hour)}",
            20 => $"tien voor half {H(hour + 1)}",
            25 => $"vijf voor half {H(hour + 1)}",
            30 => $"half {H(hour + 1)}",
            35 => $"vijf over half {H(hour + 1)}",
            40 => $"tien over half {H(hour + 1)}",
            45 => $"kwart voor {H(hour + 1)}",
            50 => $"tien voor {H(hour + 1)}",
            _ => $"vijf voor {H(hour + 1)}",
        };
    }

    private static string English(int hour, int minute)
    {
        string[] w = ["twelve", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven"];
        string H(int h) => Word(w, h);
        return minute switch
        {
            0 => $"{H(hour)} o'clock",
            15 => $"quarter past {H(hour)}",
            30 => $"half past {H(hour)}",
            45 => $"quarter to {H(hour + 1)}",
            < 30 => $"{Minutes(minute)} past {H(hour)}",
            _ => $"{Minutes(60 - minute)} to {H(hour + 1)}",
        };
        static string Minutes(int m) => m switch { 5 => "five", 10 => "ten", 20 => "twenty", _ => "twenty-five" };
    }

    private static string German(int hour, int minute)
    {
        string[] w = ["zwölf", "eins", "zwei", "drei", "vier", "fünf", "sechs", "sieben", "acht", "neun", "zehn", "elf"];
        string H(int h) => Word(w, h);
        string Full(int h) => H(h) == "eins" ? "ein" : H(h);
        return minute switch
        {
            0 => $"{Full(hour)} Uhr",
            5 => $"fünf nach {H(hour)}",
            10 => $"zehn nach {H(hour)}",
            15 => $"Viertel nach {H(hour)}",
            20 => $"zwanzig nach {H(hour)}",
            25 => $"fünf vor halb {H(hour + 1)}",
            30 => $"halb {H(hour + 1)}",
            35 => $"fünf nach halb {H(hour + 1)}",
            40 => $"zwanzig vor {H(hour + 1)}",
            45 => $"Viertel vor {H(hour + 1)}",
            50 => $"zehn vor {H(hour + 1)}",
            _ => $"fünf vor {H(hour + 1)}",
        };
    }

    private static string Spanish(int hour, int minute)
    {
        string[] w = ["doce", "una", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve", "diez", "once"];
        string H(int h) => (Word(w, h) == "una" ? "la " : "las ") + Word(w, h);
        return minute switch
        {
            0 => $"{H(hour)} en punto",
            15 => $"{H(hour)} y cuarto",
            30 => $"{H(hour)} y media",
            45 => $"{H(hour + 1)} menos cuarto",
            < 30 => $"{H(hour)} y {Minutes(minute)}",
            _ => $"{H(hour + 1)} menos {Minutes(60 - minute)}",
        };
        static string Minutes(int m) => m switch { 5 => "cinco", 10 => "diez", 20 => "veinte", _ => "veinticinco" };
    }

    /// <summary>Temperature as said on air: "1 graad", "min 3 graden", "12 degrees"…</summary>
    public static string Degrees(int t) => t switch
    {
        1 => OnAir.Say("1 graad"),
        -1 => OnAir.Say("min 1 graad"),
        < 0 => OnAir.Say("min {0} graden", -t),
        _ => OnAir.Say("{0} graden", t),
    };

    /// <summary>A default neural voice for the on-air language, used when a profile names none.</summary>
    public static string DefaultCloudVoice => Code switch
    {
        "en" => "en-GB-SoniaNeural",
        "de" => "de-DE-KatjaNeural",
        "es" => "es-ES-ElviraNeural",
        _ => "nl-NL-FennaNeural",
    };
}

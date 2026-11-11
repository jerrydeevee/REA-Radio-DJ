using System.Globalization;
using Avalonia.Data.Converters;
using CommunityToolkit.Mvvm.ComponentModel;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>One field the operator can show in the library rows, switched on or off and moved up or down.</summary>
public sealed partial class FieldChoice(string key, string label, bool on, Action changed) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    [ObservableProperty] private bool _isOn = on;
    partial void OnIsOnChanged(bool value) => changed();
}

/// <summary>
/// The fields under the title of each library row, in the operator's own order. The row template asks
/// <see cref="Line"/> for the text; the list of fields lives here so the converter needs no view model.
/// </summary>
public static class LibraryColumns
{
    public static readonly IReadOnlyList<(string Key, string Label)> All =
    [
        ("artist", "Artiest"), ("category", "Categorie"), ("album", "Album"), ("year", "Jaar"), ("genre", "Genre"),
        ("bpm", "BPM"), ("key", "Toonsoort"), ("lufs", "LUFS"), ("replaygain", "ReplayGain"), ("plays", "Keer gespeeld"),
        ("lastplayed", "Laatst gespeeld"), ("added", "Toegevoegd"), ("language", "Taal"), ("mood", "Stemming"),
        ("rating", "Waardering"), ("energy", "Energie"), ("bitrate", "Bitrate"),
    ];

    public static readonly string[] Default = ["artist", "category", "year", "bpm", "key"];

    /// <summary>The fields shown now, in order.</summary>
    public static IReadOnlyList<string> Current { get; set; } = Default;

    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>The text of one field for one item; empty when the item has no value for it.</summary>
    public static string Field(MediaDto m, string key) => key switch
    {
        "artist" => m.Artist,
        "category" => m.Category,
        "album" => m.Album,
        "year" => m.Year?.ToString(CultureInfo.InvariantCulture) ?? "",
        "genre" => m.Genre,
        "bpm" => m.Bpm is double b ? $"{b:0} BPM" : "",
        "key" => m.Camelot is { } c ? $"{c} {m.Key}" : "",
        "lufs" => m.Lufs is double l ? string.Format(Nl, "{0:0.0} LUFS", l) : "",
        "replaygain" => m.ReplayGain is double rg ? string.Format(Nl, "RG {0:+0.0;-0.0} dB", rg) : "",
        "plays" => m.PlayCount > 0 ? $"{m.PlayCount}×" : "nooit gespeeld",
        "lastplayed" => m.LastPlayed is DateTime lp ? $"laatst {lp:d-M HH:mm}" : "",
        "added" => m.Added is DateTime a ? $"sinds {a:d-M-yyyy}" : "",
        "language" => m.Language,
        "mood" => m.Mood,
        "rating" => m.Rating is int r and > 0 ? new string('★', Math.Clamp(r, 1, 5)) : "",
        "energy" => m.EnergyLevel is int e and > 0 ? $"energie {e}" : "",
        "bitrate" => m.Bitrate is int k ? $"{k} kbps" : "",
        _ => "",
    };

    public static string Line(MediaDto m, IEnumerable<string> keys) =>
        string.Join(" · ", keys.Select(k => Field(m, k)).Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>For the row template: the chosen fields of the bound item.</summary>
    public static readonly IValueConverter Text = new FuncValueConverter<MediaDto?, string>(m => m is null ? "" : Line(m, Current));
}

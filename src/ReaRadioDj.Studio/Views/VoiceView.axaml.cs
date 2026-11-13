using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class VoiceView : UserControl
{
    public VoiceView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
        ClipPick.Click += async (_, _) =>
        {
            if (DataContext is VoiceTool t && await PickWavAsync("Opname van de stem") is { } wav) await t.UploadClipAsync(wav);
        };
        OwnPick.Click += async (_, _) =>
        {
            if (DataContext is VoiceTool t && await PickWavAsync("Opname van je eigen stem") is { } wav) await t.OwnUploadAsync(wav);
        };
    }

    /// <summary>A WAV file from this computer (the studio may run on another PC than the engine, so the bytes are sent).</summary>
    private async Task<byte[]?> PickWavAsync(string title)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("WAV-opname") { Patterns = ["*.wav"] }],
        });
        if (files.Count == 0) return null;
        await using var stream = await files[0].OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }
}

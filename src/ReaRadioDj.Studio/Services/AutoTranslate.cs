using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using ReaRadioDj.Contracts.Localization;

namespace ReaRadioDj.Studio.Services;

/// <summary>
/// Translates the fixed texts of the views (written in Dutch in the .axaml files) when a control is loaded: button and
/// label texts, headers, tooltips, placeholders and window titles. Texts that come from a binding are left alone; those
/// are translated by the view model with <see cref="Tr.T"/>. Does nothing when the studio language is Dutch.
/// </summary>
public static class AutoTranslate
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed || Tr.Language == "nl") return;
        _installed = true;
        Control.LoadedEvent.AddClassHandler<Control>((c, _) => Apply(c), RoutingStrategies.Direct);
    }

    /// <summary>Translates one control's own fixed texts (not its children).</summary>
    public static void Apply(Control c)
    {
        Translate(c, ToolTip.TipProperty);
        switch (c)
        {
            case TextBlock tb:
                Translate(tb, TextBlock.TextProperty);
                if (tb.Inlines is { Count: > 0 } inlines)
                    foreach (var run in inlines.OfType<Run>()) Translate(run, Run.TextProperty);
                break;
            case TextBox box:
                Translate(box, TextBox.WatermarkProperty);
                break;
            case ComboBox combo:
                Translate(combo, ComboBox.PlaceholderTextProperty);
                break;
            case Window w:
                Translate(w, Window.TitleProperty);
                break;
        }
        if (c is HeaderedContentControl hc) Translate(hc, HeaderedContentControl.HeaderProperty);
        if (c is HeaderedSelectingItemsControl hs) Translate(hs, HeaderedSelectingItemsControl.HeaderProperty);
        if (c is HeaderedItemsControl hi) Translate(hi, HeaderedItemsControl.HeaderProperty);
        if (c is ContentControl cc && c is not Window) Translate(cc, ContentControl.ContentProperty);
        // Lists of plain texts (choices such as "links", "rechts", "uit"): the value stays Dutch, only the display changes.
        if (c is ItemsControl ic and not MenuItem and not TabControl and not Menu && ic.ItemTemplate is null && ic.DisplayMemberBinding is null)
            ic.ItemTemplate = TextItem;
    }

    private static readonly Avalonia.Controls.Templates.FuncDataTemplate<string> TextItem =
        new((text, _) => new TextBlock { Text = Tr.TryTranslate(text ?? "", out var t) ? t : text }, supportsRecycling: false);

    private static void Translate(AvaloniaObject o, AvaloniaProperty p)
    {
        if (o.GetValue(p) is not string text || text.Length == 0) return;
        if (BindingOperations.GetBindingExpressionBase(o, p) is not null) return;
        if (Tr.TryTranslate(text, out var translated) && translated != text) o.SetCurrentValue(p, translated);
    }
}

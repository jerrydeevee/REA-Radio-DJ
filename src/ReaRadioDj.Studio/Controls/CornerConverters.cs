using Avalonia;
using Avalonia.Data.Converters;

namespace ReaRadioDj.Studio.Controls;

/// <summary>Corner radii derived from a surrounding shape, so nested shapes stay concentric with every corner style.</summary>
public static class CornerConverters
{
    /// <summary>
    /// The right-hand corners of a panel laid inside a coloured card with a 2 px ring: the card's radius minus the ring,
    /// square on the left (where the card's colour shows as a strip). The corner style in Weergave scales the card's
    /// radius, so a fixed inner radius would poke out of a rounder card.
    /// </summary>
    public static readonly IValueConverter InnerRight = new FuncValueConverter<CornerRadius, CornerRadius>(c =>
        new CornerRadius(0, Math.Max(0, c.TopRight - 2), Math.Max(0, c.BottomRight - 2), 0));
}

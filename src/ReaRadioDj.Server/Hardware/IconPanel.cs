using System.Globalization;
using ReaRadioDj.Core.Hardware;
using ReaRadioDj.Core.Studio;

namespace ReaRadioDj.Server.Hardware;

/// <summary>
/// Puts REA's set-up into an iCON V1-M over its configuration port (MIDI output 4), without iMap: the touch buttons
/// send the notes REA's inputs listen to, with the name of what they do (a cart's name) on the screen.
/// </summary>
public static class IconPanel
{
    /// <summary>Writes the set-up; returns whether it went out and what happened, in words for the studio.</summary>
    public static (bool Ok, string Text) Write(HwDevice device, IReadOnlyList<HwInput> inputs, Cartwall cartwall, bool save = true)
    {
        var commands = new Dictionary<(int, int), string>();
        foreach (var input in inputs.Where(i => i.DeviceId == device.Id && i.OnCommand.Length > 0))
            if (IconV1M.Button(input.Trigger) is { } b) commands.TryAdd(b, input.OnCommand);
        var steps = IconV1M.Write((layer, button) => commands.TryGetValue((layer, button), out var c) ? LabelOf(c, cartwall) : null, save);

        string port = IconV1M.ConfigPort(device.Port);
        using var midi = new MidiOutPort(port);
        if (!midi.Open) return (false, $"“{port}” niet gevonden of in gebruik (sluit iMap)");
        foreach (var step in steps)
        {
            if (!midi.Send(step.Message)) return (false, $"het paneel nam niet alles aan via “{port}”");
            Thread.Sleep(step.PauseMs);
        }
        return (true, $"{commands.Count} van de {IconV1M.Layers * IconV1M.ButtonsPerLayer} aanraakknoppen ingesteld" + (save ? " en opgeslagen" : ""));
    }

    /// <summary>The text for a button: a cart's name (or its title), otherwise the command itself.</summary>
    public static string LabelOf(string command, Cartwall cartwall)
    {
        var w = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (w.Length == 3 && w[0].Equals("CART", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(w[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int wall) && Cartwall.IsWall(wall - 1)
            && int.TryParse(w[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot) && slot is >= 1 and <= Cartwall.SlotsPerWall)
        {
            var cart = cartwall.Get(wall - 1)[slot - 1];
            return cart.Slot.Label.Trim() is { Length: > 0 } label ? label : cart.Media?.Title is { Length: > 0 } title ? title : "";
        }
        if (command.Trim().Equals("CARTS STOP", StringComparison.OrdinalIgnoreCase)) return "Carts stop";
        return command.Trim();
    }
}

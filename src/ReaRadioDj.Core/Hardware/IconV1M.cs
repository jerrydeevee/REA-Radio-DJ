using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ReaRadioDj.Core.Hardware;

/// <summary>A message for the configuration port of an iCON V1-M and how long to wait before the next one.</summary>
public sealed record IconStep(MackieOutput Message, int PauseMs);

/// <summary>
/// Writes REA's own set-up into an iCON V1-M, the way its editor iMap does (see docs/ICON-V1M-PROTOCOL.md): the
/// five layers of touch buttons send notes on MIDI channel 2 (blue), 3 (green), 4 (orange), 5 (purple, U1) and
/// 6 (red, U2), note 0-23, with the text REA gives them. REA's own set-up (<see cref="DefaultCommand"/>): blue, green,
/// orange and purple are page 1-4 of cartwall 1, 16 jingles each, with Carts stop and below a row of six switches that
/// is the same on every layer; red holds the functions of the broadcast. Everything else (the Mackie buttons, faders, jog wheel) stays as in the Mackie layout below,
/// taken from iMap. The panel gets the whole configuration at once, as iMap always sends it.
/// </summary>
public static class IconV1M
{
    public const int Layers = 5, ButtonsPerLayer = 24, FirstChannel = 2;
    /// <summary>The layers: blue, green, orange and purple (U1) the pages of cartwall 1, red (U2) the functions.</summary>
    public const int U1 = 3, U2 = 4;

    /// <summary>On a cart layer: the jingles (one cartwall page), then Carts stop, then the row of switches.</summary>
    public const int JinglesPerLayer = 16, CartsStopButton = 17, FirstSwitch = 18;

    /// <summary>The lower row of every cart layer (six pads): the play modes and what you switch most.</summary>
    public static readonly IReadOnlyList<(string Command, string Label)> Switches =
    [
        ("MODE AUTO", "Auto"), ("MODE SEMI", "Semi"), ("MODE HAND", "Hand"), ("MIC TOGGLE", "Mic"), ("NEXT", "Volgende"), ("SKIP", "Overslaan"),
    ];

    /// <summary>The functions on U2, in the order of the buttons: the command and the text on the button.</summary>
    public static readonly IReadOnlyList<(string Command, string Label)> Functions =
    [
        ("MODE AUTO", "Auto"), ("MODE SEMI", "Semi"), ("MODE HAND", "Hand"), ("NEXT", "Volgende"), ("SKIP", "Overslaan"), ("BREAK", "AI-break"),
        ("MIC TOGGLE", "Mic"), ("PLAYER A TOGGLE", "Speler A"), ("PLAYER B TOGGLE", "Speler B"), ("PLAYER C TOGGLE", "Speler C"),
        ("PLAYER D TOGGLE", "Speler D"), ("PLAYER PRESENTER TOGGLE", "AI-pres."),
        ("CARTS STOP", "Carts stop"), ("OVERLAY STOP", "Overlay stop"), ("PLAYER PFL STOP", "PFL stop"), ("DJ ONAIR TOGGLE", "DJ op de lucht"),
        ("DJ AUTOMIX TOGGLE", "Automix"), ("REPLAN", "Log vullen"),
        ("VOIP 1 ANSWER", "VoIP 1 opnemen"), ("VOIP 1 HANGUP", "VoIP 1 ophangen"), ("VOIP 1 ONAIR TOGGLE", "VoIP 1 zender"),
        ("VOIP 2 ANSWER", "VoIP 2 opnemen"), ("VOIP 2 HANGUP", "VoIP 2 ophangen"), ("VOIP 2 ONAIR TOGGLE", "VoIP 2 zender"),
    ];

    /// <summary>
    /// What a button does in REA's set-up: on blue, green, orange and purple the 16 jingles of page 1-4 of cartwall 1
    /// (cart 1-64), Carts stop, and the <see cref="Switches"/> on the lower row; on red the <see cref="Functions"/>.
    /// Null = a free button.
    /// </summary>
    public static string? DefaultCommand(int layer, int button)
    {
        if (button is < 0 or >= ButtonsPerLayer) return null;
        if (layer == U2) return button < Functions.Count ? Functions[button].Command : null;
        if (layer is < 0 or > U1) return null;
        if (button < JinglesPerLayer) return $"CART 1 {layer * JinglesPerLayer + button + 1}";
        if (button == CartsStopButton) return "CARTS STOP";
        return button >= FirstSwitch && button - FirstSwitch < Switches.Count ? Switches[button - FirstSwitch].Command : null;
    }

    /// <summary>The text of a function or switch ("MIC TOGGLE" → "Mic"), or null for another command.</summary>
    public static string? FunctionLabel(string command) =>
        Functions.Concat(Switches).Append((Command: "CARTS STOP", Label: "Carts stop"))
            .FirstOrDefault(f => f.Command.Equals(command.Trim(), StringComparison.OrdinalIgnoreCase)).Label;

    /// <summary>A command of the cart set-up (a cart or Carts stop): REA may move it when it lays out the layers.</summary>
    public static bool IsCartCommand(string command)
    {
        var w = command.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return w is ["CART", _, _] or ["CARTS", "STOP"];
    }
    private const int FirstTouchSlot = 66, SlotsPerPage = 28, SlotSize = 8, LabelsPerPage = 12, LabelSize = 16, LineSize = 8;
    private static readonly byte[] Header = [0xF0, 0x1D, 0x03, 0x10, 0x09];
    private const byte Slot = 0x21;   // seen in every write of iMap; probably the DAW slot that is written
    private const uint Ready = 0x7F7FEF;   // short EF 7F 7F before most steps

    /// <summary>The port the configuration goes to: the fourth MIDI output of the panel.</summary>
    public static string ConfigPort(string panelPort) => $"MIDIOUT4 ({panelPort})";

    /// <summary>The trigger a touch button sends (layer 0 = blue, button 0-23), as REA's hardware inputs write it.</summary>
    public static string Trigger(int layer, int button) => $"ch{layer + FirstChannel} note {button}";

    /// <summary>A touch button's own note sent back (MIDI channel 1-16): velocity 127 lights it, 0 makes it dark.</summary>
    public static MackieOutput PadNote(int channel, int note, bool on) =>
        new((uint)(0x90 | ((channel - 1) & 0x0F) | ((note & 0x7F) << 8) | ((on ? 0x7F : 0) << 16)));

    /// <summary>The touch button for a trigger ("ch2 note 5" = blue 6), or null when no touch button sends it.</summary>
    public static (int Layer, int Button)? Button(string trigger) =>
        HwSignals.Parse(trigger) is { Kind: "note" } a && a.Channel - FirstChannel is >= 0 and < Layers && a.Number < ButtonsPerLayer
            ? (a.Channel - FirstChannel, a.Number) : null;

    /// <summary>
    /// Everything to send: the mapping pages, the texts and, when <paramref name="save"/>, the panel's "save as user
    /// default" so the set-up stays after switching it off. <paramref name="label"/> gives the text of a button
    /// (layer, button); null or empty leaves it blank.
    /// </summary>
    public static IReadOnlyList<IconStep> Write(Func<int, int, string?> label, bool save = true, byte[]? token = null)
    {
        token ??= RandomNumberGenerator.GetBytes(3).Select(b => (byte)(b & 0x7F)).ToArray();
        var steps = new List<IconStep>();
        void Long(int pause, params byte[] body) => steps.Add(new IconStep(new MackieOutput(0, [.. Header, .. body, 0xF7]), pause));
        void Short(uint message) => steps.Add(new IconStep(new MackieOutput(message), 5));

        Long(20, [0x24, token[0], token[1], token[2], 0, 0, 0, 0, 0, 0]);
        Short(Ready);
        Long(20, 0x2C, Slot, 0x04);
        var map = Mapping();
        for (int page = 0; page < map.Length / (SlotsPerPage * SlotSize); page++)
            Long(20, [0x25, Slot, (byte)page, 0x08, .. map.AsSpan(page * SlotsPerPage * SlotSize, SlotsPerPage * SlotSize)]);
        var texts = Texts(label);
        for (int page = 0; page < texts.Length / (LabelsPerPage * LabelSize); page++)
        {
            Short(Ready);
            Long(40, [0x26, 0x01, 0x01, (byte)page, LabelsPerPage, 0x02, LineSize, .. texts.AsSpan(page * LabelsPerPage * LabelSize, LabelsPerPage * LabelSize)]);
        }
        Long(20, 0x2E, Slot, 0x00);
        Short(Ready);
        Short(0x21_22_EC);
        Long(50, [0x27, token[0], token[1], token[2], 0, 0, 0, 0, 0, 0]);
        if (save)
        {
            Short(Ready);
            Short(0x01_2D_EC);
        }
        return steps;
    }

    /// <summary>The mapping: the Mackie layout with the touch buttons on notes of their own channel.</summary>
    private static byte[] Mapping()
    {
        var map = Convert.FromHexString(string.Concat(MackiePages));
        for (int layer = 0; layer < Layers; layer++)
            for (int button = 0; button < ButtonsPerLayer; button++)
            {
                int at = (FirstTouchSlot + layer * ButtonsPerLayer + button) * SlotSize;
                byte[] note = [0x09, 0x09, (byte)(layer + FirstChannel - 1), (byte)button, 0x7F, 0, 0, 0];
                note.CopyTo(map, at);
            }
        return map;
    }

    private static byte[] Texts(Func<int, int, string?> label)
    {
        var all = new StringBuilder();
        for (int layer = 0; layer < Layers; layer++)
            for (int button = 0; button < ButtonsPerLayer; button++)
                all.Append(Label(label(layer, button)));
        return Encoding.ASCII.GetBytes(all.ToString());
    }

    /// <summary>
    /// A text as the panel shows it: two lines of eight characters, plain ASCII. A line breaks at the last space that
    /// fits; a longer word is cut. What does not fit is left out.
    /// </summary>
    public static string Label(string? text)
    {
        var ascii = new StringBuilder();
        foreach (char c in (text ?? "").Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) ascii.Append(c is >= ' ' and <= '~' ? c : ' ');
        string s = string.Join(' ', ascii.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        string first, rest;
        if (s.Length <= LineSize) (first, rest) = (s, "");
        else if (s.LastIndexOf(' ', LineSize) is > 0 and var space) (first, rest) = (s[..space], s[(space + 1)..]);
        else (first, rest) = (s[..LineSize], s[LineSize..].TrimStart());
        if (rest.Length > LineSize) rest = rest[..LineSize];
        return first.PadRight(LineSize) + rest.PadRight(LineSize);
    }

    // The eight mapping pages of 28 slots (8 bytes each) as iMap wrote them for its Mackie layout. Slot 66-185 are the
    // touch buttons (overwritten above; 138-185, U1 and U2, were keyboard shortcuts); 204 is the jog wheel (controller 60), 214-222 the faders (pitch bend).
    private static readonly string[] MackiePages =
    [
        "090900007F000000090900017F000000090900027F000000090900037F000000090900047F000000090900057F000000090900067F000000090900077F000000090900087F000000090900097F0000000909000A7F0000000909000B7F0000000909000C7F0000000909000D7F0000000909000E7F0000000909000F7F000000090900107F000000090900117F000000090900127F000000090900137F000000090900147F000000090900157F000000090900167F000000090900177F000000090900187F000000090900197F0000000909001A7F0000000909001B7F000000",
        "0909001C7F0000000909001D7F0000000909001E7F0000000909001F7F000000090900307F000000090900317F0000000909002E7F0000000909002F7F000000090900327F000000000000000000000000000000000000000909004A7F00000000000000000000000909004B7F0000000909004E7F0000000909004D7F0000000909005B7F0000000909005C7F000000090900567F0000000909005D7F0000000909005E7F0000000909005F7F00000040000050004F00005800000001000200090900637F006200090900637F006200090900617F006000090900617F006000",
        "00000000000000000909002F7F002E0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000090901017F000000090901017F0000000909002B7F0000000909002C7F0000000909002D7F000000090900557F000000090900287F000000090900337F000000090900347F000000090900357F0000000909004F7F000000090900367F0000000909005A7F000000090900377F000000090900587F000000090900597F000000090900407F000000090900417F000000",
        "090900517F000000090900627F000000090900637F000000090900607F000000090900617F000000090901187F000000090902007F0000000909003C7F000000090900507F000000090900427F000000090901367F000000090901377F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000090900647F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000",
        "000000007F000000090900467F000000090903007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F000000000000007F00000040000000000000004000001E000000004000001F00000000",
        "40000020000000004000002100000000400000220000000041000015000000004100000800000000410000070000000041000006000000004100001B0000000041000019000000004000004C000000004100000A000000004300000A000000004100001D000000004100001C0000000041000012000000004100001A00000000410000140000000041000011000000004100001600000000430000160000000040000057000000004000005600000000400000050000000041000009000000004100000400000000430000040000000000000000000000000000000000000000",
        "4100001700000000430000170000000045000017000000004500000A0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000090900207F000000090900217F000000090900227F000000090900237F000000090900247F000000090900257F000000090900267F000000090900277F000000090900677F000000090900667F000000",
        "0B0B0010014100000B0B0011014100000B0B0012014100000B0B0013014100000B0B0014014100000B0B0015014100000B0B0016014100000B0B0017014100000B0B003C41010000090900687F000000090900697F0000000909006A7F0000000909006B7F0000000909006C7F0000000909006D7F0000000909006E7F0000000909006F7F000000090900707F0000000E0E0000000000000E0E0100000000000E0E0200000000000E0E0300000000000E0E0400000000000E0E0500000000000E0E0600000000000E0E0700000000000E0E0800000000000B0B0A007F000000",
    ];
}

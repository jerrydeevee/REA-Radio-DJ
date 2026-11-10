using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Core.Hardware;

/// <summary>How a device is connected.</summary>
public enum HwKind { Midi = 0, Serial = 1, Livewire = 2, Ember = 3 }

/// <summary>
/// A piece of hardware. <see cref="Port"/> is the MIDI device name, the COM port, or the host name (with an optional
/// ":port") of a Livewire node or an Ember+ provider.
/// <see cref="Baud"/> is only for serial ports, <see cref="Password"/> only for Livewire (sent as LOGIN when set).
/// </summary>
public sealed record HwDevice(long Id, string Name, bool Enabled, HwKind Kind, string Port, int Baud = 9600, string Password = "")
{
    public HwDevice Checked() => this with
    {
        Name = string.IsNullOrWhiteSpace(Name) ? Kind switch { HwKind.Midi => "MIDI", HwKind.Serial => "Serieel", HwKind.Ember => "Ember+", _ => "Livewire" } : Name.Trim(),
        Port = Port?.Trim() ?? "",
        Baud = Baud is >= 300 and <= 921600 ? Baud : 9600,
        Password = Password ?? "",
    };
}

/// <summary>A button, fader start or contact: the trigger of a device and the commands for "on" and "off" (either may be empty).</summary>
public sealed record HwInput(long Id, long DeviceId, string Trigger, string OnCommand, string OffCommand = "");

/// <summary>A studio state that a lamp, relay or tally shows.</summary>
public enum HwState { Mic = 0, OnAir = 1, Auto = 2 }

/// <summary>
/// A tally: when <see cref="State"/> changes, the device gets the target (MIDI "note 60", Livewire "gpo 4.2") or,
/// for a serial port, the text <see cref="OnText"/> or <see cref="OffText"/> (escapes \r, \n and \xNN allowed).
/// </summary>
public sealed record HwOutput(long Id, long DeviceId, HwState State, string Target, string OnText = "", string OffText = "");

/// <summary>
/// Something a device reported: a key ("note 60", "cc 7", "gpi 3.1", or a serial line), whether it is on, and for MIDI
/// the value (velocity or controller value 0..127; -1 when there is none).
/// </summary>
public sealed record HwEvent(long DeviceId, string Key, bool On, int Channel = 0, int Value = -1);

/// <summary>A parsed trigger or target.</summary>
public sealed record HwAddress(string Kind, int Number, int Sub = 0, int Channel = 0)
{
    public override string ToString() => (Channel > 0 ? $"ch{Channel} " : "") + Kind + " " + Number + (Kind is "gpi" or "gpo" ? "." + Sub : "");
}

public static partial class HwSignals
{
    [GeneratedRegex(@"^\s*(?:ch(?<ch>\d{1,2})\s+)?(?<kind>note|cc|pc|gpi|gpo)\s+(?<n>\d{1,3})(?:\.(?<sub>[1-5]))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AddressPattern();

    /// <summary>"note 60", "ch2 cc 7", "pc 5", "gpi 3.1", "gpo 4.2"; null when the text is not an address.</summary>
    public static HwAddress? Parse(string? text)
    {
        if (text is null || AddressPattern().Match(text) is not { Success: true } m) return null;
        string kind = m.Groups["kind"].Value.ToLowerInvariant();
        int n = int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
        int ch = m.Groups["ch"].Success ? int.Parse(m.Groups["ch"].Value, CultureInfo.InvariantCulture) : 0;
        int sub = m.Groups["sub"].Success ? int.Parse(m.Groups["sub"].Value, CultureInfo.InvariantCulture) : 0;
        if (kind is "gpi" or "gpo") return n is >= 1 and <= 999 && sub >= 1 && ch == 0 ? new HwAddress(kind, n, sub) : null;
        if (sub != 0 || n > 127 || ch > 16) return null;
        return new HwAddress(kind, n, 0, ch);
    }

    /// <summary>Whether an input's trigger matches what a device reported.</summary>
    public static bool Matches(HwInput input, HwEvent e)
    {
        if (input.DeviceId != e.DeviceId) return false;
        if (EmberAddress.Parse(input.Trigger) is { } ember) return ember.Equals(EmberAddress.Parse(e.Key));
        if (Parse(input.Trigger) is { } a)
            return Parse(e.Key) is { } k && k.Kind == a.Kind && k.Number == a.Number && k.Sub == a.Sub && (a.Channel == 0 || a.Channel == e.Channel);
        return string.Equals(input.Trigger.Trim(), e.Key.Trim(), StringComparison.OrdinalIgnoreCase); // a serial line
    }

    /// <summary>
    /// Whether an event is a real change. Notes, controllers and contacts report a state: only a change from the last
    /// known state counts (a fader moving above 64, or a Livewire line that repeats all pins, is no new press).
    /// Program changes and serial lines are a press every time.
    /// </summary>
    public static bool IsChange(IDictionary<(long, string), bool> last, HwEvent e)
    {
        if (Parse(e.Key) is not { Kind: "note" or "cc" or "gpi" } && EmberAddress.Parse(e.Key) is null) return true;
        var key = (e.DeviceId, (e.Channel > 0 ? $"ch{e.Channel} " : "") + e.Key);
        if (last.TryGetValue(key, out bool was) && was == e.On) return false;
        last[key] = e.On;
        return true;
    }

    // ---------------------------------------------------------------- MIDI

    /// <summary>
    /// A MIDI short message (status in the low byte) as an event. Note on with velocity 0 counts as note off; a
    /// controller is on from 64 up; a program change is always "on". Other messages (clock, aftertouch) are ignored.
    /// </summary>
    public static HwEvent? FromMidi(long deviceId, uint message)
    {
        int status = (int)(message & 0xFF), data1 = (int)((message >> 8) & 0x7F), data2 = (int)((message >> 16) & 0x7F);
        int channel = (status & 0x0F) + 1;
        return (status & 0xF0) switch
        {
            0x90 => new HwEvent(deviceId, $"note {data1}", data2 > 0, channel, data2),
            0x80 => new HwEvent(deviceId, $"note {data1}", false, channel, 0),
            0xB0 => new HwEvent(deviceId, $"cc {data1}", data2 >= 64, channel, data2),
            0xC0 => new HwEvent(deviceId, $"pc {data1}", true, channel),
            _ => null,
        };
    }

    /// <summary>The MIDI message for a tally: note on (velocity 127) or off, or a controller at 127 or 0.</summary>
    public static uint? ToMidi(HwAddress a, bool on)
    {
        int ch = Math.Max(1, a.Channel) - 1;
        return a.Kind switch
        {
            "note" => (uint)((on ? 0x90 : 0x80) | ch | (a.Number << 8) | ((on ? 127 : 0) << 16)),
            "cc" => (uint)(0xB0 | ch | (a.Number << 8) | ((on ? 127 : 0) << 16)),
            "pc" when on => (uint)(0xC0 | ch | (a.Number << 8)),
            _ => null,
        };
    }

    // ---------------------------------------------------------------- Livewire (LWRP, TCP port 93)

    public const int LivewirePort = 93;

    [GeneratedRegex(@"^\s*GPI\s+(?<port>\d+)\s+(?<pins>[HhLl]{5})\b")]
    private static partial Regex GpiLine();

    /// <summary>
    /// A GPI line from a Livewire node ("GPI 3 hlhhh"): one event per pin; low (l) is active. Pins in capitals are
    /// still changing and are skipped, the node sends the settled state (small letters) right after.
    /// </summary>
    public static IReadOnlyList<HwEvent> FromLivewire(long deviceId, string line)
    {
        if (GpiLine().Match(line) is not { Success: true } m) return [];
        int port = int.Parse(m.Groups["port"].Value, CultureInfo.InvariantCulture);
        string pins = m.Groups["pins"].Value;
        var list = new List<HwEvent>(5);
        for (int i = 0; i < 5; i++)
            if (char.IsLower(pins[i])) list.Add(new HwEvent(deviceId, $"gpi {port}.{i + 1}", pins[i] == 'l'));
        return list;
    }

    /// <summary>The command that sets one GPO pin and leaves the others ("GPO 4 xLxxx" = pin 2 active).</summary>
    public static string ToLivewire(HwAddress a, bool on)
    {
        var pins = "xxxxx".ToCharArray();
        pins[a.Sub - 1] = on ? 'L' : 'H';
        return $"GPO {a.Number} {new string(pins)}";
    }

    // ---------------------------------------------------------------- serial

    [GeneratedRegex(@"\\x([0-9A-Fa-f]{2})|\\r|\\n|\\t|\\\\")]
    private static partial Regex Escape();

    /// <summary>Text for a serial port with \r, \n, \t, \\ and \xNN turned into their bytes.</summary>
    public static byte[] Unescape(string text)
    {
        var bytes = new List<byte>();
        int at = 0;
        foreach (Match m in Escape().Matches(text))
        {
            bytes.AddRange(Encoding.Latin1.GetBytes(text[at..m.Index]));
            bytes.Add(m.Value switch
            {
                @"\r" => (byte)'\r', @"\n" => (byte)'\n', @"\t" => (byte)'\t', @"\\" => (byte)'\\',
                _ => byte.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            });
            at = m.Index + m.Length;
        }
        bytes.AddRange(Encoding.Latin1.GetBytes(text[at..]));
        return [.. bytes];
    }

    /// <summary>Splits incoming serial data into lines; keeps an unfinished line in <paramref name="pending"/>.</summary>
    public static IReadOnlyList<string> Lines(StringBuilder pending, string data)
    {
        pending.Append(data);
        var lines = new List<string>();
        string all = pending.ToString();
        int start = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] is not ('\r' or '\n')) continue;
            if (i > start) lines.Add(all[start..i].Trim());
            start = i + 1;
        }
        pending.Clear().Append(all[start..]);
        if (pending.Length > 4096) pending.Clear(); // a device that never ends its lines
        return lines.Where(l => l.Length > 0).ToList();
    }
}

/// <summary>Devices, inputs and outputs in the station database.</summary>
public sealed class HardwareStore(StationDb db)
{
    public event Action? Changed;

    public IReadOnlyList<HwDevice> Devices() => db.Query("SELECT id, name, enabled, kind, port, baud, password FROM hw_devices ORDER BY name",
        r => new HwDevice(r.GetInt64(0), r.GetString(1), r.GetInt64(2) != 0, (HwKind)r.GetInt64(3), r.GetString(4), (int)r.GetInt64(5), r.GetString(6)));

    public IReadOnlyList<HwInput> Inputs() => db.Query("SELECT id, device_id, trigger, on_command, off_command FROM hw_inputs ORDER BY device_id, id",
        r => new HwInput(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4)));

    public IReadOnlyList<HwOutput> Outputs() => db.Query("SELECT id, device_id, state, target, on_text, off_text FROM hw_outputs ORDER BY device_id, id",
        r => new HwOutput(r.GetInt64(0), r.GetInt64(1), (HwState)r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetString(5)));

    public long Save(HwDevice device)
    {
        var d = device.Checked();
        (string, object?)[] args = [("$id", d.Id), ("$n", d.Name), ("$e", d.Enabled ? 1 : 0), ("$k", (int)d.Kind), ("$p", d.Port), ("$b", d.Baud), ("$pw", d.Password)];
        long id = d.Id > 0 && db.Execute("UPDATE hw_devices SET name=$n, enabled=$e, kind=$k, port=$p, baud=$b, password=$pw WHERE id=$id", args) > 0
            ? d.Id
            : db.Insert("INSERT INTO hw_devices(name, enabled, kind, port, baud, password) VALUES($n, $e, $k, $p, $b, $pw)", args);
        Changed?.Invoke();
        return id;
    }

    public long Save(HwInput input)
    {
        (string, object?)[] args = [("$id", input.Id), ("$d", input.DeviceId), ("$t", input.Trigger.Trim()), ("$on", input.OnCommand.Trim()), ("$off", input.OffCommand.Trim())];
        long id = input.Id > 0 && db.Execute("UPDATE hw_inputs SET device_id=$d, trigger=$t, on_command=$on, off_command=$off WHERE id=$id", args) > 0
            ? input.Id
            : db.Insert("INSERT INTO hw_inputs(device_id, trigger, on_command, off_command) VALUES($d, $t, $on, $off)", args);
        Changed?.Invoke();
        return id;
    }

    public long Save(HwOutput output)
    {
        (string, object?)[] args = [("$id", output.Id), ("$d", output.DeviceId), ("$s", (int)output.State), ("$t", output.Target.Trim()), ("$on", output.OnText), ("$off", output.OffText)];
        long id = output.Id > 0 && db.Execute("UPDATE hw_outputs SET device_id=$d, state=$s, target=$t, on_text=$on, off_text=$off WHERE id=$id", args) > 0
            ? output.Id
            : db.Insert("INSERT INTO hw_outputs(device_id, state, target, on_text, off_text) VALUES($d, $s, $t, $on, $off)", args);
        Changed?.Invoke();
        return id;
    }

    /// <summary>Removes a device with its inputs and outputs (the database does not cascade).</summary>
    public bool DeleteDevice(long id)
    {
        db.Execute("DELETE FROM hw_inputs WHERE device_id=$id", ("$id", id));
        db.Execute("DELETE FROM hw_outputs WHERE device_id=$id", ("$id", id));
        bool done = db.Execute("DELETE FROM hw_devices WHERE id=$id", ("$id", id)) > 0;
        Changed?.Invoke();
        return done;
    }

    public bool DeleteInput(long id) { bool d = db.Execute("DELETE FROM hw_inputs WHERE id=$id", ("$id", id)) > 0; Changed?.Invoke(); return d; }
    public bool DeleteOutput(long id) { bool d = db.Execute("DELETE FROM hw_outputs WHERE id=$id", ("$id", id)) > 0; Changed?.Invoke(); return d; }
}

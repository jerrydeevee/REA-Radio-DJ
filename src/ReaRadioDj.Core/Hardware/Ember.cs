using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace ReaRadioDj.Core.Hardware;

// Ember+ is Lawo's open control protocol (Boost licence), used by mixing desks, routers and audio processors.
// A message is a Glow tree encoded in ASN.1 BER, sent in S101 frames over TCP. This file holds the parts that need no
// network: BER, S101 framing, the Glow elements a consumer needs, and the "ember 1.2.3" addresses used for buttons and
// tallies.

/// <summary>One BER element: a primitive with its content bytes, or a constructed element with children.</summary>
public sealed class BerNode
{
    public int Class { get; init; }
    public bool Constructed { get; init; }
    public int Tag { get; init; }
    public byte[] Content { get; init; } = [];
    public List<BerNode> Children { get; } = [];

    public bool Is(int cls, int tag) => Class == cls && Tag == tag;

    /// <summary>The element inside an explicit context tag [n].</summary>
    public BerNode? Field(int contextTag) =>
        Children.FirstOrDefault(c => c.Class == Ber.Context && c.Tag == contextTag)?.Children.FirstOrDefault();
}

/// <summary>The BER encoding and decoding Ember+ uses (definite lengths out, definite and indefinite lengths in).</summary>
public static class Ber
{
    public const int Universal = 0, Application = 1, Context = 2;
    public const int TagBoolean = 1, TagInteger = 2, TagOctets = 4, TagNull = 5, TagReal = 9, TagUtf8 = 12, TagRelativeOid = 13,
        TagSequence = 16, TagSet = 17;

    // ---------------------------------------------------------------- writing

    public static byte[] Tlv(int cls, int tag, bool constructed, ReadOnlySpan<byte> content)
    {
        var o = new List<byte>(content.Length + 8);
        int first = (cls << 6) | (constructed ? 0x20 : 0);
        if (tag < 31) o.Add((byte)(first | tag));
        else
        {
            o.Add((byte)(first | 0x1F));
            o.AddRange(Base128(tag));
        }
        if (content.Length < 128) o.Add((byte)content.Length);
        else
        {
            var len = Unsigned(content.Length);
            o.Add((byte)(0x80 | len.Length));
            o.AddRange(len);
        }
        o.AddRange(content.ToArray());
        return [.. o];
    }

    private static byte[] Join(byte[][] parts) => parts.SelectMany(p => p).ToArray();

    public static byte[] App(int tag, params byte[][] parts) => Tlv(Application, tag, true, Join(parts));
    /// <summary>An explicit context tag [n] around one element.</summary>
    public static byte[] Ctx(int tag, byte[] inner) => Tlv(Context, tag, true, inner);
    public static byte[] Set(params byte[][] parts) => Tlv(Universal, TagSet, true, Join(parts));
    public static byte[] Integer(long v) => Tlv(Universal, TagInteger, false, Signed(v));
    public static byte[] Boolean(bool v) => Tlv(Universal, TagBoolean, false, [v ? (byte)0xFF : (byte)0]);
    public static byte[] Utf8(string s) => Tlv(Universal, TagUtf8, false, Encoding.UTF8.GetBytes(s));
    public static byte[] Real(double v) => Tlv(Universal, TagReal, false, RealBytes(v));
    public static byte[] Octets(byte[] v) => Tlv(Universal, TagOctets, false, v);
    public static byte[] RelativeOid(IReadOnlyList<int> path) => Tlv(Universal, TagRelativeOid, false, path.SelectMany(n => Base128(n)).ToArray());

    /// <summary>The value as an element of its own universal type (bool, whole number, real, text, bytes).</summary>
    public static byte[] Value(object? v) => v switch
    {
        bool b => Boolean(b),
        long or int or short or byte => Integer(Convert.ToInt64(v, CultureInfo.InvariantCulture)),
        double or float or decimal => Real(Convert.ToDouble(v, CultureInfo.InvariantCulture)),
        string s => Utf8(s),
        byte[] bytes => Octets(bytes),
        _ => Tlv(Universal, TagNull, false, []),
    };

    private static byte[] Base128(int n)
    {
        var b = new List<byte> { (byte)(n & 0x7F) };
        for (n >>= 7; n > 0; n >>= 7) b.Insert(0, (byte)(0x80 | (n & 0x7F)));
        return [.. b];
    }

    private static byte[] Unsigned(long v)
    {
        var b = new List<byte>();
        do { b.Insert(0, (byte)(v & 0xFF)); v >>= 8; } while (v > 0);
        return [.. b];
    }

    /// <summary>Shortest two's complement, most significant byte first.</summary>
    public static byte[] Signed(long v)
    {
        var b = new byte[8];
        for (int i = 7; i >= 0; i--, v >>= 8) b[i] = (byte)v;
        int start = 0;
        while (start < 7 && ((b[start] == 0 && (b[start + 1] & 0x80) == 0) || (b[start] == 0xFF && (b[start + 1] & 0x80) != 0))) start++;
        return b[start..];
    }

    /// <summary>
    /// A REAL the way Ember+ writes it: base 2, the exponent of the leading bit, and the significant bits as an odd
    /// whole number (1.5 is mantissa 3, exponent 0). Infinity, minus infinity, NaN and minus zero have their own byte.
    /// </summary>
    public static byte[] RealBytes(double v)
    {
        if (double.IsNaN(v)) return [0x42];
        if (double.IsPositiveInfinity(v)) return [0x40];
        if (double.IsNegativeInfinity(v)) return [0x41];
        if (v == 0) return double.IsNegative(v) ? [0x43] : [];
        int exponent = Math.ILogB(v);
        ulong mantissa = (ulong)Math.ScaleB(Math.Abs(v), 52 - exponent);
        while ((mantissa & 1) == 0) mantissa >>= 1;
        var e = Signed(exponent);
        var m = Unsigned((long)mantissa);
        return [(byte)(0x80 | (v < 0 ? 0x40 : 0) | (e.Length - 1)), .. e, .. m];
    }

    public static double ReadReal(ReadOnlySpan<byte> c)
    {
        if (c.Length == 0) return 0;
        byte first = c[0];
        switch (first)
        {
            case 0x40: return double.PositiveInfinity;
            case 0x41: return double.NegativeInfinity;
            case 0x42: return double.NaN;
            case 0x43: return -0.0;
        }
        if ((first & 0x80) == 0) // decimal (ISO 6093) text
            return double.Parse(Encoding.ASCII.GetString(c[1..]).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
        int at = 1, expLength = (first & 3) + 1;
        if (expLength == 4) expLength = c[at++];
        long exponent = (sbyte)c[at];
        for (int i = 1; i < expLength; i++) exponent = (exponent << 8) | c[at + i];
        at += expLength;
        ulong mantissa = 0;
        foreach (byte b in c[at..]) mantissa = (mantissa << 8) | b;
        if (mantissa == 0) return 0;
        int top = 63 - BitOperations.LeadingZeroCount(mantissa);
        double value = Math.ScaleB(mantissa, (int)exponent - top + ((first >> 2) & 3));
        return (first & 0x40) != 0 ? -value : value;
    }

    public static long ReadInteger(ReadOnlySpan<byte> c)
    {
        if (c.Length == 0) return 0;
        long v = (sbyte)c[0];
        for (int i = 1; i < c.Length; i++) v = (v << 8) | c[i];
        return v;
    }

    public static int[] ReadRelativeOid(ReadOnlySpan<byte> c)
    {
        var path = new List<int>();
        int n = 0;
        foreach (byte b in c)
        {
            n = (n << 7) | (b & 0x7F);
            if ((b & 0x80) == 0) { path.Add(n); n = 0; }
        }
        return [.. path];
    }

    /// <summary>A primitive universal element as a value: bool, long, double, string, byte[], or null.</summary>
    public static object? ReadValue(BerNode? n) => n is not { Class: Universal, Constructed: false } ? null : n.Tag switch
    {
        TagBoolean => n.Content.Length > 0 && n.Content[0] != 0,
        TagInteger => ReadInteger(n.Content),
        TagReal => ReadReal(n.Content),
        TagUtf8 => Encoding.UTF8.GetString(n.Content),
        TagOctets => n.Content,
        _ => null,
    };

    // ---------------------------------------------------------------- reading

    /// <summary>All elements in the data. Throws <see cref="FormatException"/> on broken input.</summary>
    public static List<BerNode> Read(ReadOnlySpan<byte> data)
    {
        var list = new List<BerNode>();
        int pos = 0;
        while (pos < data.Length) list.Add(ReadOne(data, ref pos, 0));
        return list;
    }

    private static byte At(ReadOnlySpan<byte> d, int pos) => pos < d.Length ? d[pos] : throw new FormatException("BER: data ends too soon");

    private static BerNode ReadOne(ReadOnlySpan<byte> d, ref int pos, int depth)
    {
        if (depth > 64) throw new FormatException("BER: nested too deep");
        byte b = At(d, pos++);
        int cls = b >> 6, tag = b & 0x1F;
        bool constructed = (b & 0x20) != 0;
        if (tag == 0x1F)
        {
            tag = 0;
            byte x;
            do { x = At(d, pos++); tag = (tag << 7) | (x & 0x7F); } while ((x & 0x80) != 0 && tag < 1 << 24);
        }
        byte l = At(d, pos++);
        var node = new BerNode { Class = cls, Constructed = constructed, Tag = tag, Content = [] };
        if (l == 0x80) // indefinite: children up to two zero bytes
        {
            if (!constructed) throw new FormatException("BER: indefinite length on a primitive");
            while (!(At(d, pos) == 0 && At(d, pos + 1) == 0)) node.Children.Add(ReadOne(d, ref pos, depth + 1));
            pos += 2;
            return node;
        }
        int length = l;
        if ((l & 0x80) != 0)
        {
            int count = l & 0x7F;
            if (count > 4) throw new FormatException("BER: length too long");
            length = 0;
            for (int i = 0; i < count; i++) length = (length << 8) | At(d, pos++);
        }
        if (length < 0 || pos + length > d.Length) throw new FormatException("BER: length beyond the data");
        var content = d.Slice(pos, length);
        pos += length;
        if (!constructed) return new BerNode { Class = cls, Constructed = false, Tag = tag, Content = content.ToArray() };
        int inner = 0;
        while (inner < content.Length) node.Children.Add(ReadOne(content, ref inner, depth + 1));
        return node;
    }
}

/// <summary>A complete S101 message: Ember data (the Glow payload) or a keep-alive.</summary>
public sealed record S101Message(byte Command, byte[] Payload)
{
    public const byte Ember = 0x00, KeepAliveRequest = 0x01, KeepAliveResponse = 0x02;
}

/// <summary>S101 framing: BOF, escaped content, CRC-16 (CCITT, reflected, inverted, low byte first), EOF.</summary>
public static class S101
{
    public const byte Bof = 0xFE, Eof = 0xFF, Ce = 0xFD;
    private const byte MessageEmber = 0x0E, Version = 0x01, FlagFirst = 0x80, FlagLast = 0x40, FlagEmpty = 0x20, DtdGlow = 0x01;

    private static readonly ushort[] Table = Enumerable.Range(0, 256).Select(i =>
    {
        uint c = (uint)i;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ 0x8408 : c >> 1;
        return (ushort)c;
    }).ToArray();

    private static ushort Register(ushort crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data) crc = (ushort)((crc >> 8) ^ Table[(crc ^ b) & 0xFF]);
        return crc;
    }

    /// <summary>The CRC as it goes on the wire (start 0xFFFF, inverted at the end).</summary>
    public static ushort Crc(ReadOnlySpan<byte> data) => (ushort)~Register(0xFFFF, data);

    /// <summary>Over content plus its CRC, the register ends at this value when nothing was damaged.</summary>
    public const ushort GoodResidue = 0xF0B8;

    public static byte[] Frame(ReadOnlySpan<byte> content)
    {
        ushort crc = Crc(content);
        var o = new List<byte>(content.Length + 8) { Bof };
        void Put(byte b) { if (b >= 0xF8) { o.Add(Ce); o.Add((byte)(b ^ 0x20)); } else o.Add(b); }
        foreach (byte b in content) Put(b);
        Put((byte)(crc & 0xFF));
        Put((byte)(crc >> 8));
        o.Add(Eof);
        return [.. o];
    }

    /// <summary>Glow data in one packet (Glow DTD 2.31 announced in the application bytes).</summary>
    public static byte[] EmberPacket(ReadOnlySpan<byte> glow, byte flags = FlagFirst | FlagLast) =>
        Frame([0x00, MessageEmber, S101Message.Ember, Version, flags, DtdGlow, 0x02, 0x1F, 0x02, .. glow]);

    public static byte[] FirstPacket(ReadOnlySpan<byte> glow) => EmberPacket(glow, FlagFirst);
    public static byte[] LastPacket(ReadOnlySpan<byte> glow) => EmberPacket(glow, FlagLast);

    public static readonly byte[] KeepAliveRequest = Frame([0x00, MessageEmber, S101Message.KeepAliveRequest, Version]);
    public static readonly byte[] KeepAliveResponse = Frame([0x00, MessageEmber, S101Message.KeepAliveResponse, Version]);

    /// <summary>Reads frames from a byte stream and puts packets of one message back together.</summary>
    public sealed class Reader
    {
        private readonly List<byte> _frame = [];
        private readonly List<byte> _message = [];
        private bool _inFrame, _escape, _collecting;

        public int Damaged { get; private set; }

        public List<S101Message> Feed(ReadOnlySpan<byte> data)
        {
            var done = new List<S101Message>();
            foreach (byte b in data)
            {
                if (b == Bof) { _frame.Clear(); _inFrame = true; _escape = false; }
                else if (b == Eof)
                {
                    if (_inFrame && Take() is { } m) done.Add(m);
                    _inFrame = false;
                }
                else if (!_inFrame) { }
                else if (b == Ce) _escape = true;
                else
                {
                    _frame.Add(_escape ? (byte)(b ^ 0x20) : b);
                    _escape = false;
                    if (_frame.Count > 1 << 20) { _frame.Clear(); _inFrame = false; Damaged++; }
                }
            }
            return done;
        }

        private S101Message? Take()
        {
            var f = _frame.ToArray();
            if (f.Length < 6 || Register(0xFFFF, f) != GoodResidue) { Damaged++; return null; }
            var c = f.AsSpan(0, f.Length - 2);
            if (c[1] != MessageEmber) return null;
            byte command = c[2];
            if (command != S101Message.Ember) return new S101Message(command, []);
            if (c.Length < 7) { Damaged++; return null; }
            byte flags = c[4];
            int start = 7 + c[6];
            if (start > c.Length) { Damaged++; return null; }
            if ((flags & FlagFirst) != 0) { _message.Clear(); _collecting = true; }
            if (!_collecting) return null;
            if ((flags & FlagEmpty) == 0) _message.AddRange(c[start..].ToArray());
            if ((flags & FlagLast) == 0) return null;
            _collecting = false;
            return new S101Message(S101Message.Ember, [.. _message]);
        }
    }
}

/// <summary>Parameter types of Ember+.</summary>
public enum EmberType { None = 0, Integer = 1, Real = 2, String = 3, Boolean = 4, Trigger = 5, Enum = 6, Octets = 7 }

/// <summary>
/// A node or parameter as a provider described it. A later message often carries only some fields (just the new
/// value): <see cref="Merge"/> keeps what was known.
/// </summary>
public sealed record EmberElement(string Path, string Kind, string? Identifier = null, string? Description = null,
    bool HasValue = false, object? Value = null, EmberType? Type = null, int? Access = null, string? Enumeration = null)
{
    public bool IsParameter => Kind == "parameter";
    public bool Writable => Access is 2 or 3;
    public int[] Numbers => EmberAddress.ParsePath(Path) ?? [];

    public EmberElement Merge(EmberElement? old) => old is null ? this : new EmberElement(Path, Kind,
        Identifier ?? old.Identifier, Description ?? old.Description, HasValue || old.HasValue, HasValue ? Value : old.Value,
        Type ?? old.Type, Access ?? old.Access, Enumeration ?? old.Enumeration);

    /// <summary>The type, or what the value shows when the provider left it out.</summary>
    public EmberType EffectiveType => Type ?? Value switch
    {
        bool => EmberType.Boolean, long => EmberType.Integer, double => EmberType.Real, string => EmberType.String,
        byte[] => EmberType.Octets, _ => EmberType.None,
    };

    public string ValueText => Value switch
    {
        null => "",
        bool b => b ? "aan" : "uit",
        double d => d.ToString("0.###", CultureInfo.InvariantCulture),
        long l when EffectiveType == EmberType.Enum && Enumeration?.Split('\n') is { } names && l >= 0 && l < names.Length => names[l],
        byte[] bytes => Convert.ToHexString(bytes),
        _ => Convert.ToString(Value, CultureInfo.InvariantCulture) ?? "",
    };
}

/// <summary>The Glow messages a consumer sends, and reading what a provider sends.</summary>
public static class Glow
{
    private const int Root = 0, Parameter = 1, Command = 2, Node = 3, ElementCollection = 4, QualifiedParameter = 9, QualifiedNode = 10,
        RootElementCollection = 11, Matrix = 13, QualifiedMatrix = 17, Function = 19, QualifiedFunction = 20;
    private const int GetDirectoryCommand = 32;

    private static byte[] InRoot(byte[] element) => Ber.App(Root, Ber.App(RootElementCollection, Ber.Ctx(0, element)));

    /// <summary>Asks for the children of a node (the root when the path is empty).</summary>
    public static byte[] GetDirectory(IReadOnlyList<int> path)
    {
        var command = Ber.App(Command, Ber.Ctx(0, Ber.Integer(GetDirectoryCommand)));
        if (path.Count == 0) return InRoot(command);
        return InRoot(Ber.App(QualifiedNode, Ber.Ctx(0, Ber.RelativeOid(path)), Ber.Ctx(2, Ber.App(ElementCollection, Ber.Ctx(0, command)))));
    }

    /// <summary>Sets a parameter's value.</summary>
    public static byte[] SetValue(IReadOnlyList<int> path, object? value) =>
        InRoot(Ber.App(QualifiedParameter, Ber.Ctx(0, Ber.RelativeOid(path)), Ber.Ctx(1, Ber.Set(Ber.Ctx(2, Ber.Value(value))))));

    /// <summary>Every node and parameter in a provider message, with full paths. Streams and results are skipped.</summary>
    public static List<EmberElement> Parse(ReadOnlySpan<byte> payload)
    {
        var found = new List<EmberElement>();
        foreach (var root in Ber.Read(payload).Where(n => n.Is(Ber.Application, Root)))
            foreach (var collection in root.Children.Where(n => n.Is(Ber.Application, RootElementCollection)))
                foreach (var item in collection.Children.Where(n => n.Is(Ber.Context, 0)))
                    if (item.Children.FirstOrDefault() is { } element) Walk(element, [], found);
        return found;
    }

    private static void Walk(BerNode e, int[] parent, List<EmberElement> found)
    {
        if (e.Class != Ber.Application) return;
        string? kind = e.Tag switch
        {
            Parameter or QualifiedParameter => "parameter",
            Node or QualifiedNode => "node",
            Matrix or QualifiedMatrix => "matrix",
            Function or QualifiedFunction => "function",
            _ => null,
        };
        if (kind is null) return;
        bool qualified = e.Tag is QualifiedParameter or QualifiedNode or QualifiedMatrix or QualifiedFunction;
        int[] path;
        if (qualified) path = e.Field(0) is { Tag: Ber.TagRelativeOid } oid ? Ber.ReadRelativeOid(oid.Content) : [];
        else path = e.Field(0) is { Tag: Ber.TagInteger } number ? [.. parent, (int)Ber.ReadInteger(number.Content)] : [];
        if (path.Length == 0) return;

        var contents = e.Field(1);
        string? Text(int tag) => contents?.Field(tag) is { Tag: Ber.TagUtf8 } t ? Encoding.UTF8.GetString(t.Content) : null;
        int? Number(int tag) => contents?.Field(tag) is { Tag: Ber.TagInteger } t ? (int)Ber.ReadInteger(t.Content) : null;
        var element = kind == "parameter"
            ? new EmberElement(string.Join('.', path), kind, Text(0), Text(1),
                contents?.Children.Any(c => c.Is(Ber.Context, 2)) == true, Ber.ReadValue(contents?.Field(2)),
                Number(13) is int type ? (EmberType)type : null, Number(5), Text(7))
            : new EmberElement(string.Join('.', path), kind, Text(0), Text(1));
        found.Add(element);

        if (e.Field(2) is { } children && children.Is(Ber.Application, ElementCollection))
            foreach (var item in children.Children.Where(n => n.Is(Ber.Context, 0)))
                if (item.Children.FirstOrDefault() is { } child) Walk(child, path, found);
    }
}

/// <summary>
/// A button or tally on an Ember+ parameter: "ember 1.2.3" (on when true or not zero) or with a limit, e.g.
/// "ember 1.3.5 > -40" for a fader that counts as open above -40 dB. Operators: &gt;, &gt;=, &lt;, &lt;=, =, !=.
/// </summary>
public sealed partial record EmberAddress(int[] Path, string Op = "", double Limit = 0)
{
    [GeneratedRegex(@"^\s*ember\s+(?<path>\d+(?:\.\d+)*)\s*(?:(?<op>>=|<=|!=|>|<|=)\s*(?<limit>-?\d+(?:[.,]\d+)?))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    public string PathText => string.Join('.', Path);

    public static EmberAddress? Parse(string? text)
    {
        if (text is null || Pattern().Match(text) is not { Success: true } m || ParsePath(m.Groups["path"].Value) is not { } path) return null;
        if (!m.Groups["op"].Success) return new EmberAddress(path);
        double limit = double.Parse(m.Groups["limit"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
        return new EmberAddress(path, m.Groups["op"].Value, limit);
    }

    public static int[]? ParsePath(string text)
    {
        if (text.Length == 0) return [];
        var parts = text.Split('.');
        var path = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out path[i])) return null;
        return path;
    }

    public override string ToString() => "ember " + PathText + (Op.Length > 0 ? $" {Op} {Limit.ToString(CultureInfo.InvariantCulture)}" : "");

    public bool Equals(EmberAddress? other) => other is not null && ToString() == other.ToString();
    public override int GetHashCode() => ToString().GetHashCode(StringComparison.Ordinal);

    /// <summary>Whether a parameter value means "on"; null when the value says nothing (bytes, nothing yet).</summary>
    public bool? IsOn(object? value)
    {
        double? x = value switch
        {
            bool b => b ? 1 : 0,
            long l => l,
            double d => d,
            string s when Op.Length == 0 => s.Length > 0 && s != "0" ? 1 : 0,
            string s => double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null,
            _ => null,
        };
        if (x is not double n || double.IsNaN(n)) return null;
        return Op switch
        {
            ">" => n > Limit, ">=" => n >= Limit, "<" => n < Limit, "<=" => n <= Limit,
            "=" => Math.Abs(n - Limit) < 1e-9, "!=" => Math.Abs(n - Limit) >= 1e-9,
            _ => n != 0,
        };
    }

    /// <summary>
    /// The value to set for a tally: the given text read as the parameter's type, or without text true/false, 1/0.
    /// A trigger parameter only fires on "on" (null = send nothing).
    /// </summary>
    public static object? ValueFor(EmberType type, bool on, string text)
    {
        text = text.Trim();
        if (type == EmberType.Trigger && !on && text.Length == 0) return null;
        if (text.Length == 0)
            return type switch
            {
                EmberType.Integer or EmberType.Enum or EmberType.Trigger => on ? 1L : 0L,
                EmberType.Real => on ? 1.0 : 0.0,
                EmberType.String => on ? "1" : "0",
                _ => on,
            };
        return type switch
        {
            EmberType.Integer or EmberType.Enum or EmberType.Trigger =>
                long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l) ? l : on ? 1L : 0L,
            EmberType.Real => double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : on ? 1.0 : 0.0,
            EmberType.String => text,
            _ => text.ToLowerInvariant() is "1" or "true" or "aan" or "on" or "ja",
        };
    }
}

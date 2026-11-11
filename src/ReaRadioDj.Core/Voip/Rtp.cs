using System.Buffers.Binary;

namespace ReaRadioDj.Core.Voip;

/// <summary>An RTP packet (RFC 3550): version 2, no padding or extension of our own; CSRCs and extensions are skipped on read.</summary>
public readonly record struct RtpPacket(int PayloadType, bool Marker, ushort Sequence, uint Timestamp, uint Ssrc, ReadOnlyMemory<byte> Payload)
{
    public const int HeaderSize = 12;

    /// <summary>Writes the packet into the buffer and returns its length.</summary>
    public int Write(Span<byte> buffer)
    {
        if (buffer.Length < HeaderSize + Payload.Length) throw new ArgumentException("buffer too small");
        buffer[0] = 0x80; // V=2, P=0, X=0, CC=0
        buffer[1] = (byte)((Marker ? 0x80 : 0) | (PayloadType & 0x7F));
        BinaryPrimitives.WriteUInt16BigEndian(buffer[2..], Sequence);
        BinaryPrimitives.WriteUInt32BigEndian(buffer[4..], Timestamp);
        BinaryPrimitives.WriteUInt32BigEndian(buffer[8..], Ssrc);
        Payload.Span.CopyTo(buffer[HeaderSize..]);
        return HeaderSize + Payload.Length;
    }

    /// <summary>Reads a packet; null when it is not RTP version 2 or is cut short.</summary>
    public static RtpPacket? Read(ReadOnlyMemory<byte> data)
    {
        var s = data.Span;
        if (s.Length < HeaderSize || (s[0] >> 6) != 2) return null;
        int cc = s[0] & 0x0F;
        bool padding = (s[0] & 0x20) != 0, extension = (s[0] & 0x10) != 0;
        int offset = HeaderSize + cc * 4;
        if (s.Length < offset) return null;
        if (extension)
        {
            if (s.Length < offset + 4) return null;
            offset += 4 + BinaryPrimitives.ReadUInt16BigEndian(s[(offset + 2)..]) * 4;
            if (s.Length < offset) return null;
        }
        int end = s.Length;
        if (padding)
        {
            end -= s[^1];
            if (end < offset) return null;
        }
        return new RtpPacket(s[1] & 0x7F, (s[1] & 0x80) != 0, BinaryPrimitives.ReadUInt16BigEndian(s[2..]),
            BinaryPrimitives.ReadUInt32BigEndian(s[4..]), BinaryPrimitives.ReadUInt32BigEndian(s[8..]), data[offset..end]);
    }
}

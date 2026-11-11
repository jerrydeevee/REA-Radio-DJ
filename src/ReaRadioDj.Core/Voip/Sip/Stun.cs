using System.Buffers.Binary;
using System.Net;

namespace ReaRadioDj.Core.Voip.Sip;

/// <summary>
/// The smallest part of STUN (RFC 5389) a phone needs: ask a STUN server from which public address and port our
/// packets arrive, so that address can go into Via, Contact and SDP behind NAT.
/// </summary>
public static class Stun
{
    public const uint MagicCookie = 0x2112A442;

    /// <summary>A Binding Request with the given 12-byte transaction id.</summary>
    public static byte[] BindingRequest(ReadOnlySpan<byte> transaction)
    {
        var b = new byte[20];
        BinaryPrimitives.WriteUInt16BigEndian(b, 0x0001);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(2), 0);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4), MagicCookie);
        transaction[..12].CopyTo(b.AsSpan(8));
        return b;
    }

    public static bool IsStun(ReadOnlySpan<byte> data) =>
        data.Length >= 20 && (data[0] & 0xC0) == 0 && BinaryPrimitives.ReadUInt32BigEndian(data[4..]) == MagicCookie;

    /// <summary>The mapped address in a Binding Success Response (XOR-MAPPED-ADDRESS, else MAPPED-ADDRESS).</summary>
    public static bool TryMapped(ReadOnlySpan<byte> data, out IPEndPoint? mapped, out byte[] transaction)
    {
        mapped = null;
        transaction = [];
        if (!IsStun(data) || BinaryPrimitives.ReadUInt16BigEndian(data) != 0x0101) return false;
        transaction = data.Slice(8, 12).ToArray();
        int length = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
        var attrs = data.Slice(20, Math.Min(length, data.Length - 20));
        IPEndPoint? plain = null;
        while (attrs.Length >= 4)
        {
            int type = BinaryPrimitives.ReadUInt16BigEndian(attrs);
            int len = BinaryPrimitives.ReadUInt16BigEndian(attrs[2..]);
            if (attrs.Length < 4 + len) break;
            var v = attrs.Slice(4, len);
            if (len >= 8 && v[1] == 0x01) // IPv4
            {
                int port = BinaryPrimitives.ReadUInt16BigEndian(v[2..]);
                var ip = v.Slice(4, 4).ToArray();
                if (type == 0x0020)
                {
                    port ^= (int)(MagicCookie >> 16);
                    Span<byte> cookie = stackalloc byte[4];
                    BinaryPrimitives.WriteUInt32BigEndian(cookie, MagicCookie);
                    for (int i = 0; i < 4; i++) ip[i] ^= cookie[i];
                    mapped = new IPEndPoint(new IPAddress(ip), port);
                    return true;
                }
                if (type == 0x0001) plain = new IPEndPoint(new IPAddress(ip), port);
            }
            int step = 4 + ((len + 3) & ~3);
            if (step > attrs.Length) break;
            attrs = attrs[step..];
        }
        mapped = plain;
        return plain is not null;
    }

    /// <summary>A Binding Success Response (for tests and a fake server).</summary>
    public static byte[] BindingResponse(ReadOnlySpan<byte> transaction, IPEndPoint mapped)
    {
        var b = new byte[32];
        BinaryPrimitives.WriteUInt16BigEndian(b, 0x0101);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(2), 12);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4), MagicCookie);
        transaction[..12].CopyTo(b.AsSpan(8));
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(20), 0x0020);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(22), 8);
        b[25] = 0x01;
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(26), (ushort)(mapped.Port ^ (int)(MagicCookie >> 16)));
        var ip = mapped.Address.GetAddressBytes();
        Span<byte> cookie = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(cookie, MagicCookie);
        for (int i = 0; i < 4; i++) b[28 + i] = (byte)(ip[i] ^ cookie[i]);
        return b;
    }
}

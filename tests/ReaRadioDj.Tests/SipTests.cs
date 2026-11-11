using System.Net;
using System.Net.Sockets;
using System.Text;
using ReaRadioDj.Core.Voip.Sip;
using Xunit;

namespace ReaRadioDj.Tests;

public class SipTests
{
    private static bool Wait(Func<bool> condition, int ms = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { if (condition()) return true; Thread.Sleep(20); }
        return condition();
    }

    [Fact]
    public void Message_round_trips_and_reads_compact_headers_and_lists()
    {
        var raw = "INVITE sip:0612@voip.example SIP/2.0\r\n" +
                  "v: SIP/2.0/UDP 10.0.0.2:5060;branch=z9hG4bKabc;rport, SIP/2.0/UDP 10.0.0.9;branch=z9hG4bKdef\r\n" +
                  "f: \"Studio, 1\" <sip:studio@voip.example>;tag=11\r\n" +
                  "t: <sip:0612@voip.example>\r\n" +
                  "i: call-1@x\r\n" +
                  "CSeq: 7 INVITE\r\n" +
                  "Subject: a long\r\n  folded line\r\n" +
                  "l: 5\r\n\r\nhelloEXTRA";
        var m = SipMessage.Parse(Encoding.UTF8.GetBytes(raw))!;
        Assert.Equal("INVITE", m.Method);
        Assert.Equal(2, m.All("Via").Count());
        Assert.Equal("11", SipMessage.Param(m["From"], "tag"));
        Assert.Equal("sip:studio@voip.example", SipMessage.UriOf(m["From"]));
        Assert.Equal((7, "INVITE"), m.CSeq);
        Assert.Equal("a long folded line", m["Subject"]);
        Assert.Equal("hello", m.Body);
        Assert.Equal("", SipMessage.Param(m.All("Via").First(), "rport"));
        Assert.Equal(("voip.example", 5060), SipMessage.HostPort("sip:0612@voip.example"));
        Assert.Equal(("1.2.3.4", 5070), SipMessage.HostPort("sip:1.2.3.4:5070;transport=udp"));
        Assert.Equal("0612", SipMessage.UserOf("<sip:0612@voip.example>"));

        var again = SipMessage.Parse(m.ToBytes())!;
        Assert.Equal(m.CallId, again.CallId);
        Assert.Equal("hello", again.Body);
        Assert.Null(SipMessage.Parse("\r\n\r\n"u8));
    }

    [Fact]
    public void Digest_matches_the_rfc_2617_example()
    {
        var c = DigestChallenge.Parse("Digest realm=\"testrealm@host.com\", qop=\"auth,auth-int\", nonce=\"dcd98b7102dd2f0e8b11d0f600bfb0c093\", opaque=\"5ccc069c403ebaf9f0171e9517f40e41\"", proxy: false)!;
        var answer = c.Answer("Mufasa", "Circle Of Life", "GET", "/dir/index.html", 1, "0a4f113b");
        Assert.Contains("response=\"6629fae49393a05397450978507c4ef1\"", answer);
        Assert.Contains("qop=auth", answer);
        Assert.Contains("opaque=\"5ccc069c403ebaf9f0171e9517f40e41\"", answer);
        Assert.Equal("Authorization", c.HeaderName);
        Assert.Null(DigestChallenge.Parse("Basic realm=x", false));
        Assert.Null(DigestChallenge.Parse("Digest realm=\"x\", nonce=\"y\", algorithm=SHA-256", false));
    }

    [Fact]
    public void Sdp_offer_is_read_back_and_the_first_shared_codec_wins()
    {
        var offer = SdpAudio.Build("192.0.2.10", 40000, [Core.Voip.RtpCodecs.Create("pcmu"), Core.Voip.RtpCodecs.Create("pcma")], 42);
        var sdp = SdpAudio.Parse(offer)!;
        Assert.Equal("192.0.2.10", sdp.Address);
        Assert.Equal(40000, sdp.Port);
        Assert.Equal(["pcma", "pcmu"], [sdp.Choose(["pcma", "pcmu"])!.Value.Codec.Name, sdp.Choose(["pcmu"])!.Value.Codec.Name]);
        Assert.Null(sdp.Choose(["l16"]));

        // a far end that numbers L16 dynamically and leaves out the rtpmap of PCMA
        var other = SdpAudio.Parse("v=0\r\nc=IN IP4 198.51.100.7\r\nm=audio 7078 RTP/AVP 112 8 101\r\na=rtpmap:112 L16/48000\r\na=rtpmap:101 telephone-event/8000\r\n")!;
        Assert.Equal((Core.Voip.RtpCodecs.Create("l16").Name, 112), (other.Choose(["l16"])!.Value.Codec.Name, other.Choose(["l16"])!.Value.PayloadType));
        Assert.Equal(8, other.Choose(["pcma"])!.Value.PayloadType);
        Assert.Equal("198.51.100.7", other.Address);
    }

    [Fact]
    public void Stun_response_gives_the_mapped_address()
    {
        var t = new byte[12];
        Random.Shared.NextBytes(t);
        var request = Stun.BindingRequest(t);
        Assert.True(Stun.IsStun(request));
        var mapped = new IPEndPoint(IPAddress.Parse("203.0.113.45"), 61234);
        Assert.True(Stun.TryMapped(Stun.BindingResponse(t, mapped), out var got, out var tid));
        Assert.Equal(mapped, got);
        Assert.Equal(t, tid);
        Assert.False(Stun.TryMapped(request, out _, out _));
    }

    private static SipAgent Agent(string user, int rtp1, int rtp2, string codecs = "pcma,pcmu", string registrar = "") =>
        new(new SipAccount { Enabled = true, User = user, LocalPort = 0, Register = registrar.Length > 0, Registrar = registrar, Codecs = codecs.Split(','),
            Password = registrar.Length > 0 ? "test-wachtwoord" : "" },
            new Dictionary<string, int> { ["voip1"] = rtp1, ["voip2"] = rtp2 }) { TransactionTimeoutMs = 1000 };

    private const int TestRing = 1500;

    [Fact]
    public void Two_agents_call_answer_and_hang_up()
    {
        using var a = Agent("studio", 41000, 41002);
        using var b = Agent("verslag", 42000, 42002, codecs: "pcmu,pcma");
        var mediaA = new List<SipMedia>();
        var mediaB = new List<SipMedia>();
        var stops = new List<string>();
        a.MediaStart += m => { lock (mediaA) mediaA.Add(m); };
        b.MediaStart += m => { lock (mediaB) mediaB.Add(m); };
        b.MediaStop += ch => { lock (stops) stops.Add(ch); };

        Assert.Null(a.Dial("voip2", $"sip:verslag@127.0.0.1:{b.LocalPort}"));
        Assert.True(Wait(() => b.Status().Calls.Any(c => c.Ringing)));
        var ringing = b.Status().Calls.Single();
        Assert.Equal("voip1", ringing.Channel); // the first free channel
        Assert.Equal("studio", ringing.Remote);
        Assert.True(Wait(() => a.Status().Calls.Single().State == "gaat over"));
        Thread.Sleep(TestRing); // ringing longer than one transaction timeout must not end the call

        Assert.Null(b.Answer("voip1"));
        Assert.True(Wait(() => mediaA.Count == 1 && mediaB.Count == 1));
        Assert.Equal("in gesprek", a.Status().Calls.Single().State);
        // our order decides on each side's answer: B answers with what it prefers among what A offered
        Assert.Equal("pcmu", mediaB[0].Codec);
        Assert.Equal("pcmu", mediaA[0].Codec);
        Assert.Equal(42000, mediaA[0].RemotePort);
        Assert.Equal(41002, mediaB[0].RemotePort);
        Assert.Equal(41002, mediaA[0].LocalPort);
        Assert.Equal("voip2", mediaA[0].Channel);

        Assert.True(a.Hangup("voip2"));
        Assert.True(Wait(() => b.Status().Calls.Single().State == "afgelopen"));
        Assert.Equal("overkant hing op", b.Status().Calls.Single().Reason);
        Assert.True(Wait(() => stops.Contains("voip1")));
        Assert.Null(a.Dial("voip2", $"sip:verslag@127.0.0.1:{b.LocalPort}")); // the channel is free again
    }

    [Fact]
    public void Declining_and_cancelling_end_the_call_on_both_sides()
    {
        using var a = Agent("studio", 43000, 43002);
        using var b = Agent("verslag", 44000, 44002);
        Assert.Null(a.Dial("voip1", $"sip:verslag@127.0.0.1:{b.LocalPort}"));
        Assert.True(Wait(() => b.Status().Calls.Any(c => c.Ringing)));
        Assert.True(b.Hangup("voip1"));
        Assert.True(Wait(() => a.Status().Calls.Single().State == "afgelopen"));
        Assert.Equal("geweigerd", a.Status().Calls.Single().Reason);

        Assert.Null(a.Dial("voip1", $"sip:verslag@127.0.0.1:{b.LocalPort}"));
        Assert.True(Wait(() => a.Status().Calls.Single().State == "gaat over"));
        Assert.True(a.Hangup("voip1"));
        Assert.True(Wait(() => b.Status().Calls.Single().State == "afgelopen"));
        Assert.Equal("overkant hing op voor opnemen", b.Status().Calls.Single().Reason);

        // no shared codec → the far end refuses with 488
        using var c = Agent("codec", 45000, 45002, codecs: "l16");
        Assert.Null(a.Dial("voip2", $"sip:codec@127.0.0.1:{c.LocalPort}"));
        Assert.True(Wait(() => a.Status().Calls.Single(x => x.Channel == "voip2").State == "afgelopen"));
        Assert.StartsWith("488", a.Status().Calls.Single(x => x.Channel == "voip2").Reason);
    }

    [Fact]
    public void Registers_with_digest_auth_at_a_registrar()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        server.Client.ReceiveTimeout = 5000;
        string? error = null;
        int registers = 0;
        bool hadRport = false;
        var thread = new Thread(() =>
        {
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    var m = SipMessage.Parse(server.Receive(ref from))!;
                    registers++;
                    if (m.Method != "REGISTER") { error = "geen REGISTER: " + m.Method; return; }
                    hadRport = SipMessage.Param(m["Via"], "rport") is not null;
                    var r = SipMessage.Response(i == 0 ? 401 : 200, i == 0 ? "Unauthorized" : "OK");
                    r.Add("Via", m["Via"] + $";received={from.Address}".Replace(";rport", $";rport={from.Port}"));
                    r.Add("From", m["From"]!).Add("To", m["To"] + ";tag=srv").Add("Call-ID", m.CallId).Add("CSeq", m["CSeq"]!);
                    if (i == 0) r.Add("WWW-Authenticate", "Digest realm=\"sip.test\", nonce=\"n0nce\", qop=\"auth\", algorithm=MD5");
                    else
                    {
                        var auth = m["Authorization"] ?? "";
                        string Field(string n) => SipMessage.SplitList(auth[7..]).Select(p => p.Split('=', 2)).First(p => p[0].Trim() == n)[1].Trim('"');
                        var expected = new DigestChallenge("sip.test", "n0nce", null, "MD5", "auth", false)
                            .Answer("0201234567", "test-wachtwoord", "REGISTER", Field("uri"), Convert.ToInt32(Field("nc"), 16), Field("cnonce"));
                        if (!expected.Contains($"response=\"{Field("response")}\"")) { error = "verkeerde digest"; return; }
                        r.Add("Contact", m["Contact"] + ";expires=120");
                    }
                    server.Send(r.ToBytes(), r.ToBytes().Length, from);
                }
            }
            catch (Exception ex) { error = ex.Message; }
        }) { IsBackground = true };
        thread.Start();

        using var agent = Agent("0201234567", 46000, 46002, registrar: $"127.0.0.1:{port}");
        Assert.True(Wait(() => agent.Status().Registration == "aangemeld" || error is not null));
        Assert.Null(error);
        Assert.Equal(2, registers);
        Assert.True(hadRport);
        var until = agent.Status().RegisteredUntilUtc!.Value;
        Assert.InRange((until - DateTime.UtcNow).TotalSeconds, 100, 121);
    }
}

namespace ReaRadioDj.Core.Voip;

/// <summary>
/// G.722 (ITU-T, 1988) at 64 kbit/s: 16 kHz wideband audio, split by a QMF in a low band (6-bit ADPCM) and a high band
/// (2-bit ADPCM), one byte per pair of samples. RTP payload type 9; by a slip in RFC 1890 that everyone keeps, the RTP
/// clock and the SDP name say 8000 ("G722/8000") while the audio is 16 kHz. Written after the ITU-T reference algorithm
/// (blocks 1L–6L and 1H–6H, the adaptive predictors of block 4 and the 24-tap QMF).
/// </summary>
public sealed class G722 : IRtpCodec
{
    public string Name => "g722";
    public string Rtpmap => "G722/8000";
    public int PayloadType => 9;
    public int SampleRate => 16000;
    public int ClockRate => 8000;
    public int FrameSamples => 320;

    private readonly State _enc = new(), _dec = new();

    public int Encode(ReadOnlySpan<float> pcm, Span<byte> payload)
    {
        int pairs = Math.Min(pcm.Length / 2, payload.Length);
        for (int i = 0; i < pairs; i++)
            payload[i] = _enc.Encode(RtpCodecs.ToPcm16(pcm[2 * i]), RtpCodecs.ToPcm16(pcm[2 * i + 1]));
        return pairs;
    }

    public int Decode(ReadOnlySpan<byte> payload, Span<float> pcm)
    {
        int n = Math.Min(payload.Length, pcm.Length / 2);
        for (int i = 0; i < n; i++)
        {
            var (a, b) = _dec.Decode(payload[i]);
            pcm[2 * i] = a / 32768f;
            pcm[2 * i + 1] = b / 32768f;
        }
        return n * 2;
    }

    // ---------------------------------------------------------------- the ITU-T tables

    private static readonly int[] Qmf = [3, -11, 12, 32, -210, 951, 3876, -805, 362, -156, 53, -11];
    private static readonly int[] Q6 = [0, 35, 72, 110, 150, 190, 233, 276, 323, 370, 422, 473, 530, 587, 650, 714, 786, 858, 940, 1023, 1121, 1219, 1339, 1458, 1612, 1765, 1980, 2195, 2557, 2919, 0, 0];
    private static readonly int[] Iln = [0, 63, 62, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 0];
    private static readonly int[] Ilp = [0, 61, 60, 59, 58, 57, 56, 55, 54, 53, 52, 51, 50, 49, 48, 47, 46, 45, 44, 43, 42, 41, 40, 39, 38, 37, 36, 35, 34, 33, 32, 0];
    private static readonly int[] Wl = [-60, -30, 58, 172, 334, 538, 1198, 3042];
    private static readonly int[] Rl42 = [0, 7, 6, 5, 4, 3, 2, 1, 7, 6, 5, 4, 3, 2, 1, 0];
    private static readonly int[] Ilb =
    [
        2048, 2093, 2139, 2186, 2233, 2282, 2332, 2383, 2435, 2489, 2543, 2599, 2656, 2714, 2774, 2834,
        2896, 2960, 3025, 3091, 3158, 3228, 3298, 3371, 3444, 3520, 3597, 3676, 3756, 3838, 3922, 4008,
    ];
    private static readonly int[] Qm4 = [0, -20456, -12896, -8968, -6288, -4240, -2584, -1200, 20456, 12896, 8968, 6288, 4240, 2584, 1200, 0];
    private static readonly int[] Qm6 =
    [
        -136, -136, -136, -136, -24808, -21904, -19008, -16704, -14984, -13512, -12280, -11192, -10232, -9360, -8576, -7856,
        -7192, -6576, -6000, -5456, -4944, -4464, -4008, -3576, -3168, -2776, -2400, -2032, -1688, -1360, -1040, -728,
        24808, 21904, 19008, 16704, 14984, 13512, 12280, 11192, 10232, 9360, 8576, 7856, 7192, 6576, 6000, 5456,
        4944, 4464, 4008, 3576, 3168, 2776, 2400, 2032, 1688, 1360, 1040, 728, 432, 136, -432, -136,
    ];
    private static readonly int[] Ihn = [0, 1, 0];
    private static readonly int[] Ihp = [0, 3, 2];
    private static readonly int[] Wh = [0, -214, 798];
    private static readonly int[] Rh2 = [2, 1, 2, 1];
    private static readonly int[] Qm2 = [-7408, -1616, 7408, 1616];

    private static int Sat(int v) => Math.Clamp(v, short.MinValue, short.MaxValue);

    /// <summary>The state of one band: the pole (a) and zero (b) predictor and what they remember.</summary>
    private sealed class Band
    {
        public int S, Sp, Sz, Nb, Det;
        public readonly int[] R = new int[3], A = new int[3], Ap = new int[3], P = new int[3], D = new int[7], B = new int[7], Bp = new int[7], Sg = new int[7];

        /// <summary>Block 4: reconstruct, adapt the predictors and predict the next sample.</summary>
        public void Adapt(int d)
        {
            // RECONS, PARREC
            D[0] = d;
            R[0] = Sat(S + d);
            P[0] = Sat(Sz + d);
            // UPPOL2
            for (int i = 0; i < 3; i++) Sg[i] = P[i] >> 15;
            int wd1 = Sat(A[1] << 2);
            int wd2 = Sg[0] == Sg[1] ? -wd1 : wd1;
            if (wd2 > 32767) wd2 = 32767;
            int wd3 = (wd2 >> 7) + (Sg[0] == Sg[2] ? 128 : -128);
            wd3 += (A[2] * 32512) >> 15;
            Ap[2] = Math.Clamp(wd3, -12288, 12288);
            // UPPOL1
            Sg[0] = P[0] >> 15;
            Sg[1] = P[1] >> 15;
            wd1 = Sg[0] == Sg[1] ? 192 : -192;
            wd2 = (A[1] * 32640) >> 15;
            Ap[1] = Sat(wd1 + wd2);
            wd3 = Sat(15360 - Ap[2]);
            Ap[1] = Math.Clamp(Ap[1], -wd3, wd3);
            // UPZERO
            wd1 = d == 0 ? 0 : 128;
            Sg[0] = d >> 15;
            for (int i = 1; i < 7; i++)
            {
                Sg[i] = D[i] >> 15;
                wd2 = Sg[i] == Sg[0] ? wd1 : -wd1;
                wd3 = (B[i] * 32640) >> 15;
                Bp[i] = Sat(wd2 + wd3);
            }
            // DELAYZ, DELAYA
            for (int i = 6; i > 0; i--) { D[i] = D[i - 1]; B[i] = Bp[i]; }
            for (int i = 2; i > 0; i--) { R[i] = R[i - 1]; P[i] = P[i - 1]; A[i] = Ap[i]; }
            // FILTEP, FILTEZ, PREDIC
            wd1 = (A[1] * Sat(R[1] + R[1])) >> 15;
            wd2 = (A[2] * Sat(R[2] + R[2])) >> 15;
            Sp = Sat(wd1 + wd2);
            int sz = 0;
            for (int i = 6; i > 0; i--) sz += (B[i] * Sat(D[i] + D[i])) >> 15;
            Sz = Sat(sz);
            S = Sat(Sp + Sz);
        }

        /// <summary>Blocks 3L/3H: the log scale factor and from it the quantizer step.</summary>
        public void Scale(int w, int max, int shift)
        {
            Nb = Math.Clamp(((Nb * 127) >> 7) + w, 0, max);
            int wd1 = (Nb >> 6) & 31;
            int wd2 = shift - (Nb >> 11);
            int wd3 = wd2 < 0 ? Ilb[wd1] << -wd2 : Ilb[wd1] >> wd2;
            Det = wd3 << 2;
        }
    }

    private sealed class State
    {
        private readonly Band _low = new() { Det = 32 }, _high = new() { Det = 8 };
        private readonly int[] _x = new int[24];

        public byte Encode(int s0, int s1)
        {
            // transmit QMF: two input samples become one low-band and one high-band sample
            Array.Copy(_x, 2, _x, 0, 22);
            _x[22] = s0;
            _x[23] = s1;
            int sumOdd = 0, sumEven = 0;
            for (int i = 0; i < 12; i++)
            {
                sumOdd += _x[2 * i] * Qmf[i];
                sumEven += _x[2 * i + 1] * Qmf[11 - i];
            }
            int xLow = (sumEven + sumOdd) >> 14, xHigh = (sumEven - sumOdd) >> 14;

            // low band: SUBTRA, QUANTL, INVQAL, LOGSCL, SCALEL
            int el = Sat(xLow - _low.S);
            int wd = el >= 0 ? el : -(el + 1);
            int i6 = 1;
            for (; i6 < 30; i6++)
                if (wd < (Q6[i6] * _low.Det) >> 12) break;
            int iLow = el < 0 ? Iln[i6] : Ilp[i6];
            int ril = iLow >> 2;
            int dLow = (_low.Det * Qm4[ril]) >> 15;
            _low.Scale(Wl[Rl42[ril]], 18432, 8);
            _low.Adapt(dLow);

            // high band: SUBTRA, QUANTH, INVQAH, LOGSCH, SCALEH
            int eh = Sat(xHigh - _high.S);
            wd = eh >= 0 ? eh : -(eh + 1);
            int mih = wd >= (564 * _high.Det) >> 12 ? 2 : 1;
            int iHigh = eh < 0 ? Ihn[mih] : Ihp[mih];
            int dHigh = (_high.Det * Qm2[iHigh]) >> 15;
            _high.Scale(Wh[Rh2[iHigh]], 22528, 10);
            _high.Adapt(dHigh);

            return (byte)((iHigh << 6) | iLow);
        }

        public (int, int) Decode(byte code)
        {
            int iLow = code & 0x3F, iHigh = (code >> 6) & 0x03;
            // low band: INVQBL, RECONS, LIMIT, and the predictor update from the 4-bit part (INVQAL)
            int rLow = Math.Clamp(_low.S + ((_low.Det * Qm6[iLow]) >> 15), -16384, 16383);
            int ril = iLow >> 2;
            int dLow = (_low.Det * Qm4[ril]) >> 15;
            _low.Scale(Wl[Rl42[ril]], 18432, 8);
            _low.Adapt(dLow);
            // high band
            int dHigh = (_high.Det * Qm2[iHigh]) >> 15;
            int rHigh = Math.Clamp(dHigh + _high.S, -16384, 16383);
            _high.Scale(Wh[Rh2[iHigh]], 22528, 10);
            _high.Adapt(dHigh);

            // receive QMF: back to two 16 kHz samples
            Array.Copy(_x, 2, _x, 0, 22);
            _x[22] = rLow + rHigh;
            _x[23] = rLow - rHigh;
            int out1 = 0, out2 = 0;
            for (int i = 0; i < 12; i++)
            {
                out2 += _x[2 * i] * Qmf[i];
                out1 += _x[2 * i + 1] * Qmf[11 - i];
            }
            return (Sat(out1 >> 11), Sat(out2 >> 11));
        }
    }
}

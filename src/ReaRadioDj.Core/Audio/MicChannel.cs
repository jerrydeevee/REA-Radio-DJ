namespace ReaRadioDj.Core.Audio;

/// <summary>
/// The channel of a stereo input a microphone is on. A sound card shows its inputs in pairs (input 1-2): a microphone on
/// input 1 then only sounds on the left. Mono from one channel puts that channel on both sides.
/// </summary>
public static class MicChannel
{
    public const int Stereo = 0, Left = 1, Right = 2;   // the names for the studio: RoutingDto.MicChannelNames

    public static int Checked(int value) => value is Left or Right ? value : Stereo;

    /// <summary>Copies the chosen channel of interleaved stereo samples to both sides; stereo leaves them as they are.</summary>
    public static void Apply(Span<float> stereo, int channel)
    {
        if (channel is not (Left or Right)) return;
        int from = channel == Left ? 0 : 1;
        for (int i = 0; i + 1 < stereo.Length; i += 2)
        {
            float v = stereo[i + from];
            stereo[i] = v;
            stereo[i + 1] = v;
        }
    }
}

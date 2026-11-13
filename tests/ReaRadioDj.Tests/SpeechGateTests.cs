using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Tests;

/// <summary>The gate behind "only record the talking": pure logic, no audio device.</summary>
public class SpeechGateTests
{
    private const int Rate = 1000;   // 1000 frames a second keeps the numbers readable

    private static float[] Block(int frames, float level) => Enumerable.Repeat(level, frames * 2).ToArray();

    private sealed class Sink
    {
        public readonly List<float> Samples = [];
        public void Write(ReadOnlySpan<float> s) => Samples.AddRange(s.ToArray());
        public int Frames => Samples.Count / 2;
    }

    [Fact]
    public void Silence_is_not_kept()
    {
        var gate = new SpeechGate(Rate, 2, -40, 1, 0.2);
        var sink = new Sink();
        for (int i = 0; i < 50; i++) Assert.Equal(GateChange.None, gate.Process(Block(100, 0.001f), true, sink.Write));
        Assert.Equal(0, sink.Frames);
        Assert.False(gate.IsOpen);
    }

    [Fact]
    public void Talking_opens_with_the_pre_roll_and_closes_after_the_hang_time()
    {
        var gate = new SpeechGate(Rate, 2, -40, 0.5, 0.2);
        var sink = new Sink();
        gate.Process(Block(1000, 0.001f), true, sink.Write);                          // a second of quiet
        Assert.Equal(GateChange.Opened, gate.Process(Block(100, 0.3f), true, sink.Write));
        Assert.Equal(200 + 100, sink.Frames);                                          // 0.2 s pre-roll, then the block
        Assert.Equal(0.001f, sink.Samples[0]);                                         // the pre-roll is the quiet before
        Assert.Equal(0.3f, sink.Samples[^1]);

        // quiet for 0.5 s keeps going (a pause between sentences), past that it closes
        for (int i = 0; i < 5; i++) Assert.Equal(GateChange.None, gate.Process(Block(100, 0.001f), true, sink.Write));
        Assert.Equal(GateChange.Closed, gate.Process(Block(100, 0.001f), true, sink.Write));
        Assert.False(gate.IsOpen);
        int kept = sink.Frames;
        gate.Process(Block(500, 0.001f), true, sink.Write);
        Assert.Equal(kept, sink.Frames);                                               // closed: nothing more
    }

    [Fact]
    public void Sound_while_not_allowed_is_not_recorded()
    {
        var gate = new SpeechGate(Rate, 2, -40, 1, 0.2);
        var sink = new Sink();
        // the microphone is closed: talking in the studio does not go into the recording
        for (int i = 0; i < 10; i++) Assert.Equal(GateChange.None, gate.Process(Block(100, 0.5f), false, sink.Write));
        Assert.Equal(0, sink.Frames);
    }

    [Fact]
    public void Closing_the_microphone_ends_the_take_at_once()
    {
        var gate = new SpeechGate(Rate, 2, -40, 5, 0);
        var sink = new Sink();
        Assert.Equal(GateChange.Opened, gate.Process(Block(100, 0.5f), true, sink.Write));
        Assert.Equal(GateChange.Closed, gate.Process(Block(100, 0.5f), false, sink.Write));
        Assert.Equal(200, sink.Frames);
    }

    [Fact]
    public void A_second_take_has_its_own_pre_roll()
    {
        var gate = new SpeechGate(Rate, 2, -40, 0.1, 0.05);
        var sink = new Sink();
        gate.Process(Block(100, 0.5f), true, sink.Write);
        gate.Process(Block(200, 0f), true, sink.Write);                                // closes
        Assert.False(gate.IsOpen);
        int before = sink.Frames;
        gate.Process(Block(30, 0f), true, sink.Write);
        gate.Process(Block(30, 0f), true, sink.Write);
        Assert.Equal(GateChange.Opened, gate.Process(Block(10, 0.5f), true, sink.Write));
        Assert.Equal(before + 50 + 10, sink.Frames);                                   // only the newest 0.05 s
    }

    [Fact]
    public void The_threshold_is_in_dBFS()
    {
        var gate = new SpeechGate(Rate, 2, -20, 1, 0);
        var sink = new Sink();
        Assert.Equal(GateChange.None, gate.Process(Block(10, 0.09f), true, sink.Write));     // -20.9 dB
        Assert.Equal(GateChange.Opened, gate.Process(Block(10, 0.11f), true, sink.Write));   // -19.2 dB
    }
}

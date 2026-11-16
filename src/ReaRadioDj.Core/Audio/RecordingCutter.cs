using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>A program recording (one clock hour) and when it really started and how long it is.</summary>
public sealed record RecordedFile(string Path, DateTime Start, double Seconds)
{
    public DateTime End => Start.AddSeconds(Seconds);
}

/// <summary>A piece of one recording: from <see cref="Offset"/> seconds into the file, <see cref="Seconds"/> long.</summary>
public sealed record CutPiece(string Path, double Offset, double Seconds);

/// <summary>
/// Cuts a stretch of time out of the program recordings (one MP3 per clock hour) into a file of its own, e.g. the
/// interview of ten minutes ago as an item for the library. A stretch can run over the hour; the pieces are joined.
/// </summary>
public static partial class RecordingCutter
{
    /// <summary>The longest stretch that can be cut at once.</summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromHours(3);

    [GeneratedRegex(@"^uitzending-(\d{4}-\d{2}-\d{2}-\d{2})(-\d+)?\.mp3$", RegexOptions.IgnoreCase)]
    private static partial Regex ProgramName();

    /// <summary>The clock hour in the name of a program recording, or null for another file.</summary>
    public static DateTime? HourOf(string fileName) =>
        ProgramName().Match(fileName) is { Success: true } m
        && DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd-HH", CultureInfo.InvariantCulture, DateTimeStyles.None, out var h) ? h : null;

    /// <summary>
    /// The pieces that make up the stretch from <paramref name="from"/> to <paramref name="to"/>. Files may overlap a
    /// moment (the next hour starts before the last one stops): every second is taken once. Time that no file covers
    /// (the engine was off) is left out.
    /// </summary>
    public static IReadOnlyList<CutPiece> Plan(IEnumerable<RecordedFile> files, DateTime from, DateTime to)
    {
        var pieces = new List<CutPiece>();
        var at = from;
        foreach (var f in files.OrderBy(f => f.Start))
        {
            if (f.End <= at || f.Start >= to) continue;
            var start = f.Start > at ? f.Start : at;
            var end = f.End < to ? f.End : to;
            if ((end - start).TotalSeconds < 0.05) continue;
            pieces.Add(new(f.Path, Math.Round((start - f.Start).TotalSeconds, 3), Math.Round((end - start).TotalSeconds, 3)));
            at = end;
        }
        return pieces;
    }

    /// <summary>The program recordings in a folder (and its month folders) whose hour may hold part of the stretch.</summary>
    public static IEnumerable<string> Candidates(string folder, DateTime from, DateTime to)
    {
        if (!Directory.Exists(folder)) yield break;
        foreach (var path in Directory.EnumerateFiles(folder, "uitzending-*.mp3", SearchOption.AllDirectories))
            if (HourOf(Path.GetFileName(path)) is DateTime h && h <= to && h.AddHours(1).AddMinutes(5) >= from) yield return path;
    }

    /// <summary>
    /// When a recording started and how long it is: its length from the file, its end from the last write (so this also
    /// works for the hour that is still being recorded). Null when BASS cannot read it.
    /// </summary>
    public static RecordedFile? Measure(string path)
    {
        int stream = BASS_StreamCreateFile(false, path, 0, 0, BASS_STREAM_DECODE | BASS_STREAM_PRESCAN | BASS_UNICODE);
        if (stream == 0) return null;
        try
        {
            double seconds = BASS_ChannelBytes2Seconds(stream, BASS_ChannelGetLength(stream, BASS_POS_BYTE));
            if (seconds <= 0) return null;
            return new RecordedFile(path, File.GetLastWriteTime(path).AddSeconds(-seconds), seconds);
        }
        finally { BASS_StreamFree(stream); }
    }

    /// <summary>Joins the pieces into one MP3 at the playout rate; returns its length in seconds.</summary>
    public static unsafe double Cut(IReadOnlyList<CutPiece> pieces, string target, int bitrate)
    {
        if (pieces.Count == 0) throw new InvalidOperationException("Er is niets opgenomen in die tijd.");
        int rate = BassRuntime.SampleRate;
        int dummy = BassRuntime.Check(BASS_StreamCreate((uint)rate, 2, BASS_SAMPLE_FLOAT | BASS_STREAM_DECODE, IntPtr.Zero, IntPtr.Zero), "BASS_StreamCreate(cut)");
        var tmp = target + ".part";
        int encoder = BASS_Encode_MP3_StartFile(dummy, $"-b{bitrate}", BASS_UNICODE, tmp);
        if (encoder == 0) { BASS_StreamFree(dummy); throw new BassException("BASS_Encode_MP3_StartFile(cut)"); }
        long written = 0;
        try
        {
            var buffer = new float[rate];   // half a second of stereo
            foreach (var piece in pieces)
            {
                int mixer = BassRuntime.Check(BASS_Mixer_StreamCreate((uint)rate, 2, BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT | BASS_MIXER_END), "BASS_Mixer_StreamCreate(cut)");
                try
                {
                    int source = BassRuntime.Check(BASS_StreamCreateFile(false, piece.Path, 0, 0, BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT | BASS_STREAM_PRESCAN | BASS_UNICODE),
                        $"BASS_StreamCreateFile({Path.GetFileName(piece.Path)})");
                    BASS_ChannelSetPosition(source, BASS_ChannelSeconds2Bytes(source, piece.Offset), BASS_POS_BYTE);
                    BassRuntime.Check(BASS_Mixer_StreamAddChannel(mixer, source, BASS_STREAM_AUTOFREE), "BASS_Mixer_StreamAddChannel(cut)");
                    long left = (long)Math.Round(piece.Seconds * rate) * 2;
                    while (left > 0)
                    {
                        uint want = (uint)Math.Min(buffer.Length, left) * sizeof(float);
                        uint got = BASS_ChannelGetData(mixer, buffer, want);
                        if (got == uint.MaxValue || got == 0) break;
                        fixed (float* p = buffer) BASS_Encode_Write(encoder, p, got);
                        left -= got / sizeof(float);
                        written += got / sizeof(float);
                    }
                }
                finally { BASS_StreamFree(mixer); }
            }
        }
        finally
        {
            BASS_Encode_Stop(encoder);
            BASS_StreamFree(dummy);
        }
        if (written == 0) { File.Delete(tmp); throw new InvalidOperationException("Er kwam geen geluid uit de opname."); }
        File.Move(tmp, target, overwrite: false);
        return written / 2.0 / rate;
    }

    /// <summary>Writes title, artist and a comment with the time it went on air into the new file.</summary>
    public static void Tag(string path, string title, string artist, DateTime from)
    {
        using var f = TagLib.File.Create(path);
        f.Tag.Title = title;
        f.Tag.Performers = artist.Length > 0 ? [artist] : [];
        f.Tag.Year = (uint)from.Year;
        f.Tag.Comment = $"Uit de uitzending van {from:dd-MM-yyyy HH:mm:ss}";
        f.Save();
    }

    [LibraryImport("bassenc")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool BASS_Encode_Write(int handle, float* buffer, uint length);
}

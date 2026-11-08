using Microsoft.Extensions.Logging;

namespace ReaRadioDj.Server;

public sealed record StudioMessage(long Id, DateTime Time, string Level, string Text);

/// <summary>The last few hundred messages for the operator (events fired, fallbacks, errors), newest last.</summary>
public sealed class MessageCenter(ILogger<MessageCenter> logger)
{
    private const int Size = 300;
    private readonly LinkedList<StudioMessage> _messages = new();
    private readonly object _gate = new();
    private long _next;

    public long LastId { get { lock (_gate) return _next; } }

    public void Info(string text) => Add("info", text);
    public void Warning(string text) => Add("waarschuwing", text);
    public void Error(string text) => Add("fout", text);

    public void Add(string level, string text)
    {
        lock (_gate)
        {
            _messages.AddLast(new StudioMessage(++_next, DateTime.Now, level, text));
            while (_messages.Count > Size) _messages.RemoveFirst();
        }
        logger.LogInformation("[{Level}] {Text}", level, text);
    }

    public IReadOnlyList<StudioMessage> Since(long id)
    {
        lock (_gate) return _messages.Where(m => m.Id > id).ToList();
    }
}

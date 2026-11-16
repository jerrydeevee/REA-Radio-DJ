using System.Net;
using System.Text.Json;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Remote;

namespace ReaRadioDj.Server.Remote;

/// <summary>The remote controls with their own limits (see <see cref="RemoteClient"/>), stored in the station database.</summary>
public sealed class RemoteClientStore(StationDb db, ILogger<RemoteClientStore> logger)
{
    public const string Key = "remote-clients";
    private volatile IReadOnlyList<RemoteClient>? _clients;

    public IReadOnlyList<RemoteClient> Clients => _clients ??= Load();

    private List<RemoteClient> Load()
    {
        if (db.GetSetting(Key) is not { } json) return [];
        try { return RemoteClients.Clean(JsonSerializer.Deserialize<List<RemoteClient>>(json, JsonSerializerOptions.Web) ?? [], CommandBus.Groups); }
        catch (JsonException ex) { logger.LogWarning(ex, "Remote clients unreadable"); return []; }
    }

    /// <summary>Stores the list; a client keeps its key when its id stays the same.</summary>
    public IReadOnlyList<RemoteClient> Save(IEnumerable<RemoteClient> clients)
    {
        var old = Clients.ToDictionary(c => c.Id);
        var list = RemoteClients.Clean(clients.Select(c => c with { KeyHash = old.TryGetValue(c.Id ?? "", out var o) ? o.KeyHash : "" }), CommandBus.Groups);
        Write(list);
        return list;
    }

    /// <summary>Gives a client a new key and returns it; this is the only time the key can be seen.</summary>
    public string? NewKey(string id)
    {
        var list = Clients.ToList();
        int i = list.FindIndex(c => c.Id == id);
        if (i < 0) return null;
        var key = RemoteClients.NewKey();
        list[i] = list[i] with { KeyHash = RemoteClients.Hash(key) };
        Write(list);
        logger.LogInformation("Remote control {Name}: new key", list[i].Name);
        return key;
    }

    /// <summary>Removes the key: the client is then only known by its address.</summary>
    public bool ClearKey(string id)
    {
        var list = Clients.ToList();
        int i = list.FindIndex(c => c.Id == id);
        if (i < 0) return false;
        list[i] = list[i] with { KeyHash = "" };
        Write(list);
        return true;
    }

    private void Write(List<RemoteClient> list)
    {
        db.SetSetting(Key, JsonSerializer.Serialize(list, JsonSerializerOptions.Web));
        _clients = list;
    }

    /// <summary>A client by key (and address), or else a client known by address only.</summary>
    public RemoteClient? Find(string? key, IPAddress? from) =>
        RemoteClients.ByKey(Clients, key, from) ?? (RemoteClients.IsLocal(from) ? null : RemoteClients.ByAddress(Clients, from));
}

using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Remote;

namespace ReaRadioDj.Server.Remote;

/// <summary>Remote controls with their own limits: list, save, new key (shown once), key off. Administrator only.</summary>
public static class RemoteClientsApi
{
    private static RemoteClientDto Dto(RemoteClient c) => new(c.Id, c.Name, c.Enabled, c.HasKey, c.Addresses, c.Groups);

    private static RemoteClientsDto List(RemoteClientStore store) =>
        new(store.Clients.Select(Dto).ToList(), CommandBus.Groups.Where(g => g != RemoteClients.InfoGroup).ToList());

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/remote-clients", (RemoteClientStore store) => List(store));
        api.MapPut("/remote-clients", (List<RemoteClientDto> list, RemoteClientStore store) =>
        {
            store.Save(list.Select(d => new RemoteClient(d.Id ?? "", d.Name ?? "", d.Enabled, "", d.Addresses ?? "", d.Groups ?? [])));
            return List(store);
        });
        api.MapPost("/remote-clients/{id}/key", (string id, RemoteClientStore store) =>
            store.NewKey(id) is { } key ? Results.Ok(new RemoteClientKeyDto(id, key)) : Results.NotFound("Onbekende afstandsbediening: sla de lijst eerst op."));
        api.MapDelete("/remote-clients/{id}/key", (string id, RemoteClientStore store) =>
            store.ClearKey(id) ? Results.Ok(List(store)) : Results.NotFound());
    }
}

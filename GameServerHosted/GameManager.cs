using Microsoft.Extensions.Hosting;

namespace GameServerHosted;

public class GameManager : IHostedService
{
    private readonly NetworkManager _networkManager;

    public GameManager(NetworkManager networkManager)
    {
        _networkManager = networkManager;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _networkManager.StartServer(1337);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
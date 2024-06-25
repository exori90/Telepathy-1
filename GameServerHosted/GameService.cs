using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Text;
using Telepathy;

namespace GameServerHosted;

public class GameService : IHostedService
{
    private Server Server;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        StartServer(1337, 0);
        RunServer(0);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public const int MaxMessageSize = 16 * 1024;
    static long messagesReceived = 0;
    static long dataReceived = 0;

    public void StartServer(int port, int seconds)
    {
        Log.Info("[Telepathy] Starting server");

        Server = new Server(MaxMessageSize);

        Server.OnData = ServerOnData;
        Server.OnConnected = ServerOnConnected;
        Server.OnDisconnected = ServerOnDisconnected;

        Server.Start(port);
    }

    public void RunServer(int seconds)
    {
        int serverFrequency = 60;
        Stopwatch stopwatch = Stopwatch.StartNew();

        var runTimer = Stopwatch.StartNew();
        bool runServer = true;

        while (runServer)
        {
            // tick and process as many as we can. will auto reply.
            // (100k limit to avoid deadlocks)
            Server.Tick(100000);

            // sleep
            Thread.Sleep(1000 / serverFrequency);

            // report every 10 seconds
            if (stopwatch.ElapsedMilliseconds > 1000 * 2)
            {
                Log.Info(string.Format("[Telepathy] Thread[" + Thread.CurrentThread.ManagedThreadId + "]: Server in={0} ({1} KB/s)  out={0} ({1} KB/s) ReceiveQueue={2}", messagesReceived, (dataReceived * 1000 / (stopwatch.ElapsedMilliseconds * 1024)), Server.ReceivePipeTotalCount.ToString()));
                stopwatch.Stop();
                stopwatch = Stopwatch.StartNew();
                messagesReceived = 0;
                dataReceived = 0;
            }

            if (seconds != 0)
            {
                runServer = (runTimer.ElapsedMilliseconds < (seconds * 1000));
            }
        }
    }

    public void ServerOnData(int connectionId, ArraySegment<byte> data)
    {
        Log.Info($"Client #{connectionId} sends: {Encoding.ASCII.GetString(data.ToArray(), 0, data.Count)}");

        Server.Send(connectionId, data);
        messagesReceived++;
        dataReceived += data.Count;
    }

    public void ServerOnConnected(int val)
    {
        Log.Info("OnConnected");
    }

    public void ServerOnDisconnected(int val)
    {
        Log.Info("OnDisconnected");
    }
}
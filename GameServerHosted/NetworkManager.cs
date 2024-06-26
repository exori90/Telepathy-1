using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Text;
using Telepathy;

namespace GameServerHosted;

public class NetworkManager
{
    public NetworkManager()
    {
        Log.Info("[Telepathy] Starting server");
    }

    ~NetworkManager()
    {
        Log.Info("[Telepathy] Stopping server");
    }

    private Server Server;

    public const int MaxMessageSize = 16 * 1024;
    static long messagesReceived = 0;
    static long dataReceived = 0;

    public void StartServer(int port)
    {
        Server = new Server(MaxMessageSize);

        Server.OnData = ServerOnData;
        Server.OnConnected = ServerOnConnected;
        Server.OnDisconnected = ServerOnDisconnected;

        Server.Start(port);

        MessageLoop();
    }

    private void MessageLoop()
    {
        int serverFrequency = 60;

        while (true)
        {
            Server.Tick(100000);
            Thread.Sleep(1000 / serverFrequency);
        }
    }

    private void ServerOnData(int connectionId, ArraySegment<byte> data)
    {
        Log.Info($"Client #{connectionId} sends: {Encoding.ASCII.GetString(data.ToArray(), 0, data.Count)}");

        Server.Send(connectionId, data);
        messagesReceived++;
        dataReceived += data.Count;
    }

    private void ServerOnConnected(int val)
    {
        Log.Info("OnConnected");
    }

    private void ServerOnDisconnected(int val)
    {
        Log.Info("OnDisconnected");
    }
}
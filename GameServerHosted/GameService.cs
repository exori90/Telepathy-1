﻿using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Text;
using Telepathy;

namespace GameServerHosted;

public class GameService : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        StartServer(1337, 0);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public const int MaxMessageSize = 16 * 1024;
    static long messagesReceived = 0;
    static long dataReceived = 0;

    public static void StartServer(int port, int seconds)
    {

        // create server
        Server server = new Server(MaxMessageSize);

        // OnData replies and updates statistics
        server.OnData = (connectionId, data) => {
            Log.Info($"Client #{connectionId} sends: {Encoding.ASCII.GetString(data.ToArray(), 0, data.Count)}");
            server.Send(connectionId, data);
            messagesReceived++;
            dataReceived += data.Count;
        };
        server.OnConnected = (val) => { Log.Info("OnConnected"); };

        server.Start(port);
        int serverFrequency = 60;
        Log.Info("[Telepathy] Started server");

        Stopwatch stopwatch = Stopwatch.StartNew();

        var runTimer = Stopwatch.StartNew();
        bool runServer = true;

        while (runServer)
        {
            // tick and process as many as we can. will auto reply.
            // (100k limit to avoid deadlocks)
            server.Tick(100000);

            // sleep
            Thread.Sleep(1000 / serverFrequency);

            // report every 10 seconds
            if (stopwatch.ElapsedMilliseconds > 1000 * 2)
            {
                Log.Info(string.Format("[Telepathy] Thread[" + Thread.CurrentThread.ManagedThreadId + "]: Server in={0} ({1} KB/s)  out={0} ({1} KB/s) ReceiveQueue={2}", messagesReceived, (dataReceived * 1000 / (stopwatch.ElapsedMilliseconds * 1024)), server.ReceivePipeTotalCount.ToString()));
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
}
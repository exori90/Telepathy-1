﻿using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Text;
using System.Timers;
using Telepathy;

namespace GameClientHosted;

public class ClientService : IHostedService
{
    private System.Threading.Timer? _timer = null;
    int clientFrequency = 14;
    List<Client> clients = new List<Client>();

    public const int MaxMessageSize = 16 * 1024;
    static long messagesSent = 0;
    static long messagesReceived = 0;
    static long dataReceived = 0;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Thread.Sleep(3000);
        StartClients("127.0.0.1", 1337, 1);

        DoWork(null);
        
        return Task.CompletedTask;
    }

    private void DoWork(object? state)
    {
        int seconds = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();


        var timer = new System.Timers.Timer(1000.0 / clientFrequency);

        // THIS HAPPENS IN DIFFERENT THREADS.
        // so make sure that GetNextMessage is thread safe!

        timer.Elapsed += (object sender, ElapsedEventArgs e) =>
        {
            foreach (Client client in clients)
            {
                if (client.Connected)
                {
                    // tick client to receive and update statistics in OnData
                    client.Tick(1000);
                }
            }
        };

        timer.AutoReset = true;
        timer.Enabled = true;

        if (seconds == 0)
        {
            Console.ReadLine();
        }
        else
        {
            Thread.Sleep(seconds * 1000);
        }

        timer.Stop();
        timer.Dispose();

        foreach (Client client in clients)
        {
            client.Disconnect();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public void StartClients(string host, int port, int clientAmount)
    {
        Log.Error("[Telepathy] Starting " + clientAmount + " clients...");
 
        for (int i = 0; i < clientAmount; ++i)
        {
            Client client = new Client(MaxMessageSize);
            // setup hook to add to statistics
            client.OnData = data => {
                Log.Info($"Server sends: {Encoding.ASCII.GetString(data.ToArray(), 0, data.Count)}");
                messagesReceived++;
                dataReceived += data.Count;
            };
            client.Connect(host, port);
            clients.Add(client);
            Thread.Sleep(15);
        }
        Log.Info("[Telepathy] Started all clients");

        if (!clients.All(cl => cl.Connected))
        {
            Log.Info("[Telepathy] Not all clients were connected successfully. aborting.");
            return;
        }
    }
}
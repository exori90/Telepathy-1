using GameServerHosted;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<NetworkManager>();
builder.Services.AddHostedService<GameManager>();

using IHost host = builder.Build();

await host.RunAsync();
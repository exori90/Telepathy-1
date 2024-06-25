using GameClientHosted;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<ClientService>();
using IHost host = builder.Build();

await host.RunAsync();
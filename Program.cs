using ColaWorker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .UseSystemd()
    .ConfigureServices((hostContext, services) =>
    {
        // Registramos nuestro servicio principal
        services.AddHostedService<GestorDeColas>();
    })
    .Build();

await host.RunAsync();

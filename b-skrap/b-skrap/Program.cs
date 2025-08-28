using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddHttpClient("scraper", c =>
        {
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (BrainvilleWatcher)");
        });
    })
    .Build();


builder.Build().Run();

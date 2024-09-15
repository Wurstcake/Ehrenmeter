using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Ehrenmeter.Backend.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

#if DEBUG
        services.AddSingleton<IHTMLViewRetrievalService, HTMLViewRetrievalServiceDev>();
#else
        services.AddSingleton<IHTMLViewRetrievalService, HTMLViewRetrievalService>();
#endif

        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IPasswordService, PasswordService>();
    })
    .Build();

host.Run();

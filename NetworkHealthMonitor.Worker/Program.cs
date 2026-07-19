using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetworkHealthMonitor.Worker;

var options = WorkerOptions.Parse(args);

if (options.HealthCheck)
{
    Environment.ExitCode = await WorkerMaintenance.RunHealthCheckAsync(options);
    return;
}

if (options.DatabaseVerificationMode != DatabaseVerificationMode.None)
{
    Environment.ExitCode = await DatabaseVerificationCommand.RunAsync(options);
    return;
}

if (options.RunOnce)
{
    await using var scheduler = await WorkerComposition.CreateSchedulerAsync(options);
    await scheduler.RunDuePlansOnceAsync();
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(options);
builder.Services.AddHostedService<WorkerService>();
builder.Services.AddWindowsService(serviceOptions =>
{
    serviceOptions.ServiceName = "NetworkHealthMonitorWorker";
});

await builder.Build().RunAsync();

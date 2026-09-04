using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Labs.Runtime.Api.Endpoints;
using Labs.Runtime.Application.Abstractions;
using Labs.Runtime.Application.Execution;
using Labs.Runtime.Application.Jobs;
using Labs.Runtime.Core.Execution;
using Labs.Runtime.Infrastructure.Catalog;
using Labs.Runtime.Infrastructure.DotNet;
using Labs.Runtime.Infrastructure.Jobs;
using Labs.Runtime.Infrastructure.Output;
using Labs.Runtime.Infrastructure.Processes;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var configuredLabsRoot = builder.Configuration["LabsRuntime:LabsRoot"];
if (string.IsNullOrWhiteSpace(configuredLabsRoot))
{
    throw new InvalidOperationException(
        "Configuration value 'LabsRuntime:LabsRoot' is required.");
}

var labsRoot = Path.IsPathRooted(configuredLabsRoot)
    ? configuredLabsRoot
    : Path.GetFullPath(configuredLabsRoot, builder.Environment.ContentRootPath);

var allowedOrigins = builder.Configuration
    .GetSection("LabsRuntime:AllowedOrigins")
    .GetChildren()
    .Select(origin => origin.Value)
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Cast<string>()
    .ToArray();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "DELETE")
            .AllowAnyHeader();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter(
        "lab-runs",
        limiterOptions =>
        {
            limiterOptions.PermitLimit = 3;
            limiterOptions.Window = TimeSpan.FromMinutes(1);
            limiterOptions.QueueLimit = 0;
            limiterOptions.AutoReplenishment = true;
        });
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ILabCatalog>(new InMemoryLabCatalog());
builder.Services.AddSingleton<ILabJobStore, InMemoryLabJobStore>();
builder.Services.AddSingleton(
    new InMemoryLabOutputStoreOptions(maxBufferedOutputsPerJob: 4096));
builder.Services.AddSingleton<ILabOutputStore, InMemoryLabOutputStore>();
builder.Services.AddSingleton<IProcessRunner, SystemProcessRunner>();
builder.Services.AddSingleton(
    new DotNetProcessExecutorOptions(
        labsRoot,
        BuiltInDotNetLabProjects.All));
builder.Services.AddSingleton<ILabExecutor, DotNetProcessExecutor>();
builder.Services.AddSingleton<LabExecutorResolver>();
builder.Services.AddSingleton(
    new LabJobOrchestratorOptions(cleanupTimeout: TimeSpan.FromSeconds(10)));
builder.Services.AddSingleton<LabJobOrchestrator>();
builder.Services.AddSingleton<ILabRunCoordinator, LabRunCoordinator>();

var app = builder.Build();

app.UseCors();
app.UseRateLimiter();

app.MapGet(
    "/",
    () => Results.Ok(new
    {
        service = "Backend Engineering Labs Runtime",
        status = "ok"
    }));
app.MapLabRunEndpoints();

app.Run();

public partial class Program;

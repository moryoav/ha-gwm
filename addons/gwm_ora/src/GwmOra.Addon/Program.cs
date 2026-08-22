using System.Text.Json;
using GwmOra.Addon.Configuration;
using GwmOra.Addon.Gwm;
using GwmOra.Addon.Ingress;
using GwmOra.Addon.Models;
using GwmOra.Addon.RemoteCommands;
using GwmOra.Addon.Supervisor;

var optionsPath = Environment.GetEnvironmentVariable("GWM_ORA_OPTIONS_PATH") ?? "/data/options.json";
var statePath = Environment.GetEnvironmentVariable("GWM_ORA_STATE_PATH") ?? "/data/state.json";
var addonOptions = AddonOptionsLoader.Load(optionsPath);
var stateStore = AddonStateStore.Load(statePath);

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(addonOptions.ToMicrosoftLogLevel());

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
});

builder.Services.AddSingleton(addonOptions);
builder.Services.AddSingleton(stateStore);
builder.Services.AddSingleton<GwmApiClientFactory>();
builder.Services.AddSingleton<SupervisorOptionsService>();
builder.Services.AddSingleton<GwmAuthenticationService>();
builder.Services.AddSingleton<RemoteCommandStore>();
builder.Services.AddSingleton<RemoteCommandService>();
builder.Services.AddSingleton<GwmVehicleService>();
builder.Services.AddHostedService<VehiclePollingWorker>();
builder.Services.AddHostedService<SupervisorDiscoveryWorker>();

var app = builder.Build();
var addonVersion = Environment.GetEnvironmentVariable("GWM_ORA_ADDON_VERSION") ?? "dev";
var addonArch = Environment.GetEnvironmentVariable("GWM_ORA_ADDON_ARCH") ?? "unknown";
app.Logger.LogInformation("GWM add-on {Version} starting on {Architecture}", addonVersion, addonArch);

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        var expected = $"Bearer {stateStore.State.ApiToken}";
        if (!String.Equals(authorization, expected, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
            return;
        }
    }
    else if (!IngressAccess.IsAllowed(context))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Ingress access is only allowed through Home Assistant.");
        return;
    }

    await next();
});

var api = app.MapGroup("/api/v1");

api.MapGet("/health", (GwmVehicleService vehicles) => Results.Ok(vehicles.GetHealth()));

api.MapGet("/vehicles", (GwmVehicleService vehicles) => Results.Ok(vehicles.GetVehicles()));

api.MapPost("/refresh", async (GwmVehicleService vehicles, CancellationToken cancellationToken) =>
{
    try
    {
        await vehicles.RefreshNowAsync(cancellationToken);
        return Results.Accepted(value: vehicles.GetVehicles());
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

api.MapGet("/commands/{commandId}", (string commandId, RemoteCommandService commands) =>
{
    var command = commands.Get(commandId);
    return command is null ? Results.NotFound(new { error = "command_not_found" }) : Results.Ok(command);
});

api.MapPost("/vehicles/{vin}/commands/climate", (string vin, ClimateCommandRequest request, RemoteCommandService commands) =>
{
    try
    {
        return Results.Accepted(value: commands.EnqueueClimate(vin, request));
    }
    catch (RemoteCommandUnavailableException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
    }
    catch (ArgumentException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
    }
});

api.MapPost("/vehicles/{vin}/commands/lock", (string vin, LockCommandRequest request, RemoteCommandService commands) =>
{
    try
    {
        return Results.Accepted(value: commands.EnqueueLock(vin, request));
    }
    catch (RemoteCommandUnavailableException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
    }
    catch (ArgumentException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
    }
});

api.MapPost("/vehicles/{vin}/commands/windows/close", (string vin, RemoteCommandService commands) =>
{
    try
    {
        return Results.Accepted(value: commands.EnqueueWindowClose(vin));
    }
    catch (RemoteCommandUnavailableException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
    }
});

app.MapGet("/", (GwmVehicleService vehicles) => IngressPage.Render(vehicles));

await app.RunAsync();

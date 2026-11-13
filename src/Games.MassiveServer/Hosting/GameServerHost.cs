using Games.MassiveServer.Configuration;
using Games.MassiveServer.Endpoints;
using Games.MassiveServer.Services;
using Microsoft.Extensions.Options;

namespace Games.MassiveServer.Hosting;

/// <summary>Builds an HTTP and UDP host for a dedicated game mode.</summary>
public static class GameServerHost
{
    /// <summary>Creates a game server application using the specified defaults.</summary>
    /// <param name="args">Command-line arguments passed to the host.</param>
    /// <param name="preset">Default matchmaking and UDP settings for the game mode.</param>
    /// <returns>The configured web application.</returns>
    public static WebApplication CreateApplication(string[] args, GameServerPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration["GameAuth:BaseUrl"] ??= "http://localhost:5000";
        builder.Configuration["GameAuth:TimeoutSeconds"] ??= "5";
        builder.Configuration["GameServer:UdpPort"] ??= preset.UdpPort.ToString();
        builder.Configuration["GameServer:PublicAddress"] ??= "127.0.0.1";
        builder.Configuration["Matchmaking:GameCode"] ??= preset.GameCode;
        builder.Configuration["Matchmaking:MinPlayers"] ??= preset.MinPlayers.ToString();
        builder.Configuration["Matchmaking:MaxPlayers"] ??= preset.MaxPlayers.ToString();
        builder.Configuration["Matchmaking:MatchStartDelaySeconds"] ??= preset.MatchStartDelaySeconds.ToString();
        builder.Configuration["Matchmaking:PersistentWorld"] ??= preset.PersistentWorld.ToString();
        builder.Configuration["Matchmaking:QueueTimeoutSeconds"] ??= "60";
        builder.Configuration["Matchmaking:ConnectionTicketLifetimeSeconds"] ??= "120";

        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IValidateOptions<GameAuthOptions>, GameAuthOptionsValidator>();
        builder.Services.AddOptions<GameAuthOptions>()
            .BindConfiguration(GameAuthOptions.SectionName)
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<MatchmakingOptions>, MatchmakingOptionsValidator>();
        builder.Services.AddOptions<MatchmakingOptions>()
            .BindConfiguration(MatchmakingOptions.SectionName)
            .Validate(
                options => options.MaxPlayers <= preset.MaxPlayers,
                $"Matchmaking:MaxPlayers cannot exceed the {preset.MaxPlayers}-player limit for this game mode.")
            .ValidateOnStart();
        builder.Services.AddOptions<GameServerOptions>()
            .BindConfiguration(GameServerOptions.SectionName)
            .Validate(options =>
                options.UdpPort is > 0 and <= 65535
                && !string.IsNullOrWhiteSpace(options.PublicAddress),
                "GameServer:UdpPort and GameServer:PublicAddress must be configured.")
            .ValidateOnStart();
        builder.Services.AddHttpClient<IPlatformTokenValidator, PlatformTokenValidator>(
            (serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<GameAuthOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            });
        builder.Services.AddSingleton<IMatchmakingService, MatchmakingService>();
        builder.Services.AddHostedService<MatchmakingWorker>();
        builder.Services.AddHostedService<GameServerHostedService>();

        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" }))
            .WithName("GetGameServerHealth")
            .WithSummary("Check game server health");
        app.MapMatchmakingEndpoints();

        return app;
    }
}

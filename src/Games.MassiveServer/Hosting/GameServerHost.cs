using System.Net;
using System.Threading.RateLimiting;
using Games.MassiveServer.Configuration;
using Games.MassiveServer.Endpoints;
using Games.MassiveServer.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Plataforma.Core.Configuration;

namespace Games.MassiveServer.Hosting;

/// <summary>Builds an HTTP and UDP host for a dedicated game mode.</summary>
public static class GameServerHost
{
    private const string WebGlCorsPolicy = "WebGlCors";

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

        var allowedCorsOrigins = CorsOriginHelper.ParseAllowedOrigins(builder.Configuration["Cors:AllowedOrigins"]);
        builder.Services.AddCors(options =>
        {
            options.AddPolicy(WebGlCorsPolicy, policy =>
            {
                if (allowedCorsOrigins.Length > 0)
                {
                    policy.WithOrigins(allowedCorsOrigins);
                }
                else if (builder.Environment.IsDevelopment())
                {
                    policy.SetIsOriginAllowed(CorsOriginHelper.IsLoopbackOrigin);
                }

                policy.WithHeaders("Authorization", "Content-Type")
                    .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS");
            });
        });
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 2;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Loopback, 8));
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.IPv6Loopback, 128));
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("192.168.0.0"), 16));
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("matchmaking-write", httpContext =>
            {
                var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    remoteIp,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromSeconds(10),
                        PermitLimit = 10,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
            options.AddPolicy("matchmaking-read", httpContext =>
            {
                var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    remoteIp,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromSeconds(10),
                        PermitLimit = 60,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
        });
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
        builder.Services.AddSingleton<IValidateOptions<GameServerOptions>, GameServerOptionsValidator>();
        builder.Services.AddOptions<GameServerOptions>()
            .BindConfiguration(GameServerOptions.SectionName)
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
        builder.Services.AddSingleton<GameServerHostedService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<GameServerHostedService>());

        var app = builder.Build();
        app.UseForwardedHeaders();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            await next();
        });
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseCors(WebGlCorsPolicy);
        app.UseRateLimiter();
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(20)
        });

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" }))
            .WithName("GetGameServerHealth")
            .WithSummary("Check game server health");
        app.MapMatchmakingEndpoints();
        app.MapWebSocketGameEndpoint();

        return app;
    }
}

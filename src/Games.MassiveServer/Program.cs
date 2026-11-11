using Games.MassiveServer.Configuration;
using Games.MassiveServer.Endpoints;
using Games.MassiveServer.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

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

app.Run();

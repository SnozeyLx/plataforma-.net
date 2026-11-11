namespace Games.MassiveServer.Services;

/// <summary>Periodically forms matches and expires stale queue entries.</summary>
public sealed class MatchmakingWorker(IMatchmakingService matchmakingService) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            matchmakingService.ProcessQueue();
            await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
        }
    }
}

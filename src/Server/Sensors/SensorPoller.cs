using HwMonitor.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace HwMonitor.Sensors;

public sealed class SensorHub : Hub;

/// <summary>Reads the sensors on a timer, keeps the latest snapshot and sends it to all browsers.</summary>
public sealed class SensorPoller(
    ISensorSource source,
    IHubContext<SensorHub> hub,
    IOptions<SensorOptions> options,
    ILogger<SensorPoller> logger) : BackgroundService
{
    private volatile SensorSnapshot? _latest;

    public SensorSnapshot? Latest => _latest;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(250, options.Value.PollIntervalMs)));
        var failing = false;
        var fps = new FpsFilter();
        do
        {
            SensorSnapshot snapshot;
            try
            {
                snapshot = source.Read();
                snapshot = snapshot with { Fps = fps.Add(snapshot.Fps) };
                if (failing)
                    logger.LogInformation("Reading sensors works again");
                failing = false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log the full error once, not every poll.
                if (!failing)
                    logger.LogError(ex, "Reading sensors failed");
                else
                    logger.LogDebug(ex, "Reading sensors still fails");
                failing = true;

                // Keep the old values (and their timestamp), but show the error in the UI.
                if (_latest is not { } last)
                    continue;
                snapshot = last with { Status = last.Status with { Message = $"Reading sensors failed: {ex.Message}" } };
            }

            _latest = snapshot;
            try
            {
                await hub.Clients.All.SendAsync("snapshot", snapshot, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Sending the snapshot failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

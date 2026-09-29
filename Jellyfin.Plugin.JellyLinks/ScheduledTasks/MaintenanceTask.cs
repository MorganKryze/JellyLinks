using Jellyfin.Plugin.JellyLinks.Maintenance;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyLinks.ScheduledTasks;

public sealed class MaintenanceTask : IScheduledTask
{
    private readonly MaintenanceRunner _runner;
    private readonly ILogger<MaintenanceTask> _log;

    public MaintenanceTask(MaintenanceRunner runner, ILogger<MaintenanceTask> log)
    {
        _runner = runner;
        _log = log;
    }

    public string Name => "JellyLinks maintenance";
    public string Key => "JellyLinksMaintenance";
    public string Description => "Expires batches, closes idle sessions, folds old sessions into totals.";
    public string Category => "JellyLinks";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var r = _runner.Run();
        _log.LogInformation("[JellyLinks] maintenance: {Expired} expired, {Interrupted} interrupted, {Abandoned} abandoned, {Purged} purged",
            r.Expired, r.Interrupted, r.Abandoned, r.Purged);
        progress.Report(100);
        return Task.CompletedTask;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
        new[] { new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromHours(4).Ticks } };
}

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// One implementation per registered task type. Each handler exposes a stable
/// <see cref="Key"/> (matched against <c>ScheduledTask.TaskKey</c>) and a single
/// <see cref="ExecuteAsync"/> method. The runner resolves these via
/// <see cref="IServiceProvider"/> from a fresh scope on every tick so the
/// handler gets a clean DbContext and other scoped dependencies.
/// </summary>
public interface IScheduledTaskHandler
{
    /// <summary>Discriminator string — matched case-insensitively against ScheduledTask.TaskKey.</summary>
    string Key { get; }

    /// <summary>Default display name; used when seeding a brand-new row for this handler.</summary>
    string DefaultName { get; }

    /// <summary>Default description; used when seeding a brand-new row for this handler.</summary>
    string DefaultDescription { get; }

    /// <summary>Default interval in seconds for a brand-new seed row.</summary>
    int DefaultIntervalSeconds { get; }

    /// <summary>Execute the task. Throw on hard failure; return a summary on success/no-op.</summary>
    Task<TaskRunSummary> ExecuteAsync(CancellationToken ct);
}

/// <summary>Per-execution metrics returned by an <see cref="IScheduledTaskHandler"/>.</summary>
public record TaskRunSummary(int ItemsChecked, int ItemsUpdated, string? Output);

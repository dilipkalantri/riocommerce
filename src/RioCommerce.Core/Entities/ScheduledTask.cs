using RioCommerce.Core.Enums;

namespace RioCommerce.Core.Entities;

/// <summary>
/// One row per logical background job. The <see cref="TaskKey"/> string discriminator
/// maps to an <c>IScheduledTaskHandler</c> registered in DI — same pattern nopCommerce
/// uses for its scheduled tasks.
/// </summary>
public class ScheduledTask : BaseEntity
{
    /// <summary>Stable discriminator that maps to a registered handler (e.g. "PaymentStatusSync").</summary>
    public string TaskKey { get; set; } = string.Empty;

    /// <summary>Human-readable name shown in the admin grid.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>One-line description shown below the name.</summary>
    public string? Description { get; set; }

    /// <summary>Interval between runs, in seconds.</summary>
    public int IntervalSeconds { get; set; } = 3600;

    public bool Enabled { get; set; } = true;

    /// <summary>If true, the next failure disables the task automatically.</summary>
    public bool StopOnError { get; set; }

    /// <summary>Hard cap on a single execution. The runner cancels the task token after this many seconds.</summary>
    public int TimeoutSeconds { get; set; } = 300;

    public DateTime? LastRunAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public ScheduledTaskStatus LastStatus { get; set; } = ScheduledTaskStatus.Idle;
    public string? LastError { get; set; }
    public int LastDurationMs { get; set; }
    public int LastItemsChecked { get; set; }
    public int LastItemsUpdated { get; set; }
}

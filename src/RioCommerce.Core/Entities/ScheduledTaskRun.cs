using RioCommerce.Core.Enums;

namespace RioCommerce.Core.Entities;

/// <summary>One row per task execution — the History tab on /admin/scheduled-tasks reads these.</summary>
public class ScheduledTaskRun : BaseEntity
{
    public Guid ScheduledTaskId { get; set; }
    public ScheduledTask? Task { get; set; }

    /// <summary>"schedule" (background runner fired it) or "manual" (admin clicked Run Now).</summary>
    public string Trigger { get; set; } = "schedule";

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int DurationMs { get; set; }
    public ScheduledTaskStatus Status { get; set; } = ScheduledTaskStatus.Running;

    public int ItemsChecked { get; set; }
    public int ItemsUpdated { get; set; }

    /// <summary>Human-readable summary of what happened (per-task — e.g. "5 orders checked · 2 settled").</summary>
    public string? Output { get; set; }

    /// <summary>Exception message + first stack frame on failure.</summary>
    public string? Error { get; set; }
}

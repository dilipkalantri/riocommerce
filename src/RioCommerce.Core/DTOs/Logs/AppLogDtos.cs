using RioCommerce.Core.Enums;

namespace RioCommerce.Core.DTOs.Logs;

/// <summary>
/// One <c>app_logs</c> row as the admin UI sees it — a flat projection of
/// <see cref="Entities.AppLog"/> with no navigation properties, so the log list can be
/// serialised and cached without dragging the entity graph along.
/// </summary>
public class AppLogItem
{
    public Guid Id { get; set; }

    /// <summary>Severity. Drives the row colour and the "min level" filter.</summary>
    public AppLogLevel Level { get; set; }

    /// <summary>Logical area that produced the event — "SerialKey", "Checkout", "Auth".</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Short machine-readable code — "serial_key.enqueued". Null for ad-hoc messages.</summary>
    public string? EventCode { get; set; }

    /// <summary>Human-readable single-line summary — what shows in the list view.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Full exception text including stack trace, when the event recorded one.</summary>
    public string? Exception { get; set; }

    /// <summary>Structured context as raw JSON. Pretty-printed by the detail pane, not here —
    /// the list never parses it, so a malformed payload stays viewable rather than throwing.</summary>
    public string? Properties { get; set; }

    public Guid? UserId { get; set; }
    public Guid? OrderId { get; set; }

    /// <summary>Generic entity tag + value for events not tied to an order.</summary>
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }

    /// <summary>HTTP path that produced the event, if it came from a web request.</summary>
    public string? RequestPath { get; set; }

    /// <summary>Server hostname that wrote the row — tells instances apart once there are several.</summary>
    public string? Source { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Query filter for the admin log browser. Every field is optional and they compose:
/// supplying a level, a category and a date range returns only rows satisfying all three.
/// <b>Null means "don't narrow on this"</b>, which is what lets the unfiltered page and a
/// fully-filtered one run through the same query path.
/// </summary>
public class AppLogFilter
{
    /// <summary>Inclusive floor on severity — <c>Warning</c> returns Warning, Error and Critical.
    /// Null returns every level.</summary>
    public AppLogLevel? MinLevel { get; set; }

    /// <summary>Exact category match, from the categories actually observed in the table.</summary>
    public string? Category { get; set; }

    /// <summary>Exact event-code match — groups one class of event without parsing message text.</summary>
    public string? EventCode { get; set; }

    /// <summary>Narrow to one order's breadcrumbs — the "what happened to order X" view.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Narrow to events attributed to one actor.</summary>
    public Guid? UserId { get; set; }

    // ── Date range, inclusive on both ends. Compared against CreatedAt, which is stored UTC,
    //    so callers convert from local time before setting these. ──
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }

    /// <summary>Free text, case-insensitive, across message / event code / properties JSON.</summary>
    public string? Query { get; set; }

    public int Page { get; set; } = 1;

    /// <summary>Rows per page. Clamped to 1..500 by the service so a hand-edited value can't
    /// ask for the whole table.</summary>
    public int PageSize { get; set; } = 50;
}

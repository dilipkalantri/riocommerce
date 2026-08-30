namespace RioCommerce.Core.Enums;

/// <summary>
/// Lead lifecycle stage. Backed by the PostgreSQL enum type <c>lead_status</c> (see
/// <c>HasPostgresEnum&lt;LeadStatus&gt;</c>), so labels are matched by snake_case NAME, not ordinal.
///
/// <para>The first five members are the original set and MUST keep their exact names — existing rows
/// store them as the labels <c>new, contacted, follow_up, converted, lost</c>. The CRM stages added
/// afterwards are appended for the same reason: PostgreSQL enum labels can only be added, never
/// renumbered. Display order in the admin UI is controlled explicitly by
/// <c>LeadStageMeta.Pipeline</c>, so declaration order here does not constrain the dropdown.</para>
/// </summary>
public enum LeadStatus
{
    New,
    Contacted,
    FollowUp,
    Converted,
    Lost,
    // ── CRM stages (added later; appended so the five above keep their labels) ──
    Interested,
    DemoScheduled,
    PaymentPending
}

/// <summary>Presentation helpers for <see cref="LeadStatus"/> — pipeline order, labels and colours.</summary>
public static class LeadStageMeta
{
    /// <summary>Stages in sales-pipeline order (not declaration order) for dropdowns and filters.</summary>
    public static readonly LeadStatus[] Pipeline =
    {
        LeadStatus.New,
        LeadStatus.Contacted,
        LeadStatus.Interested,
        LeadStatus.FollowUp,
        LeadStatus.DemoScheduled,
        LeadStatus.PaymentPending,
        LeadStatus.Converted,
        LeadStatus.Lost
    };

    public static string Label(LeadStatus s) => s switch
    {
        LeadStatus.FollowUp => "Follow-up",
        LeadStatus.DemoScheduled => "Demo Scheduled",
        LeadStatus.PaymentPending => "Payment Pending",
        _ => s.ToString()
    };

    /// <summary>Stage colour modifier — pairs with the <c>.stage</c> / <c>.stage-select</c> badge
    /// classes in app.css. One distinct colour per pipeline stage.</summary>
    public static string Css(LeadStatus s) => s switch
    {
        LeadStatus.New => "stage--new",                     // blue
        LeadStatus.Contacted => "stage--contacted",         // orange
        LeadStatus.Interested => "stage--interested",       // green
        LeadStatus.FollowUp => "stage--followup",           // purple
        LeadStatus.DemoScheduled => "stage--demo",          // teal
        LeadStatus.PaymentPending => "stage--payment",      // amber
        LeadStatus.Converted => "stage--converted",         // dark green
        LeadStatus.Lost => "stage--lost",                   // red
        _ => "stage--new"
    };
}

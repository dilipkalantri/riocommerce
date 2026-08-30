using RioCommerce.Core.Enums;

namespace RioCommerce.Core.DTOs.Reporting;

/// <summary>The eight reports in the Reports section. Also the route key — <c>/admin/reports/{slug}</c>.</summary>
public enum ReportType
{
    Sales,
    Gst,
    Faculty,
    Franchisee,
    ProductSubject,
    FranchiseeReInvoice,
    Shipping,
    TeacherSettlement
}

/// <summary>
/// The one filter every report shares. Each report reads only the fields that apply to it and
/// ignores the rest, which is what lets a single filter bar and a single query pipeline drive all
/// eight.
///
/// <para><b>Every list means "any of these", and an EMPTY list means "All".</b> That convention is
/// what makes the All option free on every multi-select — there is no separate "all" sentinel to
/// keep in sync, and a filter nobody touched simply does not narrow anything.</para>
///
/// <para>All filters compose: supplying a date range, three faculty, two products and a franchisee
/// returns only the rows satisfying every one of them.</para>
/// </summary>
public class ReportQuery
{
    // ── Date range. Each report applies it to its OWN date field — order date, invoice date,
    //    dispatch date, settlement period — so the same two inputs stay meaningful everywhere. ──
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    // ── Multi-select dimensions. Empty = All. ──
    public List<Guid> FacultyIds { get; set; } = new();
    public List<Guid> ProductIds { get; set; } = new();
    public List<Guid> SubjectIds { get; set; } = new();
    public List<Guid> FranchiseIds { get; set; } = new();

    // ── Status filters. Empty = All. ──
    public List<OrderStatus> OrderStatuses { get; set; } = new();
    public List<PaymentStatus> PaymentStatuses { get; set; } = new();
    public List<PaymentMode> PaymentModes { get; set; } = new();
    public List<OrderSource> Sources { get; set; } = new();
    public List<ShipmentStatus> ShippingStatuses { get; set; } = new();
    public List<TeacherSettlementStatus> SettlementStatuses { get; set; } = new();
    public List<ReInvoiceStatus> ReInvoiceStatuses { get; set; } = new();

    /// <summary>Courier partner, exact match. Shipping report only.</summary>
    public string? Courier { get; set; }

    /// <summary>Free text across the identifying columns of whichever report is running — order
    /// number, student name, email, phone, invoice number, product, faculty, franchisee, AWB.
    /// Combines with every other filter rather than replacing them.</summary>
    public string? Search { get; set; }

    /// <summary>Column key to sort on. Each report declares which keys it accepts and falls back
    /// to its own default when given one it does not recognise.</summary>
    public string? SortBy { get; set; }
    public bool SortDesc { get; set; } = true;

    public int Page { get; set; } = 1;

    /// <summary>Rows per page. <b>Zero or less means unpaged</b> — which is how export runs, so a
    /// download contains the whole filtered result rather than the page on screen (§21, §22, §36.7).</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>Server-resolved franchise scope. Set only for franchise-portal callers, never from
    /// the query string — it overrides <see cref="FranchiseIds"/> so a franchisee cannot widen
    /// their own scope by editing a URL.</summary>
    public Guid? FranchiseScopeId { get; set; }

    public bool IsPaged => PageSize > 0;

    /// <summary>Rows to skip. Guards against a page number below 1 arriving from a hand-edited URL.</summary>
    public int Skip => IsPaged ? Math.Max(0, Page - 1) * PageSize : 0;

    /// <summary>True when nothing at all has been narrowed — used to render the Clear Filters
    /// button as inactive rather than making the user guess whether it did anything.</summary>
    public bool IsEmpty =>
        From is null && To is null
        && FacultyIds.Count == 0 && ProductIds.Count == 0
        && SubjectIds.Count == 0 && FranchiseIds.Count == 0
        && OrderStatuses.Count == 0 && PaymentStatuses.Count == 0
        && PaymentModes.Count == 0 && Sources.Count == 0
        && ShippingStatuses.Count == 0 && SettlementStatuses.Count == 0
        && ReInvoiceStatuses.Count == 0
        && string.IsNullOrWhiteSpace(Courier)
        && string.IsNullOrWhiteSpace(Search);

    /// <summary>A copy with paging removed — what the Excel and CSV exports run, so the file
    /// reflects the filters rather than the current page.</summary>
    public ReportQuery ForExport() => new()
    {
        From = From,
        To = To,
        FacultyIds = new List<Guid>(FacultyIds),
        ProductIds = new List<Guid>(ProductIds),
        SubjectIds = new List<Guid>(SubjectIds),
        FranchiseIds = new List<Guid>(FranchiseIds),
        OrderStatuses = new List<OrderStatus>(OrderStatuses),
        PaymentStatuses = new List<PaymentStatus>(PaymentStatuses),
        PaymentModes = new List<PaymentMode>(PaymentModes),
        Sources = new List<OrderSource>(Sources),
        ShippingStatuses = new List<ShipmentStatus>(ShippingStatuses),
        SettlementStatuses = new List<TeacherSettlementStatus>(SettlementStatuses),
        ReInvoiceStatuses = new List<ReInvoiceStatus>(ReInvoiceStatuses),
        Courier = Courier,
        Search = Search,
        SortBy = SortBy,
        SortDesc = SortDesc,
        Page = 1,
        PageSize = 0,
        FranchiseScopeId = FranchiseScopeId
    };
}

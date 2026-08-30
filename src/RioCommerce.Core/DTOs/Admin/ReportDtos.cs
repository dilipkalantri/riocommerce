using RioCommerce.Core.DTOs.Meta;
using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Admin;

public record RecentOrder(string OrderNumber, string StudentName, string ProductSummary, decimal Amount, OrderStatus Status);

/// <summary>
/// One faculty's earnings for a period. <paramref name="Earnings"/> is the BARE share — the taxable
/// value of the faculty's own supply — so it stays comparable across registered and unregistered
/// faculty and is the figure that belongs in a revenue-cost total. <paramref name="GstOnShare"/> is
/// the tax a registered faculty additionally invoices (recoverable as input tax credit), and
/// <paramref name="TotalPayout"/> is the cash that actually leaves.
/// </summary>
public record FacultyPayout(
    Guid FacultyId, string FacultyName, int Orders, decimal Earnings,
    decimal GstOnShare = 0m, decimal TotalPayout = 0m);

public class DashboardData
{
    public decimal RevenueToday { get; set; }
    public int OrdersToday { get; set; }
    public int PendingOrders { get; set; }
    public int ActiveStudents { get; set; }
    public List<RecentOrder> RecentOrders { get; set; } = new();
    public List<FacultyPayout> FacultyEarnings { get; set; } = new();
}

/// <summary>
/// One faculty sharing rule as the admin list shows it. Init-only properties rather than a
/// positional record: the row grew a GST decomposition and an effective window, and a positional
/// record would make every future column a breaking change at each construction site.
/// </summary>
public record SharingRuleRow
{
    public Guid Id { get; init; }
    public Guid ProductId { get; init; }
    public Guid FacultyId { get; init; }
    public string ProductTitle { get; init; } = string.Empty;
    public string? Sku { get; init; }
    public CourseLevel Level { get; init; }
    public string FacultyName { get; init; } = string.Empty;
    public string FacultyShortCode { get; init; } = string.Empty;

    public SharingType ShareType { get; init; }
    public decimal ShareValue { get; init; }

    /// <summary>Regular selling price, for context next to the effective figure.</summary>
    public decimal ProductPrice { get; init; }

    /// <summary>The price the share is actually computed on — special when active and permitted.</summary>
    public decimal EffectivePrice { get; init; }

    public decimal GstRate { get; init; }

    /// <summary>Step 1 — the pre-GST value inside <see cref="EffectivePrice"/>.</summary>
    public decimal TaxableBase { get; init; }

    /// <summary>Step 2 — the bare share per unit. This is what <c>EffectiveAmount</c> used to hold,
    /// except that column was computed on the GST-inclusive gross and never refreshed.</summary>
    public decimal EffectiveAmount { get; init; }

    /// <summary>Step 3 — GST on the share; zero when the faculty is not registered.</summary>
    public decimal GstOnShare { get; init; }

    /// <summary>Total per-unit payout = <see cref="EffectiveAmount"/> + <see cref="GstOnShare"/>.</summary>
    public decimal TotalPayout { get; init; }

    public bool FacultyIsGstRegistered { get; init; }

    /// <summary>This rule's share as a percentage of the taxable base — comparable across
    /// percentage and fixed ₹ rules.</summary>
    public decimal SharePctOfBase { get; init; }

    /// <summary>The product's COMBINED share across all its faculty. Shown on every row so an admin
    /// scanning the list can see when a product is over-allocated without opening it.</summary>
    public decimal ProductTotalSharePct { get; init; }

    public bool ProductExceedsCap { get; init; }

    /// <summary>True when the product's combined share hit the taxable-base cap and this line was
    /// scaled down.</summary>
    public bool WasCapped { get; init; }

    public bool IsActive { get; init; }

    /// <summary>False when the rule row exists but its effective window has not opened yet or has
    /// already closed — configured, but not currently earning.</summary>
    public bool IsInEffect { get; init; }

    public DateTime? EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public string? Notes { get; init; }
}

public class SharingRuleEdit
{
    public Guid? Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid FacultyId { get; set; }
    public SharingType ShareType { get; set; } = SharingType.Percentage;
    public decimal ShareValue { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string? Notes { get; set; }
}

public record SharingOptions(List<IdName> Products, List<IdName> Faculty);

using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Franchise;

// Row shown in the admin "Set commissions for franchise X" editor — one row per Active product.
public class FranchiseCommissionRow
{
    public Guid ProductId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal SellingPrice { get; set; }
    public bool DefaultShareEnabled { get; set; }
    public CommissionType DefaultShareType { get; set; } = CommissionType.Percent;
    public decimal DefaultShareValue { get; set; }
    public bool HasRule { get; set; }
    public CommissionType Type { get; set; } = CommissionType.Percent;
    public decimal Value { get; set; }
}

public class SetCommissionRequest
{
    public Guid FranchiseId { get; set; }
    public Guid ProductId { get; set; }
    public CommissionType Type { get; set; }
    public decimal Value { get; set; }
}

public class BulkSetCommissionRequest
{
    public Guid FranchiseId { get; set; }
    public List<Guid> ProductIds { get; set; } = new();
    public CommissionType Type { get; set; }
    public decimal Value { get; set; }
}

/// <summary>
/// Assigns one product↔commission rule across MANY franchisees at once — selected franchisees,
/// or every franchisee when <see cref="AllFranchisees"/> is true. Covers requirement: "assign
/// products to multiple franchisees" and "to all franchisees at once with default share values".
/// Combined with <see cref="ProductIds"/> it sets the same Type+Value for each (franchise, product)
/// pair in one operation.
/// </summary>
public class BulkAssignAcrossFranchiseesRequest
{
    /// <summary>Target franchisees. Ignored when <see cref="AllFranchisees"/> is true.</summary>
    public List<Guid> FranchiseIds { get; set; } = new();
    /// <summary>When true, applies to every active franchisee (FranchiseIds is ignored).</summary>
    public bool AllFranchisees { get; set; }
    /// <summary>Products to assign. At least one required.</summary>
    public List<Guid> ProductIds { get; set; } = new();
    public CommissionType Type { get; set; }
    public decimal Value { get; set; }
}

// One row per earned commission (per order item).
public class FranchiseEarningRow
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public string ProductTitle { get; set; } = string.Empty;
    public decimal BaseAmount { get; set; }
    public CommissionType Type { get; set; }
    public decimal Value { get; set; }
    public decimal CommissionAmount { get; set; }
    public DateTime EarnedAt { get; set; }
}

public record FranchiseEarningsSummary(int OrderCount, decimal TotalEarnings, decimal MtdEarnings, decimal LastMonthEarnings);

/// <summary>
/// One existing franchise↔product assignment (a FranchiseCommission row), shown in the
/// Assigned Products management grid on the bulk-assign page.
/// </summary>
public class FranchiseAssignmentRow
{
    public Guid FranchiseId { get; set; }
    public Guid ProductId { get; set; }
    public string FranchiseName { get; set; } = string.Empty;
    public string FranchiseCode { get; set; } = string.Empty;
    public string ProductTitle { get; set; } = string.Empty;
    public CommissionType Type { get; set; } = CommissionType.Percent;
    /// <summary>The assigned share value on this franchise↔product row.</summary>
    public decimal AssignedValue { get; set; }
    /// <summary>The product's own default share (for comparison). Null when the product has none.</summary>
    public bool ProductDefaultEnabled { get; set; }
    public CommissionType ProductDefaultType { get; set; } = CommissionType.Percent;
    public decimal ProductDefaultValue { get; set; }
    /// <summary>"Custom" when the assigned value differs from the product default, else "Default".</summary>
    public bool IsCustom { get; set; }
    public bool IsActive { get; set; }
    public DateTime AssignedAt { get; set; }
}

public class UpdateAssignmentRequest
{
    public Guid FranchiseId { get; set; }
    public Guid ProductId { get; set; }
    public CommissionType Type { get; set; } = CommissionType.Percent;
    public decimal Value { get; set; }
}

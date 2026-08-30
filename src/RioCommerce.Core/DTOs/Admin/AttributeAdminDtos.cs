using RioCommerce.Core.Enums;

namespace RioCommerce.Core.DTOs.Admin;

// ─────────────── Product Attributes ───────────────

public record ProductAttributeAdminItem(
    Guid Id, string Name, string? Description, int PredefinedValueCount, int UsedByProductCount);

public class ProductAttributeEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<PredefinedValueEditModel> PredefinedValues { get; set; } = new();
}

public class PredefinedValueEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal PriceAdjustment { get; set; }
    public bool PriceAdjustmentUsePercentage { get; set; }
    public bool IsPreSelected { get; set; }
    public int DisplayOrder { get; set; }
}

// ─────────────── Specification Attributes ───────────────

public record SpecificationAttributeAdminItem(
    Guid Id, string Name, string? GroupName, int OptionCount, int DisplayOrder);

public class SpecificationAttributeEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public Guid? SpecificationAttributeGroupId { get; set; }
    public List<SpecOptionEditModel> Options { get; set; } = new();
}

public class SpecOptionEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ColorSquaresRgb { get; set; }
    public int DisplayOrder { get; set; }
}

public record SpecAttributeGroupItem(Guid Id, string Name, int DisplayOrder, int AttributeCount);

public class SpecAttributeGroupEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

// ─────────────── Checkout Attributes ───────────────

public record CheckoutAttributeAdminItem(
    Guid Id, string Name, AttributeControlType ControlType, bool IsRequired, bool IsActive,
    int ValueCount, int DisplayOrder);

public class CheckoutAttributeEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? TextPrompt { get; set; }
    public bool IsRequired { get; set; }
    public AttributeControlType ControlType { get; set; } = AttributeControlType.DropdownList;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<CheckoutValueEditModel> Values { get; set; } = new();
}

public class CheckoutValueEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal PriceAdjustment { get; set; }
    public bool PriceAdjustmentUsePercentage { get; set; }
    public bool IsPreSelected { get; set; }
    public int DisplayOrder { get; set; }
}

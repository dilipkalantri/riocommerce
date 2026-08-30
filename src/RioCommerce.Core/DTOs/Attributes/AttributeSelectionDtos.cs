namespace RioCommerce.Core.DTOs.Attributes;

// Raw selection posted from the UI. The server resolves names + price adjustments from the DB — never trusting client amounts.
public record AttributeSelection(Guid MappingId, Guid? ValueId, string? TextValue = null);

public record CheckoutAttributeSelection(Guid AttributeId, Guid? ValueId, string? TextValue = null);

// Resolved + serialized into *.SelectedAttributesJson / Order.CheckoutAttributesJson for display and order records.
public record SelectedAttribute(
    string AttributeName, string? ValueName, string? TextValue, decimal PriceAdjustment);

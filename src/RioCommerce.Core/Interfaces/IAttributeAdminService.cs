using RioCommerce.Core.DTOs.Admin;
namespace RioCommerce.Core.Interfaces;

public interface IAttributeAdminService
{
    // Product attributes (+ predefined values)
    Task<List<ProductAttributeAdminItem>> ListProductAttributesAsync();
    Task<ProductAttributeEditModel?> GetProductAttributeAsync(Guid id);
    Task<(bool ok, string? error, Guid id)> SaveProductAttributeAsync(ProductAttributeEditModel model);
    Task<(bool ok, string? error)> DeleteProductAttributeAsync(Guid id);

    // Specification attributes (+ options) and their groups
    Task<List<SpecificationAttributeAdminItem>> ListSpecificationAttributesAsync();
    Task<SpecificationAttributeEditModel?> GetSpecificationAttributeAsync(Guid id);
    Task<(bool ok, string? error, Guid id)> SaveSpecificationAttributeAsync(SpecificationAttributeEditModel model);
    Task<(bool ok, string? error)> DeleteSpecificationAttributeAsync(Guid id);

    Task<List<SpecAttributeGroupItem>> ListSpecGroupsAsync();
    Task<(bool ok, string? error, Guid id)> SaveSpecGroupAsync(SpecAttributeGroupEditModel model);
    Task<(bool ok, string? error)> DeleteSpecGroupAsync(Guid id);

    // Checkout attributes (+ values)
    Task<List<CheckoutAttributeAdminItem>> ListCheckoutAttributesAsync();
    Task<CheckoutAttributeEditModel?> GetCheckoutAttributeAsync(Guid id);
    Task<(bool ok, string? error, Guid id)> SaveCheckoutAttributeAsync(CheckoutAttributeEditModel model);
    Task ToggleCheckoutAttributeAsync(Guid id);
    Task<(bool ok, string? error)> DeleteCheckoutAttributeAsync(Guid id);
}

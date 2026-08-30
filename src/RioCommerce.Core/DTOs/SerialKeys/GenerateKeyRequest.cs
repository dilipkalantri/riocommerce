using RioCommerce.Core.Entities;

namespace RioCommerce.Core.DTOs.SerialKeys;

/// <summary>
/// Provider-agnostic input. The orchestrator (<c>SerialKeyService</c>) builds this from
/// the order + product config, then hands it to whichever <c>ISerialKeyProvider</c> matches
/// <see cref="ProviderKey"/>. The provider is responsible for mapping it into its own
/// request shape (Rio's wrapped <c>{ "Entity": {...} }</c>, Valence's whatever).
/// </summary>
public class GenerateKeyRequest
{
    public string ProviderKey { get; set; } = string.Empty;

    // Customer identity — the provider may need to sign the user up first (Rio does).
    public Guid? UserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }

    /// <summary>International calling code without the '+' (e.g. "91"). Used by registration-style
    /// providers (Superclass) that take country_code + mobile as separate fields. Ignored by
    /// key-issuing providers (RioPlay, Valence). Defaults to "91" at enqueue when not otherwise known.</summary>
    public string? CustomerCountryCode { get; set; }

    /// <summary>Customer postal/PIN code, mapped from the order's billing (or shipping) address.
    /// Sent by Superclass as <c>pincode</c>; ignored by RioPlay/Valence.</summary>
    public string? CustomerPincode { get; set; }

    // Order identity. SerialOrderId is the int sequence used by providers that want an Int32 owner id (Rio).
    public Guid OrderId { get; set; }
    public Guid OrderItemId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int SerialOrderId { get; set; }

    // Product identity.
    public Guid ProductId { get; set; }
    public string ProductTitle { get; set; } = string.Empty;
    /// <summary>Provider's own product code (Rio's Int32 product id). The provider parses as needed.</summary>
    public string? ProviderProductCode { get; set; }

    /// <summary>Quantity of keys to request for this line item.</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>Provider-side tenant routing (Rio's TenantId as a string). Provider resolves this to credentials.</summary>
    public string? TenantRef { get; set; }

    /// <summary>If <c>true</c>, provider should create and activate in one call (Rio's <c>GenerateSerialKeyForNop</c>).</summary>
    public bool AutoActivate { get; set; } = true;

    /// <summary>Opaque provider-specific JSON config (deserialised inside the provider into its typed POCO).</summary>
    public string ConfigJson { get; set; } = "{}";
}

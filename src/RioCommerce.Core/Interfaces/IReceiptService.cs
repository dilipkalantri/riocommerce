using RioCommerce.Core.DTOs.Orders;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Order-wise RECEIPT generation. Unlike invoices (paid orders only), a receipt is available for
/// EVERY order in any status, showing full order, taxation and — for franchise orders — the
/// franchisee/company bifurcation.
/// </summary>
public interface IReceiptService
{
    /// <summary>Builds the receipt model for an order (any status). Null if the order isn't found.</summary>
    Task<ReceiptDetail?> GetAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>Renders the receipt PDF. Null if the order isn't found.</summary>
    Task<(byte[] bytes, string filename)?> RenderPdfAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>Computes the franchisee/company bifurcation for an order (franchise orders only).
    /// Used by receipts, invoices and the franchisee financial-bifurcation view.</summary>
    Task<FranchiseBifurcation?> GetBifurcationAsync(Guid orderId, CancellationToken ct = default);
}

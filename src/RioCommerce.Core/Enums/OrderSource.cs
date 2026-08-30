namespace RioCommerce.Core.Enums;

public enum OrderSource
{
    Website,
    Counter,
    Franchisee,
    Other,
    /// <summary>Money added to a franchisee's wallet, recorded as an order so it flows through the
    /// ordinary invoice pipeline. NOT a sale — excluded from every order list and revenue figure
    /// via <c>OrderQueryExtensions.ExcludeWalletTopUps()</c>.</summary>
    WalletTopUp
}

using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Shared order-query filters. Kept in one place on purpose: a wallet top-up is stored as a real
/// order so it can be invoiced through the normal pipeline, which means EVERY order list and
/// revenue figure has to leave it out — otherwise the same money is counted twice (once when the
/// franchisee funds the wallet, again when they spend it on courses).
/// </summary>
public static class OrderQueryExtensions
{
    /// <summary>Drops wallet top-up orders. Apply to every order list, count and revenue sum.
    /// Do NOT apply on invoice queries — a top-up invoice is a real invoice and belongs there.</summary>
    public static IQueryable<Order> ExcludeWalletTopUps(this IQueryable<Order> q)
        => q.Where(o => o.Source != OrderSource.WalletTopUp);

    /// <summary>In-memory counterpart, for code that has already materialised orders.</summary>
    public static IEnumerable<Order> ExcludeWalletTopUps(this IEnumerable<Order> rows)
        => rows.Where(o => o.Source != OrderSource.WalletTopUp);

    /// <summary>True for the synthetic order that records money being added to a franchise wallet.</summary>
    public static bool IsWalletTopUp(this Order o) => o.Source == OrderSource.WalletTopUp;

    /// <summary>
    /// Orders that produced a tax invoice — what a GST return must reflect. This is deliberately
    /// NOT <see cref="ExcludeWalletTopUps"/>: the two filters are opposites by design.
    /// <list type="bullet">
    ///   <item>Wallet top-ups are KEPT — GST is charged when the money comes in.</item>
    ///   <item>Wallet-funded course orders are DROPPED — no invoice is raised for those, because
    ///         the tax was already collected on the top-up that funded them.</item>
    ///   <item>Gateway and website orders are kept, exactly as before.</item>
    /// </list>
    /// Using the sales filter here would under-report output tax; using no filter would double it.
    /// </summary>
    public static IQueryable<Order> InvoicedForGst(this IQueryable<Order> q)
        => q.Where(o => !o.PaidFromWallet);
}

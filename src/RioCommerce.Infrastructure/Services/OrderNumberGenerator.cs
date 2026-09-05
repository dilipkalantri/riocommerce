using Microsoft.EntityFrameworkCore;
using RioCommerce.Core.Entities;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Allocates the next order number for a configurable series (e.g. "VJP-" → VJP-01, VJP-02 …).
///
/// <para>One implementation for every order source. Checkout, the admin counter and the school
/// portal each had their own near-identical copy hard-coding "RIO", so changing the prefix meant
/// finding all three and they could silently drift apart.</para>
///
/// <para>The sequence is scoped to the series in force, which gives the same two properties the
/// invoice series has: changing the prefix never rewrites an issued order number, and the new
/// series simply starts again at 1 while the old numbers stay exactly as they were.</para>
///
/// <para>Concurrency: callers already handle a duplicate by retrying, and the order number is
/// unique in the database, so a race loses at the insert rather than producing a duplicate.</para>
/// </summary>
public static class OrderNumberGenerator
{
    /// <summary>Used when no series is configured, so an untouched deployment keeps its numbering.</summary>
    public const string LegacyPrefix = "RIO-";

    /// <summary>Where the legacy RIO series started; preserved so existing installs don't jump.</summary>
    private const int LegacySeed = 1043;

    /// <summary>
    /// Next number in <paramref name="series"/>. Pass the caller's own <c>Orders</c> queryable so
    /// each keeps its own filtering (the admin path, for instance, ignores the soft-delete filter
    /// and must still see deleted orders' numbers as taken).
    /// </summary>
    public static async Task<string> NextAsync(
        IQueryable<Order> orders, string? series, CancellationToken ct = default)
    {
        var configured = !string.IsNullOrWhiteSpace(series);
        var prefix = configured ? series!.Trim() : LegacyPrefix;

        var taken = await orders
            .Where(o => o.OrderNumber.StartsWith(prefix))
            .Select(o => o.OrderNumber)
            .ToListAsync(ct);

        // Seed only matters when the series has no numbers yet.
        var next = configured ? 1 : LegacySeed;
        foreach (var num in taken)
        {
            // Re-checked ordinally: the database StartsWith goes through LIKE, so a prefix
            // containing a wildcard character could otherwise widen the match.
            if (!num.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var tail = num[prefix.Length..];
            if (int.TryParse(tail, out var n) && n >= next) next = n + 1;
        }

        // D2 so a fresh series reads VJP-01, not VJP-1. Longer numbers are unaffected.
        return prefix + next.ToString("D2");
    }
}

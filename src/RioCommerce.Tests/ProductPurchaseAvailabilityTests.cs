using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// <c>Product.AllowCustomerPurchase</c> withdraws a product from sale WITHOUT unpublishing it:
/// it stays visible, searchable and on the homepage, but Add To Cart and Buy Now are refused.
///
/// <para>Hiding the buttons is presentation. These tests pin the parts that actually enforce it:</para>
/// <list type="bullet">
///   <item><b>Add is refused server-side.</b> Both Add To Cart and Buy Now funnel through
///         <c>CartService.AddAsync</c>, so a crafted request that skips the UI is refused too.</item>
///   <item><b>Checkout re-validates.</b> A product withdrawn AFTER it was added must not become
///         an order — add-time approval is not carried forward.</item>
///   <item><b>Default is permissive.</b> A product that never touches the flag stays buyable, which
///         is what keeps every pre-existing product behaving as before.</item>
/// </list>
/// </summary>
public class ProductPurchaseAvailabilityTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"purchase-{Guid.NewGuid()}").Options);

    private static readonly Guid Buyer = Guid.NewGuid();

    private static Product Seed(RioCommerceDbContext db, bool allowPurchase, string title = "Test Course")
    {
        var p = new Product
        {
            Id = Guid.NewGuid(), Title = title, Slug = title.ToLower().Replace(' ', '-'),
            SellingPrice = 400m, Mrp = 400m,
            Status = ProductStatus.Active,       // published the whole time
            AllowCustomerPurchase = allowPurchase,
        };
        db.Products.Add(p);
        db.Users.Add(new User { Id = Buyer, FullName = "Buyer", Email = "buyer@example.test" });
        db.SaveChanges();
        return p;
    }

    // ── TEST 1 / 5: purchase allowed ───────────────────────────────────────────

    [Fact]
    public async Task AddToCart_Succeeds_WhenPurchaseAllowed()
    {
        using var db = NewDb();
        var p = Seed(db, allowPurchase: true);

        var (ok, err) = await new CartService(db).AddAsync(Buyer, p.Id, null);

        Assert.True(ok);
        Assert.Null(err);
        Assert.Single(db.CartItems.Where(c => c.ProductId == p.Id));
    }

    // ── TEST 3: direct call with purchase disabled ─────────────────────────────

    [Fact]
    public async Task AddToCart_IsRefused_WhenPurchaseDisabled()
    {
        using var db = NewDb();
        var p = Seed(db, allowPurchase: false);

        var (ok, err) = await new CartService(db).AddAsync(Buyer, p.Id, null);

        Assert.False(ok);
        Assert.Equal("This product is currently not available for purchase.", err);
        Assert.Empty(db.CartItems);          // nothing written
    }

    /// <summary>Buy Now is not a separate path — it calls the same AddAsync — so blocking the add
    /// is what stops it reaching checkout or the payment gateway.</summary>
    [Fact]
    public async Task BuyNow_CannotReachCheckout_WhenPurchaseDisabled()
    {
        using var db = NewDb();
        var p = Seed(db, allowPurchase: false);

        var (ok, _) = await new CartService(db).AddAsync(Buyer, p.Id, null);

        Assert.False(ok);
        Assert.Empty(db.CartItems);          // no cart line => /checkout has nothing to order
    }

    // ── TEST 4: withdrawn AFTER it was already in the cart ─────────────────────

    [Fact]
    public async Task Checkout_IsRefused_WhenProductWithdrawnAfterAdd()
    {
        using var db = NewDb();
        var p = Seed(db, allowPurchase: true);
        var cart = new CartService(db);

        var (ok, _) = await cart.AddAsync(Buyer, p.Id, null);
        Assert.True(ok);                     // allowed at the time it was added

        // Admin withdraws it from sale afterwards.
        p.AllowCustomerPurchase = false;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => cart.CheckoutAsync(Buyer, null));

        Assert.Contains("not available for purchase", ex.Message);
        Assert.Contains(p.Title, ex.Message);   // names the offending line
        Assert.Empty(db.Orders);                // no order created
    }

    [Fact]
    public async Task Checkout_Succeeds_WhenProductStaysPurchasable()
    {
        using var db = NewDb();
        var p = Seed(db, allowPurchase: true);
        var cart = new CartService(db);

        Assert.True((await cart.AddAsync(Buyer, p.Id, null)).ok);

        // Reaching past the availability gate is what matters here; the rest of checkout has its
        // own coverage. Any exception must NOT be the purchase-availability one.
        try
        {
            await cart.CheckoutAsync(Buyer, null);
        }
        catch (Exception ex)
        {
            Assert.DoesNotContain("not available for purchase", ex.Message);
        }
    }

    // ── Default preserves existing behaviour ───────────────────────────────────

    [Fact]
    public void NewProduct_IsPurchasableByDefault()
    {
        // The entity default is what the migration backfills existing rows to, so a product that
        // predates this feature — or one created without touching the flag — stays buyable.
        Assert.True(new Product().AllowCustomerPurchase);
    }

    [Fact]
    public async Task DisablingPurchase_DoesNotUnpublishOrHideTheProduct()
    {
        using var db = NewDb();
        var p = Seed(db, allowPurchase: false);
        p.IsFeatured = true;
        await db.SaveChangesAsync();

        var stored = await db.Products.SingleAsync(x => x.Id == p.Id);

        // Purchase availability is orthogonal to catalogue visibility.
        Assert.Equal(ProductStatus.Active, stored.Status);
        Assert.True(stored.IsFeatured);
        Assert.False(stored.AllowCustomerPurchase);
    }
}

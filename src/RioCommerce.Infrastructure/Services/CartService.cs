using System.Text.Json;
using RioCommerce.Core.DTOs.Attributes;
using RioCommerce.Core.DTOs.Cart;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class CartService : ICartService
{
    private readonly RioCommerceDbContext _db;
    public CartService(RioCommerceDbContext db) => _db = db;

    // A franchisee must purchase through the Franchise Portal (counter order), NOT the customer
    // cart/checkout. This is the authoritative server-side guard — the storefront also hides the
    // buy buttons for these users, but blocking here stops a direct API call too. A franchise
    // account is a user who owns an active Franchise row.
    private Task<bool> IsFranchiseUserAsync(Guid userId) =>
        _db.Franchises.AnyAsync(f => f.AdminUserId == userId && f.IsActive);

    private const string FranchiseBlockedMessage =
        "Franchise accounts place orders through the Franchise Portal. Please use Franchise → Place Order.";

    public async Task<int> CountAsync(Guid userId) =>
        await _db.CartItems.Where(c => c.UserId == userId).SumAsync(c => (int?)c.Quantity) ?? 0;

    public async Task<(bool ok, string? error)> AddAsync(Guid userId, Guid productId, Guid? modeId, IReadOnlyList<AttributeSelection>? selections = null, IReadOnlyList<Guid>? selectedOptionIds = null)
    {
        if (await IsFranchiseUserAsync(userId)) return (false, FranchiseBlockedMessage);

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product == null) return (false, "Product not found.");

        // Server-side purchase gate. Both Add To Cart AND Buy Now come through here, so a crafted
        // request cannot add a withdrawn product by skipping the UI — the buttons being hidden is
        // presentation only; THIS is the boundary.
        if (!product.AllowCustomerPurchase) return (false, "This product is currently not available for purchase.");

        // ── Duplicate guard: reject if this product (any mode/attribute combo) is already in the cart. ──
        var alreadyInCart = await _db.CartItems.AnyAsync(c => c.UserId == userId && c.ProductId == productId);
        if (alreadyInCart) return (false, "This product is already in your cart.");

        // Special price overrides everything (including mode prices) when active.
        var basePrice = product.IsSpecialPriceActive
            ? product.SpecialPrice!.Value
            : modeId.HasValue
                ? await _db.ProductModes.Where(m => m.Id == modeId).Select(m => (decimal?)m.Price).FirstOrDefaultAsync() ?? product.SellingPrice
                : product.SellingPrice;
        var (adjustment, json) = await ResolveAttributesAsync(productId, basePrice, selections);

        _db.CartItems.Add(new CartItem
        {
            UserId = userId, ProductId = productId, ProductModeId = modeId, Quantity = 1,
            SelectedAttributesJson = json, AttributePriceAdjustment = adjustment,
            SelectedOptionIdsJson = (selectedOptionIds is { Count: > 0 })
                ? JsonSerializer.Serialize(selectedOptionIds)
                : null
        });
        await _db.SaveChangesAsync();
        return (true, null);
    }

    // Resolves raw UI selections into a price adjustment + a serialized snapshot, all server-side (never trust client amounts).
    private async Task<(decimal adjustment, string? json)> ResolveAttributesAsync(
        Guid productId, decimal basePrice, IReadOnlyList<AttributeSelection>? selections)
    {
        if (selections == null || selections.Count == 0) return (0m, null);

        var mappingIds = selections.Select(s => s.MappingId).Distinct().ToList();
        var mappings = await _db.ProductAttributeMappings
            .Where(m => m.ProductId == productId && mappingIds.Contains(m.Id))
            .Include(m => m.ProductAttribute)
            .Include(m => m.Values)
            .ToListAsync();

        var resolved = new List<SelectedAttribute>();
        decimal total = 0m;
        foreach (var sel in selections)
        {
            var m = mappings.FirstOrDefault(x => x.Id == sel.MappingId);
            if (m == null) continue;
            if (sel.ValueId is Guid vid)
            {
                var v = m.Values.FirstOrDefault(x => x.Id == vid);
                if (v == null) continue;
                var adj = v.PriceAdjustmentUsePercentage ? Math.Round(basePrice * v.PriceAdjustment / 100m, 2) : v.PriceAdjustment;
                total += adj;
                resolved.Add(new SelectedAttribute(m.ProductAttribute.Name, v.Name, null, adj));
            }
            else if (!string.IsNullOrWhiteSpace(sel.TextValue))
            {
                resolved.Add(new SelectedAttribute(m.ProductAttribute.Name, null, sel.TextValue.Trim(), 0m));
            }
        }

        if (resolved.Count == 0) return (0m, null);
        return (total, JsonSerializer.Serialize(resolved));
    }

    // Builds a short "Attribute: value" summary from the stored JSON for display.
    internal static string? AttributeSummary(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var sel = JsonSerializer.Deserialize<List<SelectedAttribute>>(json);
            if (sel == null || sel.Count == 0) return null;
            return string.Join(", ", sel.Select(s => $"{s.AttributeName}: {s.ValueName ?? s.TextValue}"));
        }
        catch { return null; }
    }

    // Resolve a cart item's selected purchase-option IDs → (add-on sum, snapshot JSON).
    // SERVER-SIDE lookup only — never trust a client amount. Validates each id belongs to an active
    // group of THIS product; ignores foreign ids (tamper-safe); one pick honoured per group.
    private static (decimal addOn, string? snapshot) CartOptionResolve(CartItem c)
    {
        if (string.IsNullOrWhiteSpace(c.SelectedOptionIdsJson)) return (0m, null);
        List<Guid> ids;
        try { ids = JsonSerializer.Deserialize<List<Guid>>(c.SelectedOptionIdsJson!) ?? new(); }
        catch (JsonException) { return (0m, null); }
        if (ids.Count == 0 || c.Product?.OptionGroups == null) return (0m, null);

        decimal sum = 0m;
        var snap = new List<object>();
        foreach (var g in c.Product.OptionGroups.Where(g => g.IsActive))
        {
            var picked = g.Items.FirstOrDefault(i => i.IsActive && ids.Contains(i.Id));
            if (picked == null) continue;
            sum += picked.PriceAddOn;
            snap.Add(new { groupName = g.Name, optionName = picked.Name, addOn = picked.PriceAddOn });
        }
        return (sum, snap.Count == 0 ? null : JsonSerializer.Serialize(snap));
    }

    private static decimal CartOptionAddOn(CartItem c) => CartOptionResolve(c).addOn;

    public async Task RemoveAsync(Guid userId, Guid cartItemId)
    {
        var item = await _db.CartItems.FirstOrDefaultAsync(c => c.Id == cartItemId && c.UserId == userId);
        if (item != null) { _db.CartItems.Remove(item); await _db.SaveChangesAsync(); }
    }

    // Quantity is bounded server-side so the client can't smuggle in absurd values; ≤0 just removes the line.
    public const int MaxCartLineQuantity = 10;
    public async Task<(bool ok, string? error)> UpdateQuantityAsync(Guid userId, Guid cartItemId, int newQuantity)
    {
        var item = await _db.CartItems.FirstOrDefaultAsync(c => c.Id == cartItemId && c.UserId == userId);
        if (item == null) return (false, "Item not found in your cart.");
        if (newQuantity <= 0) { _db.CartItems.Remove(item); await _db.SaveChangesAsync(); return (true, null); }
        if (newQuantity > MaxCartLineQuantity) return (false, $"Maximum quantity is {MaxCartLineQuantity}.");
        if (item.Quantity == newQuantity) return (true, null);
        item.Quantity = newQuantity;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<CartView> GetAsync(Guid userId, string? couponCode = null)
    {
        var rows = await _db.CartItems.Where(c => c.UserId == userId)
            .Include(c => c.Product).ThenInclude(p => p.PrimaryFaculty)
            .Include(c => c.Product).ThenInclude(p => p.OptionGroups).ThenInclude(g => g.Items)
            .Include(c => c.ProductMode)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();

        var view = new CartView();
        foreach (var c in rows)
        {
            // Mode price is ADDITIVE — an add-on to the regular (or special) price.
            // Special price discounts the base; the mode add-on still applies on top of it.
            var basePrice = c.Product.IsSpecialPriceActive ? c.Product.SpecialPrice!.Value : c.Product.SellingPrice;
            var unit = basePrice + (c.ProductMode?.Price ?? 0m) + CartOptionAddOn(c) + c.AttributePriceAdjustment;
            view.Items.Add(new CartItemView(c.Id, c.ProductId, c.Product.Slug, c.Product.Title, c.Product.Level,
                c.Product.PrimaryFaculty?.DisplayName, c.ProductMode?.ModeName, unit, c.Product.Mrp, c.Quantity,
                AttributeSummary(c.SelectedAttributesJson), c.Product.BatchStatus,
                c.Product.LectureAccessTiming, c.Product.NotesDispatchTimeline));
        }

        view.Subtotal = view.Items.Sum(i => i.UnitPrice * i.Quantity);
        var productSavings = view.Items.Sum(i => Math.Max(0, i.Mrp - i.UnitPrice) * i.Quantity);

        view.Discount = await ResolveCouponAsync(couponCode, view.Subtotal, view);

        view.Total = view.Subtotal - view.Discount;
        view.GstIncluded = Math.Round(view.Total * 18m / 118m, 2);   // prices are GST-inclusive
        view.Savings = productSavings + view.Discount;
        return view;
    }

    public async Task<string> CheckoutAsync(Guid userId, string? couponCode)
    {
        if (await IsFranchiseUserAsync(userId))
            throw new InvalidOperationException(FranchiseBlockedMessage);

        var rows = await _db.CartItems.Where(c => c.UserId == userId)
            .Include(c => c.Product).ThenInclude(p => p.OptionGroups).ThenInclude(g => g.Items)
            .Include(c => c.Product).Include(c => c.ProductMode)
            .ToListAsync();
        if (rows.Count == 0) throw new InvalidOperationException("Your cart is empty.");

        // A product can be withdrawn from sale AFTER it was added to a cart, so availability is
        // re-checked here rather than trusted from add-time.
        var blocked = rows.Where(r => !r.Product.AllowCustomerPurchase).Select(r => r.Product.Title).ToList();
        if (blocked.Count > 0)
            throw new InvalidOperationException(
                $"{string.Join(", ", blocked)} is currently not available for purchase. Please remove it from your cart to continue.");

        var summary = await GetAsync(userId, couponCode);
        var user = await _db.Users.FirstAsync(u => u.Id == userId);
        var orderNo = $"RIO-{1042 + await _db.Orders.CountAsync()}";

        var order = new Order
        {
            OrderNumber = orderNo,
            UserId = userId,
            StudentName = user.FullName,
            StudentPhone = user.Phone ?? "",
            StudentEmail = user.Email,
            StudentCity = user.City,
            Source = OrderSource.Website,
            Subtotal = summary.Subtotal,
            DiscountAmount = summary.Discount,
            CouponCode = summary.CouponCode,
            GstAmount = summary.GstIncluded,
            TotalAmount = summary.Total,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending
        };
        foreach (var c in rows)
        {
            // Mode price is ADDITIVE (add-on), not replacement. Purchase-option add-ons stack too.
            var basePrice = c.Product.IsSpecialPriceActive ? c.Product.SpecialPrice!.Value : c.Product.SellingPrice;
            var (optAddOn, optSnapshot) = CartOptionResolve(c);
            var unit = basePrice + (c.ProductMode?.Price ?? 0m) + optAddOn + c.AttributePriceAdjustment;
            order.Items.Add(new OrderItem
            {
                ProductId = c.ProductId,
                ProductModeId = c.ProductModeId,
                ProductTitle = c.Product.Title,
                ModeName = c.ProductMode?.ModeName,
                SelectedAttributesJson = c.SelectedAttributesJson,
                SelectedOptionIdsJson = c.SelectedOptionIdsJson,
                SelectedOptionsJson = optSnapshot,
                Quantity = c.Quantity,
                UnitPrice = unit,
                GstRate = c.Product.GstRate,
                LineTotal = unit * c.Quantity
            });
        }

        _db.Orders.Add(order);
        _db.CartItems.RemoveRange(rows);
        await _db.SaveChangesAsync();
        return orderNo;
    }

    private async Task<decimal> ResolveCouponAsync(string? couponCode, decimal subtotal, CartView view)
    {
        if (string.IsNullOrWhiteSpace(couponCode)) return 0;
        var code = couponCode.Trim().ToUpper();
        var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == code && c.IsActive);
        var now = DateTime.UtcNow;

        if (coupon == null) { view.CouponMessage = "Invalid coupon code."; return 0; }
        if ((coupon.StartsAt.HasValue && coupon.StartsAt > now) || (coupon.ExpiresAt.HasValue && coupon.ExpiresAt < now))
        { view.CouponMessage = "This coupon has expired."; return 0; }
        if (subtotal < coupon.MinOrder)
        { view.CouponMessage = $"Minimum order of ₹{coupon.MinOrder:N0} required for this coupon."; return 0; }

        var discount = coupon.CouponType == SharingType.Percentage ? subtotal * coupon.Value / 100m : coupon.Value;
        if (coupon.MaxDiscount.HasValue && discount > coupon.MaxDiscount.Value) discount = coupon.MaxDiscount.Value;
        discount = Math.Round(Math.Min(discount, subtotal), 2);

        view.CouponCode = coupon.Code;
        view.CouponMessage = $"Coupon \"{coupon.Code}\" applied 🎉";
        return discount;
    }
}

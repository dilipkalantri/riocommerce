using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RioCommerce.Core.DTOs.School;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// School Portal course enrolment — see <see cref="ISchoolEnrollmentService"/>.
///
/// Deliberately thin. It owns no catalogue, no pricing table and no enrolment rules of its own:
///   • the course list is a projection of Products where Status == Active;
///   • the price is the product's effective selling price, read at submit time;
///   • the output is an ordinary Order built from the same entities CheckoutService uses;
///   • Enrollment rows are NOT written here — the existing admin activation path grants access
///     once Accounts has settled the payment.
/// </summary>
public class SchoolEnrollmentService(
    RioCommerceDbContext db,
    ISchoolStudentService students,
    ILogger<SchoolEnrollmentService> log) : ISchoolEnrollmentService
{
    private readonly RioCommerceDbContext _db = db;
    private readonly ISchoolStudentService _students = students;
    private readonly ILogger<SchoolEnrollmentService> _log = log;

    // Matches CheckoutService: prices are GST-inclusive and the seller is registered in Maharashtra.
    private const string SellerState = "Maharashtra";
    private const decimal GstDivisor = 118m;
    private const decimal GstNumerator = 18m;

    public async Task<List<SchoolEnrollmentProduct>> ListProductsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // The special-price window is re-expressed here rather than calling Product.EffectiveSellingPrice
        // because that is a computed C# property EF cannot translate. Same expression the franchise
        // catalogue uses, so both portals price a product identically.
        return await _db.Products.AsNoTracking()
            .Where(p => p.Status == ProductStatus.Active)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Title)
            .Select(p => new SchoolEnrollmentProduct(
                p.Id,
                p.Title,
                (p.SpecialPrice != null && p.SpecialPrice > 0
                    && (p.SpecialPriceStartDateUtc == null || now >= p.SpecialPriceStartDateUtc)
                    && (p.SpecialPriceEndDateUtc == null || now <= p.SpecialPriceEndDateUtc))
                    ? p.SpecialPrice!.Value
                    : p.SellingPrice))
            .ToListAsync(ct);
    }

    public async Task<PlaceSchoolEnrollmentResult> PlaceAsync(
        Guid actingUserId, PlaceSchoolEnrollmentRequest request, CancellationToken ct = default)
    {
        // ── 1. Scope. The school comes from the signed-in user, never from the request. ──
        var schoolId = await _students.ResolveSchoolIdAsync(actingUserId, ct);
        if (schoolId is null)
            return new(false, "Your account is not linked to a school.", null, 0, 0m);

        var studentIds = request.StudentUserIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (studentIds.Count == 0)
            return new(false, "Select at least one student to enrol.", null, 0, 0m);

        // ── 2. Every student must be on THIS school's roll. A single stranger fails the lot. ──
        var onRoll = await _db.SchoolStudents.AsNoTracking()
            .Where(ss => ss.SchoolId == schoolId.Value && ss.IsActive && studentIds.Contains(ss.UserId))
            .Select(ss => ss.UserId)
            .ToListAsync(ct);

        if (onRoll.Count != studentIds.Count)
        {
            _log.LogWarning(
                "School enrolment rejected: user {UserId} listed {Asked} students but only {Found} are on school {SchoolId}.",
                actingUserId, studentIds.Count, onRoll.Count, schoolId);
            return new(false, "Some selected students are no longer on your school's roll. Refresh and try again.", null, 0, 0m);
        }

        // ── 3. Price from the catalogue, at submit time. Never from the browser. ──
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.Status == ProductStatus.Active, ct);
        if (product == null)
            return new(false, "That course is no longer available. Pick another and try again.", null, 0, 0m);

        var unit = product.IsSpecialPriceActive ? product.SpecialPrice!.Value : product.SellingPrice;
        var qty = studentIds.Count;
        var total = Math.Round(unit * qty, 2);

        var school = await _db.Schools.AsNoTracking()
            .Include(s => s.State)
            .FirstOrDefaultAsync(s => s.Id == schoolId.Value, ct);
        var principal = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actingUserId, ct);

        var (cgst, sgst, igst) = SplitGst(total, school?.State?.Name);

        // ── 4. One order, unpaid. Written in a transaction so a failure leaves nothing behind. ──
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var order = new Order
            {
                OrderNumber = await GenerateOrderNumberAsync(ct),
                UserId = actingUserId,
                CreatedById = actingUserId,
                StudentName = principal?.FullName ?? school?.Name ?? "School",
                StudentPhone = principal?.Phone ?? string.Empty,
                StudentEmail = principal?.Email,
                StudentCity = school?.CityOrVillage,
                // No OrderSource member describes a school, and adding one would change a persisted
                // enum. Other + SourceNo (documented free text) records the origin without that.
                Source = OrderSource.Other,
                SourceNo = school?.UdiseCode,
                CustomerType = CustomerType.Organization,
                OrgName = school?.Name,
                BillingName = school?.Name,
                BillingAddress = school?.Address,
                BillingCity = school?.CityOrVillage,
                BillingState = school?.State?.Name,
                BillingPincode = school?.PinCode,
                Subtotal = total,
                DiscountAmount = 0m,
                GstAmount = cgst + sgst + igst,
                CgstAmount = cgst,
                SgstAmount = sgst,
                IgstAmount = igst,
                TotalAmount = total,
                Status = OrderStatus.Pending,
                PaymentStatus = PaymentStatus.Pending,
            };

            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductTitle = product.Title,
                Quantity = qty,
                UnitPrice = unit,
                Discount = 0m,
                GstRate = product.GstRate,
                GstAmount = cgst + sgst + igst,
                LineTotal = total,
            });

            // The roster lives on the order as a note so Accounts and Admin can see exactly who the
            // order covers. Activation still grants access through the existing path.
            var names = await _db.SchoolStudents.AsNoTracking()
                .Include(ss => ss.User)
                .Where(ss => ss.SchoolId == schoolId.Value && studentIds.Contains(ss.UserId))
                .OrderBy(ss => ss.User.FullName)
                .Select(ss => ss.User.FullName + (ss.StudentClass == null ? "" : $" ({ss.StudentClass}{ss.Section})"))
                .ToListAsync(ct);

            order.Notes.Add(new OrderNote
            {
                Body = $"School enrolment — {school?.Name ?? "school"} ({qty} student{(qty == 1 ? "" : "s")}):\n"
                     + string.Join("\n", names.Select((n, i) => $"{i + 1}. {n}")),
                IsCustomerVisible = false,
                CreatedByUserId = actingUserId,
                CreatedByName = principal?.FullName ?? "School Portal",
            });

            _db.Orders.Add(order);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _log.LogInformation(
                "School enrolment order {OrderNumber} created for school {SchoolId}: {Qty} x {ProductId} = {Total}.",
                order.OrderNumber, schoolId, qty, product.Id, total);

            return new(true, null, order.OrderNumber, qty, total);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            // The caller gets a neutral message; the detail stays in the log.
            _log.LogError(ex, "School enrolment failed for user {UserId}, school {SchoolId}.", actingUserId, schoolId);
            return new(false, "We could not submit this enrolment. Please try again.", null, 0, 0m);
        }
    }

    /// <summary>Mirrors CheckoutService: prices are GST-inclusive, so tax is extracted, not added.</summary>
    private static (decimal cgst, decimal sgst, decimal igst) SplitGst(decimal grossTotal, string? buyerState)
    {
        var gst = Math.Round(grossTotal * GstNumerator / GstDivisor, 2);
        if (string.Equals(buyerState?.Trim(), SellerState, StringComparison.OrdinalIgnoreCase))
        {
            var half = Math.Round(gst / 2m, 2);
            return (half, gst - half, 0m);   // rounding absorbed into SGST so the parts sum to gst
        }
        return (0m, 0m, gst);
    }

    /// <summary>Same RIO-nnnn sequence CheckoutService issues, so school orders sit in one series.</summary>
    private async Task<string> GenerateOrderNumberAsync(CancellationToken ct)
    {
        var last = await _db.Orders.Where(o => o.OrderNumber.StartsWith("RIO"))
            .OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync(ct);
        var next = 1043;
        if (last != null && int.TryParse(last.OrderNumber.Split('-').Last(), out var n)) next = n + 1;
        return $"RIO-{next}";
    }
}

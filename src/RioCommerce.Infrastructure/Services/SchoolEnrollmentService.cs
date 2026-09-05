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
    IInvoiceService invoices,
    ICompanySettingsService company,
    ILogger<SchoolEnrollmentService> log) : ISchoolEnrollmentService
{
    private readonly RioCommerceDbContext _db = db;
    private readonly ISchoolStudentService _students = students;
    private readonly IInvoiceService _invoices = invoices;
    private readonly ICompanySettingsService _company = company;
    private readonly ILogger<SchoolEnrollmentService> _log = log;

    // Matches CheckoutService: prices are GST-inclusive and the seller is registered in Maharashtra.
    private const string SellerState = "Maharashtra";
    private const decimal GstDivisor = 118m;
    private const decimal GstNumerator = 18m;

    public async Task<List<SchoolEnrollmentProduct>> ListProductsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // The catalogue shown here is ALWAYS priced at the school tier — this endpoint is gated to
        // school Principals / Coordinators, and every order it places is on behalf of enrolled
        // school students. So the projected price is the minimum of (school-student price, active
        // special price, regular price), with only the ones that are actually set considered.
        //
        // Re-expressed in EF-translatable form because Product.EffectivePriceFor is a computed C#
        // method the query provider can't translate. The condition below mirrors it exactly.
        return await _db.Products.AsNoTracking()
            .Where(p => p.Status == ProductStatus.Active)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Title)
            .Select(p => new SchoolEnrollmentProduct(
                p.Id,
                p.Title,
                // Regular effective price (special when active, else selling).
                (p.SchoolStudentPrice != null && p.SchoolStudentPrice > 0
                 && p.SchoolStudentPrice.Value <
                    ((p.SpecialPrice != null && p.SpecialPrice > 0
                      && (p.SpecialPriceStartDateUtc == null || now >= p.SpecialPriceStartDateUtc)
                      && (p.SpecialPriceEndDateUtc == null || now <= p.SpecialPriceEndDateUtc))
                        ? p.SpecialPrice.Value
                        : p.SellingPrice))
                    ? p.SchoolStudentPrice.Value
                    : (p.SpecialPrice != null && p.SpecialPrice > 0
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
        var scope = await _students.ResolveScopeAsync(actingUserId, ct);
        if (scope is null)
            return new(false, "Your account is not linked to a school.", null, 0, 0m);

        // ── 1a. PRINCIPAL ONLY. Enrolment raises an order and commits the school to payment, so it
        //    is not something a coordinator may do — not through the page, and not by posting at the
        //    API directly. The page carries [Authorize(Roles="school_principal")] as well; this is
        //    the gate that does not depend on routing being configured correctly.
        //
        //    IsRestricted is precisely "the caller is a coordinator" (see SchoolAccessScope), read
        //    from the SchoolUsers table rather than from a claim the client could stale-cache.
        if (scope.IsRestricted)
        {
            _log.LogWarning("School enrolment DENIED — coordinator {UserId} attempted to place an enrolment for school {SchoolId}.",
                actingUserId, scope.SchoolId);
            return new(false, "Only the school principal can enrol students and make payments.", null, 0, 0m);
        }

        var schoolId = scope.SchoolId;

        var studentIds = request.StudentUserIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (studentIds.Count == 0)
            return new(false, "Select at least one student to enrol.", null, 0, 0m);

        // ── 2. Every student must be on THIS school's roll AND, for a coordinator, be one they
        //    added. A single stranger fails the lot, so a hand-made request that slips one other
        //    student into the list is rejected outright rather than partially honoured. This is the
        //    check that stops a coordinator enrolling — and therefore billing and invoicing —
        //    another coordinator's or the principal's students. ──
        var onRoll = await _db.SchoolStudents.AsNoTracking()
            .Where(ss => ss.SchoolId == schoolId
                      && ss.IsActive
                      && studentIds.Contains(ss.UserId)
                      && (scope.RestrictToAddedByUserId == null
                          || ss.AddedByUserId == scope.RestrictToAddedByUserId))
            .Select(ss => ss.UserId)
            .ToListAsync(ct);

        if (onRoll.Count != studentIds.Count)
        {
            _log.LogWarning(
                "School enrolment rejected: user {UserId} listed {Asked} students but only {Found} are reachable on school {SchoolId} (restricted={Restricted}).",
                actingUserId, studentIds.Count, onRoll.Count, schoolId, scope.IsRestricted);
            return new(false, "Some selected students are no longer on your school's roll. Refresh and try again.", null, 0, 0m);
        }

        // ── 3. Price from the catalogue, at submit time. Never from the browser. ──
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.Status == ProductStatus.Active, ct);
        if (product == null)
            return new(false, "That course is no longer available. Pick another and try again.", null, 0, 0m);

        // School-tier price for the order line — same rule the enrollment catalogue displays. Route
        // is gated to school staff, so the buyer always qualifies for the school tier.
        var unit = product.EffectivePriceFor(isSchoolStudent: true);
        var qty = studentIds.Count;
        var total = Math.Round(unit * qty, 2);

        var school = await _db.Schools.AsNoTracking()
            .Include(s => s.State)
            .FirstOrDefaultAsync(s => s.Id == schoolId, ct);
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
                .Where(ss => ss.SchoolId == schoolId && studentIds.Contains(ss.UserId))
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

            // ── The roster, structurally ──────────────────────────────────────────────────
            // The OrderNote above stays for humans; THESE rows are what the system acts on. They
            // record who the order covers and at what price, so payment confirmation can grant
            // access and raise one invoice per student without re-reading a free-text note.
            // Written unpaid and unconfirmed — ConfirmedAt/InvoiceId are filled only after
            // server-side payment verification.
            foreach (var studentId in studentIds)
            {
                _db.SchoolEnrollmentStudents.Add(new SchoolEnrollmentStudent
                {
                    Id = Guid.NewGuid(),
                    Order = order,
                    SchoolId = schoolId,
                    StudentUserId = studentId,
                    ProductId = product.Id,
                    UnitPrice = unit,
                });
            }

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

    public async Task<bool> IsSchoolOrderAsync(string orderNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderNumber)) return false;
        return await _db.SchoolEnrollmentStudents
            .AnyAsync(r => r.Order.OrderNumber == orderNumber, ct);
    }

    /// <summary>
    /// The enrolment roster this caller may read: their school, narrowed to the students THEY added
    /// when they are a Coordinator.
    ///
    /// Every per-student read below starts from this, so the isolation rule is written once and
    /// cannot be forgotten by a new query. The narrowing is a subquery over SchoolStudents —
    /// SchoolEnrollmentStudents has no "added by" of its own, and adding one would duplicate a fact
    /// that already exists on SchoolStudents.AddedByUserId and could then disagree with it.
    /// </summary>
    private IQueryable<SchoolEnrollmentStudent> ScopedRoster(SchoolAccessScope scope)
    {
        var q = _db.SchoolEnrollmentStudents.AsNoTracking()
            .Where(r => r.SchoolId == scope.SchoolId);

        if (scope.RestrictToAddedByUserId is { } addedBy)
        {
            var mine = _db.SchoolStudents.AsNoTracking()
                .Where(ss => ss.SchoolId == scope.SchoolId && ss.AddedByUserId == addedBy)
                .Select(ss => ss.UserId);
            q = q.Where(r => mine.Contains(r.StudentUserId));
        }

        return q;
    }

    public async Task<List<SchoolEnrollmentOrderRow>> ListOrdersAsync(
        Guid actingUserId, CancellationToken ct = default)
    {
        var scope = await _students.ResolveScopeAsync(actingUserId, ct);
        if (scope is null) return new();

        // Grouped from the roster so the scope IS the school link — an order with no roster row for
        // this school simply produces no group, rather than being filtered out after the fact.
        var roster = _db.SchoolEnrollmentStudents.AsNoTracking()
            .Where(r => r.SchoolId == scope.SchoolId);

        // COORDINATOR ISOLATION — an order is theirs only when EVERY student on it is one they
        // added. Matching "contains at least one of mine" would be wrong in the other direction:
        // StudentCount and TotalAmount below are whole-order figures, so a mixed order would show
        // this coordinator other people's pupils in the count and their money in the total.
        // Requiring the whole order keeps every number on the row truthful.
        if (scope.RestrictToAddedByUserId is { } addedBy)
        {
            var mine = _db.SchoolStudents.AsNoTracking()
                .Where(ss => ss.SchoolId == scope.SchoolId && ss.AddedByUserId == addedBy)
                .Select(ss => ss.UserId);

            roster = roster.Where(r =>
                !_db.SchoolEnrollmentStudents
                    .Any(o => o.OrderId == r.OrderId && !mine.Contains(o.StudentUserId)));
        }

        var rows = await roster
            .GroupBy(r => r.OrderId)
            .Select(g => new
            {
                OrderId = g.Key,
                StudentCount = g.Count(),
                Order = g.First().Order,
                CourseTitle = g.First().Product.Title,
                InvoiceCount = _db.Set<Invoice>()
                    .Count(i => i.OrderId == g.Key && i.Status == InvoiceStatus.Active),
            })
            .ToListAsync(ct);

        return rows
            .OrderByDescending(r => r.Order.CreatedAt)
            .Select(r => new SchoolEnrollmentOrderRow(
                r.OrderId,
                r.Order.OrderNumber,
                r.Order.CreatedAt,
                r.CourseTitle,
                r.StudentCount,
                r.Order.TotalAmount,
                r.Order.PaymentStatus.ToString(),
                r.Order.GatewayPaymentMode,
                r.InvoiceCount))
            .ToList();
    }

    public async Task<List<SchoolStudentPaymentRow>> ListStudentPaymentsAsync(
        Guid actingUserId, CancellationToken ct = default)
    {
        var scope = await _students.ResolveScopeAsync(actingUserId, ct);
        if (scope is null) return new();

        // Driven FROM the SCOPED roster, so both the school scope and the coordinator's added-by
        // restriction are the join itself, not a post-filter.
        // LEFT JOIN on the invoice: pending and failed rows must still appear, just without one.
        //
        // Sorting and filtering stay in SQL on persisted columns. Enum→string and DateOnly→DateTime
        // conversions are done AFTER materialisation — putting them in the projection is what made
        // the earlier invoice query untranslatable.
        var rows = await (
            from r in ScopedRoster(scope)
            join inv in _db.Set<Invoice>().AsNoTracking().Where(i => i.Status == InvoiceStatus.Active)
                on new { r.OrderId, S = (Guid?)r.StudentUserId } equals new { inv.OrderId, S = inv.StudentUserId }
                into invs
            from i in invs.DefaultIfEmpty()
            orderby r.Order.CreatedAt descending, r.StudentUser.FullName
            select new
            {
                r.StudentUserId,
                StudentName = r.StudentUser.FullName,
                StudentPhone = r.StudentUser.Phone,
                CourseTitle = r.Product.Title,
                r.UnitPrice,
                OrderDate = r.Order.CreatedAt,
                ConfirmedAt = r.Order.ConfirmedAt,
                Status = r.Order.PaymentStatus,
                r.Order.OrderNumber,
                InvoiceId = (Guid?)i.Id,
                InvoiceNumber = i.InvoiceNumber,
            }).ToListAsync(ct);

        return rows.Select(x =>
        {
            var paid = x.Status == PaymentStatus.Success;
            return new SchoolStudentPaymentRow(
                x.StudentUserId,
                x.StudentName,
                x.StudentPhone,
                x.CourseTitle,
                x.UnitPrice,
                // Paid rows are dated by when the payment actually confirmed; unpaid ones by when
                // the enrolment was raised, since no payment date exists yet.
                paid ? (x.ConfirmedAt ?? x.OrderDate) : x.OrderDate,
                paid ? "Paid" : x.Status.ToString(),
                paid,
                // Only offer an invoice when the payment succeeded AND one was actually raised.
                paid ? x.InvoiceId : null,
                paid ? x.InvoiceNumber : null,
                x.OrderNumber);
        }).ToList();
    }

    public async Task<Dictionary<Guid, SchoolStudentPaymentStatus>> GetStudentPaymentStatusesAsync(
        Guid actingUserId, CancellationToken ct = default)
    {
        var scope = await _students.ResolveScopeAsync(actingUserId, ct);
        if (scope is null) return new();

        // Every school-raised enrolment line this caller may see, with its order's persisted status
        // and the invoice if one was issued. Driven FROM the SCOPED roster, so a student who merely
        // named this school when self-registering has no row here and therefore no school payment
        // obligation — and a coordinator sees only the lines for students they added.
        var rows = await (
            from r in ScopedRoster(scope)
            join inv in _db.Set<Invoice>().AsNoTracking().Where(i => i.Status == InvoiceStatus.Active)
                on new { r.OrderId, S = (Guid?)r.StudentUserId } equals new { inv.OrderId, S = inv.StudentUserId }
                into invs
            from i in invs.DefaultIfEmpty()
            select new
            {
                r.StudentUserId,
                r.UnitPrice,
                CourseTitle = r.Product.Title,
                Status = r.Order.PaymentStatus,
                r.Order.OrderNumber,
                r.Order.CreatedAt,
                InvoiceId = (Guid?)i.Id,
                InvoiceNumber = i.InvoiceNumber,
            }).ToListAsync(ct);

        // Collapse a student's lines to one verdict. A pupil can be enrolled more than once (a
        // failed attempt then a successful one), so success wins outright; otherwise the most
        // recent attempt decides between Failed and Pending.
        return rows
            .GroupBy(x => x.StudentUserId)
            .Select(g =>
            {
                var paid = g.Where(x => x.Status == PaymentStatus.Success)
                            .OrderByDescending(x => x.CreatedAt).FirstOrDefault();
                var latest = g.OrderByDescending(x => x.CreatedAt).First();
                var chosen = paid ?? latest;
                var isPaid = paid is not null;
                var isFailed = !isPaid && latest.Status == PaymentStatus.Failed;

                return new SchoolStudentPaymentStatus(
                    g.Key,
                    isPaid ? "Paid" : isFailed ? "Failed" : "Pending",
                    isPaid,
                    isFailed,
                    // An invoice is only ever offered against a SUCCESSFUL payment.
                    isPaid ? chosen.InvoiceId : null,
                    isPaid ? chosen.InvoiceNumber : null,
                    chosen.UnitPrice,
                    chosen.CourseTitle,
                    chosen.OrderNumber);
            })
            .ToDictionary(x => x.StudentUserId);
    }

    public async Task<SchoolPaymentSummary> GetPaymentSummaryAsync(
        Guid actingUserId, CancellationToken ct = default)
    {
        var scope = await _students.ResolveScopeAsync(actingUserId, ct);
        if (scope is null) return new(0, 0, 0, 0m);

        // The denominator is everyone the CALLER may see — the whole roll for a principal, only the
        // students they added for a coordinator. Using the school-wide count here would leak the
        // school's size through the "pending" figure even with every other number restricted.
        var totalStudents = await _db.SchoolStudents.AsNoTracking()
            .CountAsync(ss => ss.SchoolId == scope.SchoolId
                           && ss.IsActive
                           && (scope.RestrictToAddedByUserId == null
                               || ss.AddedByUserId == scope.RestrictToAddedByUserId), ct);

        var paidRoster = ScopedRoster(scope)
            .Where(r => r.Order.PaymentStatus == PaymentStatus.Success);

        // DISTINCT student: a pupil enrolled on two courses is one paid student, not two.
        var paidStudents = await paidRoster.Select(r => r.StudentUserId).Distinct().CountAsync(ct);

        // Money is summed over every paid enrolment line, so two courses count twice — which is
        // what "total paid" means.
        var totalPaid = await paidRoster.SumAsync(r => (decimal?)r.UnitPrice, ct) ?? 0m;

        return new SchoolPaymentSummary(
            totalStudents,
            paidStudents,
            Math.Max(0, totalStudents - paidStudents),
            totalPaid);
    }

    public async Task<List<SchoolStudentInvoiceRow>> ListInvoicesAsync(
        Guid actingUserId, Guid? orderId = null, CancellationToken ct = default)
    {
        var scope = await _students.ResolveScopeAsync(actingUserId, ct);
        if (scope is null) return new();

        // Join drives FROM the SCOPED roster: only invoices whose (OrderId, StudentUserId) pair is
        // reachable by this caller can appear. Passing another school's — or another coordinator's —
        // orderId yields an empty list, not a leak.
        //
        // ORDERING AND SHAPING ARE SEPARATE STEPS ON PURPOSE.
        // Filtering AND sorting stay in SQL, on persisted columns — Invoice.InvoiceDate (a DateOnly
        // column) and Invoice.InvoiceNumber. Only the final shaping runs client-side, over the rows
        // this query already narrowed and ordered; nothing extra is fetched.
        //
        // Sorting after the projection is what broke: EF cannot order by a member of a DTO built by
        // a constructor, so `OrderByDescending(x => x.InvoiceDate)` on the projected row had no SQL
        // translation. Two more pieces were untranslatable in that same statement —
        // DateOnly.ToDateTime(...) and enum .ToString() — so both now happen after materialisation.
        var rows = await (
            from r in ScopedRoster(scope)
            join i in _db.Set<Invoice>().AsNoTracking()
                on new { r.OrderId, S = (Guid?)r.StudentUserId } equals new { i.OrderId, S = i.StudentUserId }
            where i.Status == InvoiceStatus.Active
               && (orderId == null || r.OrderId == orderId.Value)
            orderby i.InvoiceDate descending, i.InvoiceNumber
            select new
            {
                i.Id,
                i.InvoiceNumber,
                i.InvoiceDate,
                StudentName = r.StudentUser.FullName,
                CourseTitle = r.Product.Title,
                i.TotalAmount,
                i.OrderNumber,
                PaymentStatus = r.Order.PaymentStatus,
            }).ToListAsync(ct);

        return rows.Select(x => new SchoolStudentInvoiceRow(
            x.Id,
            x.InvoiceNumber,
            x.InvoiceDate.ToDateTime(TimeOnly.MinValue),
            x.StudentName,
            x.CourseTitle,
            x.TotalAmount,
            x.OrderNumber,
            x.PaymentStatus.ToString())).ToList();
    }

    public async Task<(byte[] bytes, string filename)?> RenderInvoicePdfAsync(
        Guid actingUserId, Guid invoiceId, CancellationToken ct = default)
    {
        var scope = await _students.ResolveScopeAsync(actingUserId, ct);
        if (scope is null) return null;

        // AUTHORISATION, not filtering. THE direct-URL guard: this is what a coordinator hits when
        // they paste another coordinator's invoice id into /school/invoice/{id}/pdf.
        //
        // The invoice must be linked, through the SCOPED roster, to a student this caller may reach:
        // Invoice → StudentUserId → roster(SchoolId [+ AddedByUserId]) → caller.
        // An id from another school, or from another coordinator's student, fails this and returns
        // null — indistinguishable from not found, so the endpoint cannot be used to probe which
        // invoice ids exist.
        var allowed = await (
            from r in ScopedRoster(scope)
            join i in _db.Set<Invoice>().AsNoTracking()
                on new { r.OrderId, S = (Guid?)r.StudentUserId } equals new { i.OrderId, S = i.StudentUserId }
            where i.Id == invoiceId
            select i.Id).AnyAsync(ct);

        if (!allowed)
        {
            _log.LogWarning("School invoice PDF DENIED — user {UserId} (school {SchoolId}, restricted={Restricted}) requested invoice {InvoiceId}.",
                actingUserId, scope.SchoolId, scope.IsRestricted, invoiceId);
            return null;
        }

        // Rendering itself is the existing, already-verified pipeline. Nothing is re-implemented.
        return await _invoices.RenderPdfAsync(invoiceId, ct);
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

    /// <summary>Same configured series CheckoutService issues, so school orders sit in one sequence
    /// with every other order rather than a parallel numbering of their own.</summary>
    private async Task<string> GenerateOrderNumberAsync(CancellationToken ct) =>
        await OrderNumberGenerator.NextAsync(_db.Orders, (await _company.GetAsync()).OrderSeries, ct);
}

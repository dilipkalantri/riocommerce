using RioCommerce.Infrastructure.Services.Catalog;
using System.Globalization;
using System.Text;
using System.Text.Json;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Meta;
using RioCommerce.Core.DTOs.Orders;
using RioCommerce.Core.DTOs.Realtime;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services;

public class OrderAdminService : IOrderAdminService
{
    private const string EntityType = "Order";
    private readonly RioCommerceDbContext _db;
    private readonly IAuditService _audit;
    private readonly IRealtimeBus _bus;
    private readonly INotificationCenterService _notify;
    private readonly IOrderCalculationService _calc;
    private readonly INotificationService _customerNotify;
    private readonly IFranchiseService _franchise;
    private readonly IFacultySharingService _facultySharing;
    private readonly ISerialKeyService _serialKeys;
    private readonly IInvoiceService _invoices;
    private readonly IInstallmentService _installments;
    private readonly ILogger<OrderAdminService> _log;
    /// <summary>The same sender checkout uses, so a resent confirmation is identical to the original.</summary>
    private readonly INotificationSender _sender;
    private readonly IPermissionService _perm;
    public OrderAdminService(RioCommerceDbContext db, IAuditService audit, IRealtimeBus bus,
        INotificationCenterService notify, IOrderCalculationService calc, INotificationService customerNotify,
        IFranchiseService franchise, IFacultySharingService facultySharing, ISerialKeyService serialKeys,
        IInvoiceService invoices, IInstallmentService installments, INotificationSender sender,
        IPermissionService perm, ILogger<OrderAdminService> log)
    {
        _db = db; _audit = audit; _bus = bus; _notify = notify; _calc = calc; _customerNotify = customerNotify;
        _franchise = franchise; _facultySharing = facultySharing; _serialKeys = serialKeys;
        _invoices = invoices; _installments = installments; _sender = sender; _perm = perm; _log = log;
    }

    private const string SuperAdminOnly = "Only a Super Admin can do this.";

    /// <summary>
    /// Gate for cancelling and deleting orders.
    ///
    /// <para>An unknown actor is refused. Background jobs and unauthenticated callers arrive with a
    /// null actor id, and there is no legitimate path where one of those should cancel or delete a
    /// customer's paid order — so the absence of an identity is treated as "not permitted" rather
    /// than waved through.</para>
    /// </summary>
    private async Task<bool> IsSuperAdminAsync(Guid? actorId)
        => actorId is { } id && id != Guid.Empty && await _perm.IsSuperAdminAsync(id);

    /// <summary>
    /// Once a franchisee has paid, THE FRANCHISEE can no longer change that order's basket.
    ///
    /// <para>Payment settles their side irreversibly: the wallet is debited by
    /// <c>FranchiseNetPayable</c>, a ledger row records that exact figure, the franchise share is
    /// retained, and any coupon redemption is counted. None of it is recomputed when an item is
    /// edited afterwards, so a franchisee editing their own paid order could quietly change what
    /// they owe while the ledger keeps saying something else.</para>
    ///
    /// <para><b>Staff are not blocked.</b> Admins and super admins keep full editing rights — they
    /// are the ones who fix a mis-keyed order, and they can see and correct the ledger side too.
    /// The lock exists to stop the party with an interest in the number from moving it.</para>
    ///
    /// <para>Two conditions, both required: the actor is a franchise user, and the order is a paid
    /// franchise order. Anyone who is not a franchise user passes straight through.</para>
    /// </summary>
    private async Task<bool> IsFrozenForActorAsync(Order o, Guid? actorId)
    {
        if (o.Source != OrderSource.Franchisee || o.PaymentStatus != PaymentStatus.Success) return false;
        return await IsFranchiseUserAsync(actorId);
    }

    /// <summary>
    /// True when the actor acts for a franchise rather than for RioCommerce.
    ///
    /// <para>Staff roles win outright — a user who is admin or super admin is treated as staff even
    /// if they also carry a franchise role, so granting someone franchise access can never quietly
    /// take away their admin rights.</para>
    ///
    /// <para>Both ways of being a franchise user count: holding the <c>franchise_admin</c> role, and
    /// being the <c>AdminUserId</c> on a franchise record. The two are set independently, so
    /// checking only one would leave a way around the lock.</para>
    /// </summary>
    private async Task<bool> IsFranchiseUserAsync(Guid? actorId)
    {
        if (actorId is not { } id || id == Guid.Empty) return false;

        var roles = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == id && ur.IsActive)
            .Select(ur => ur.Role.Name)
            .ToListAsync();

        if (roles.Any(r => r is "super_admin" or "admin")) return false;
        if (roles.Contains("franchise_admin")) return true;

        return await _db.Franchises.AsNoTracking().AnyAsync(f => f.AdminUserId == id);
    }

    private const string FrozenOrderMessage =
        "This order is paid and locked. Contact RioCommerce if something needs to be corrected.";

    /// <summary>Idempotent — calls EnqueueForOrderAsync + invoice generation but never lets either issue
    /// derail the order flow. Used by every code path here that transitions an order into a paid state.</summary>
    private async Task TryEnqueueSerialKeysAsync(Guid orderId, string orderNumber)
    {
        try
        {
            var n = await _serialKeys.EnqueueForOrderAsync(orderId);
            if (n > 0) _log.LogInformation("SerialKey enqueued count={Count} orderNumber={OrderNumber}", n, orderNumber);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SerialKey enqueue threw orderNumber={OrderNumber}", orderNumber);
        }
        try
        {
            var (newId, existingId, err) = await _invoices.EnsureForOrderAsync(orderId, actorName: "admin");
            if (newId.HasValue)
                _log.LogInformation("Invoice generated id={Id} orderNumber={OrderNumber}", newId, orderNumber);
            else if (!string.IsNullOrEmpty(err) && existingId == null)
                _log.LogWarning("Invoice not generated orderNumber={OrderNumber} reason={Reason}", orderNumber, err);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Invoice generation threw orderNumber={OrderNumber}", orderNumber);
        }
    }

    private void Touch(params string[] scopes) { foreach (var s in scopes) _bus.PublishDataChanged(new RealtimeEvent(s)); }

    // Builds the filtered (unpaged, unsorted) order query shared by listing + export.
    private IQueryable<Order> BuildQuery(OrderFilter f)
    {
        // OnlyDeleted = the recycle bin; bypass the global soft-delete filter and show deleted rows.
        var q = f.OnlyDeleted ? _db.Orders.IgnoreQueryFilters().Where(o => o.IsDeleted) : _db.Orders.AsQueryable();
        // Wallet top-ups are orders only so they can be invoiced through the normal pipeline. They
        // are not sales and must never appear in the order list or its export.
        q = q.ExcludeWalletTopUps();

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(o => EF.Functions.ILike(o.OrderNumber, $"%{s}%")
                || EF.Functions.ILike(o.StudentName, $"%{s}%")
                || EF.Functions.ILike(o.StudentPhone, $"%{s}%")
                || (o.StudentEmail != null && EF.Functions.ILike(o.StudentEmail, $"%{s}%"))
                // The originating system's reference is how staff who work in that system look an
                // order up, so it belongs in the same box rather than behind its own field.
                || (o.SourceNo != null && EF.Functions.ILike(o.SourceNo, $"%{s}%")));
        }
        if (!string.IsNullOrWhiteSpace(f.SourceNo)) q = q.Where(o => o.SourceNo != null && EF.Functions.ILike(o.SourceNo, $"%{f.SourceNo.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(f.OrderNumber)) q = q.Where(o => EF.Functions.ILike(o.OrderNumber, $"%{f.OrderNumber.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(f.StudentName)) q = q.Where(o => EF.Functions.ILike(o.StudentName, $"%{f.StudentName.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(f.StudentEmail)) q = q.Where(o => o.StudentEmail != null && EF.Functions.ILike(o.StudentEmail, $"%{f.StudentEmail.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(f.StudentPhone)) q = q.Where(o => EF.Functions.ILike(o.StudentPhone, $"%{f.StudentPhone.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(f.Product)) q = q.Where(o => o.Items.Any(i => EF.Functions.ILike(i.ProductTitle, $"%{f.Product.Trim()}%")));
        if (f.ProductId.HasValue) q = q.Where(o => o.Items.Any(i => i.ProductId == f.ProductId));
        if (!string.IsNullOrWhiteSpace(f.CouponCode)) q = q.Where(o => o.CouponCode != null && EF.Functions.ILike(o.CouponCode, $"%{f.CouponCode.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(f.Notes)) q = q.Where(o => o.InternalNotes != null && EF.Functions.ILike(o.InternalNotes, $"%{f.Notes.Trim()}%"));
        if (f.StartDate.HasValue) q = q.Where(o => o.CreatedAt >= f.StartDate.Value.ToUniversalTime());
        if (f.EndDate.HasValue) q = q.Where(o => o.CreatedAt < f.EndDate.Value.ToUniversalTime().AddDays(1));
        // Faculty match goes through ProductFaculty, the real many-to-many. Matching on
        // PrimaryFacultyId alone missed every order for a course taught by a co-faculty, so
        // filtering by that person returned nothing.
        if (f.FacultyId.HasValue)
            q = q.Where(o => o.Items.Any(i => _db.ProductFaculty
                .Any(pf => pf.ProductId == i.ProductId && pf.FacultyId == f.FacultyId)));
        if (f.FranchiseId.HasValue) q = q.Where(o => o.FranchiseId == f.FranchiseId);
        if (f.Status.HasValue) q = q.Where(o => o.Status == f.Status);
        if (f.PaymentStatus.HasValue) q = q.Where(o => o.PaymentStatus == f.PaymentStatus);
        if (f.Source.HasValue) q = q.Where(o => o.Source == f.Source);
        if (f.PaymentMode.HasValue) q = q.Where(o => o.PaymentMode == f.PaymentMode);

        // ── Multi-select (§17). Empty list = All, so an untouched filter never narrows anything.
        //    These sit alongside the single-value fields above rather than replacing them: the
        //    existing UI and any API caller still pass those. ──
        if (f.FacultyIds.Count > 0)
            q = q.Where(o => o.Items.Any(i => _db.ProductFaculty
                .Any(pf => pf.ProductId == i.ProductId && f.FacultyIds.Contains(pf.FacultyId))));
        if (f.ProductIds.Count > 0)
            q = q.Where(o => o.Items.Any(i => f.ProductIds.Contains(i.ProductId)));
        if (f.SubjectIds.Count > 0)
            // Any subject the ordered course covers, so filtering Orders by "Costing" finds the
            // combo that teaches it. This narrows which ORDERS are listed; it never changes an
            // order's own figures, which come from the order rows themselves.
            q = q.Where(o => o.Items.Any(i => _db.ProductSubjects
                    .Any(ps => ps.ProductId == i.ProductId && f.SubjectIds.Contains(ps.SubjectId))
                || (i.Product.SubjectId != null && f.SubjectIds.Contains(i.Product.SubjectId.Value))));
        if (f.FranchiseIds.Count > 0)
            q = q.Where(o => o.FranchiseId != null && f.FranchiseIds.Contains(o.FranchiseId.Value));
        if (f.Statuses.Count > 0) q = q.Where(o => f.Statuses.Contains(o.Status));
        if (f.PaymentStatuses.Count > 0) q = q.Where(o => f.PaymentStatuses.Contains(o.PaymentStatus));
        if (f.Sources.Count > 0) q = q.Where(o => f.Sources.Contains(o.Source));
        if (f.PaymentModes.Count > 0) q = q.Where(o => o.PaymentMode != null && f.PaymentModes.Contains(o.PaymentMode.Value));
        if (f.Enrollment == EnrollmentStatus.Active) q = q.Where(o => o.Items.Any(i => i.IsActivated));
        else if (f.Enrollment == EnrollmentStatus.NotActivated) q = q.Where(o => !o.Items.Any(i => i.IsActivated));
        // Expired isn't modelled (no per-enrollment expiry yet) → returns no rows so the filter is honest.
        else if (f.Enrollment == EnrollmentStatus.Expired) q = q.Where(o => false);

        // ── 💰 Finance filters — all server-side; tiny inline expressions translate to SQL cleanly. ──
        if (f.GrossMin.HasValue) q = q.Where(o => o.Items.Sum(i => i.UnitPrice * i.Quantity) >= f.GrossMin);
        if (f.GrossMax.HasValue) q = q.Where(o => o.Items.Sum(i => i.UnitPrice * i.Quantity) <= f.GrossMax);
        if (f.NetMin.HasValue)   q = q.Where(o => o.TotalAmount >= f.NetMin);
        if (f.NetMax.HasValue)   q = q.Where(o => o.TotalAmount <= f.NetMax);
        if (f.HasStudentDiscount == true)  q = q.Where(o => o.DiscountAmount > 0 || o.Items.Any(i => i.Discount > 0));
        if (f.HasStudentDiscount == false) q = q.Where(o => o.DiscountAmount == 0 && !o.Items.Any(i => i.Discount > 0));
        if (f.TaxMin.HasValue) q = q.Where(o => o.GstAmount >= f.TaxMin);
        if (f.TaxMax.HasValue) q = q.Where(o => o.GstAmount <= f.TaxMax);
        // Franchise "discount" % is now the recorded commission share of the order subtotal.
        if (f.FranchiseDiscountPctMin.HasValue)
            q = q.Where(o => o.Source == OrderSource.Franchisee &&
                             o.Subtotal > 0 &&
                             _db.FranchiseCommissionEntries.Where(e => e.OrderId == o.Id).Sum(e => (decimal?)e.CommissionAmount).GetValueOrDefault() / o.Subtotal * 100m >= f.FranchiseDiscountPctMin);
        if (f.FranchiseDiscountPctMax.HasValue)
            q = q.Where(o => o.Source != OrderSource.Franchisee ||
                             o.Subtotal == 0 ||
                             _db.FranchiseCommissionEntries.Where(e => e.OrderId == o.Id).Sum(e => (decimal?)e.CommissionAmount).GetValueOrDefault() / o.Subtotal * 100m <= f.FranchiseDiscountPctMax);

        return q;
    }

    private static IQueryable<Order> ApplySort(IQueryable<Order> q, OrderFilter f) => (f.SortBy, f.SortDesc) switch
    {
        // ── Original sort modes — kept for API compatibility ──
        ("amount", true)   => q.OrderByDescending(o => o.TotalAmount),
        ("amount", false)  => q.OrderBy(o => o.TotalAmount),
        ("order", true)    => q.OrderByDescending(o => o.OrderNumber),
        ("order", false)   => q.OrderBy(o => o.OrderNumber),
        // ── Finance/ERP grid sort modes ──
        ("net", true)      => q.OrderByDescending(o => o.TotalAmount),
        ("net", false)     => q.OrderBy(o => o.TotalAmount),
        ("gross", true)    => q.OrderByDescending(o => o.Items.Sum(i => i.UnitPrice * i.Quantity)),
        ("gross", false)   => q.OrderBy(o => o.Items.Sum(i => i.UnitPrice * i.Quantity)),
        ("gst", true)      => q.OrderByDescending(o => o.GstAmount),
        ("gst", false)     => q.OrderBy(o => o.GstAmount),
        ("taxable", true)  => q.OrderByDescending(o => o.TotalAmount - o.GstAmount),
        ("taxable", false) => q.OrderBy(o => o.TotalAmount - o.GstAmount),
        ("student", true)  => q.OrderByDescending(o => o.StudentName),
        ("student", false) => q.OrderBy(o => o.StudentName),
        ("sourceno", true) => q.OrderByDescending(o => o.SourceNo),
        ("sourceno", false)=> q.OrderBy(o => o.SourceNo),
        (_, false)         => q.OrderBy(o => o.CreatedAt),
        _                  => q.OrderByDescending(o => o.CreatedAt)
    };

    // Projects to a flat shape EF can translate, then maps the derived enrollment status in memory.
    private async Task<List<OrderListItem>> ProjectAsync(IQueryable<Order> q)
    {
        var rows = await q.Select(o => new
        {
            o.Id, o.OrderNumber, o.SourceNo, o.CreatedAt, o.StudentName, o.StudentPhone, o.StudentEmail,
            FirstProduct = o.Items.Select(i => i.ProductTitle).FirstOrDefault(),
            FirstProductLevel = o.Items.Select(i => i.Product.Level).FirstOrDefault(),
            ItemCount = o.Items.Count,
            // EVERY product on the order, with quantity. The grid keeps showing the compact
            // "first +N more", but an export must carry the whole basket — a spreadsheet row
            // reading "…Book Set +1 more" tells the reader a product exists and then hides it.
            // ModeName + SelectedOptionsJson are what the customer actually bought (lecture mode and
            // the purchase-option picks); without them the export cannot tell two orders apart.
            AllProducts = o.Items.Select(i => new { i.ProductTitle, i.Quantity, i.ModeName, i.SelectedOptionsJson }).ToList(),
            // Full billing + shipping address for despatch. Only the city was being exported.
            o.BillingAddress, o.BillingCity, o.BillingState, o.BillingPincode,
            o.ShippingAddress, o.ShippingCity, o.ShippingState, o.ShippingPincode,
            o.GstNumber,
            Faculty = o.Items.Select(i => i.Product.PrimaryFaculty!.DisplayName).FirstOrDefault(),
            o.Subtotal, o.DiscountAmount, o.GstAmount,
            o.TotalAmount, o.Source, o.Status, o.PaymentStatus, o.PaymentMode,
            HasAffiliate = o.AffiliateId != null,
            IsCorporate  = o.CustomerType == CustomerType.Organization,
            AnyActivated = o.Items.Any(i => i.IsActivated),
            Franchise = o.Franchise != null ? o.Franchise.Name : null,
            // Gross = sticker price total; ItemDiscount = per-item discount summed.
            GrossSum = o.Items.Sum(i => i.UnitPrice * i.Quantity),
            ItemDiscountSum = o.Items.Sum(i => i.Discount * i.Quantity),
            Commission = o.Source == OrderSource.Franchisee
                ? _db.FranchiseCommissionEntries.Where(e => e.OrderId == o.Id).Sum(e => (decimal?)e.CommissionAmount).GetValueOrDefault()
                : 0m,
            o.ReferralSourceName, o.ReferralType
        }).ToListAsync();

        return rows.Select(x =>
        {
            // ── Spec formulas ──
            //  GrossAmount        = sum(UnitPrice * Qty)   — UnitPrice is already the LIST price
            //  StudentDiscount    = Order.DiscountAmount   — already the roll-up of the line discounts
            //  FranchiseDiscount  = franchisee commission share (only for OrderSource.Franchisee, else 0)
            //  FranchisePct       = FranchiseDiscount / (Gross - StudentDiscount) * 100
            //  NetAmount          = Order.TotalAmount (GST inclusive — final paid)
            //  TaxableAmount      = NetAmount - GstAmount     (stored GST when available)
            //  GstAmount          = stored GST, else Net × 18 / 118 (spec formula fallback)
            //
            // The arithmetic now lives in Reporting.OrderMoney so this grid and all eight reports
            // compute it from one place — §26 and §36.13/14 require the reports to agree with the
            // order screens to the rupee, which two copies of the formulas cannot guarantee.
            var money = Reporting.OrderMoney.ForOrder(
                grossSum: x.GrossSum,
                orderDiscount: x.DiscountAmount,
                itemDiscountSum: x.ItemDiscountSum,
                commission: x.Commission,
                source: x.Source,
                totalAmount: x.TotalAmount,
                storedGst: x.GstAmount);

            return new OrderListItem
            {
                Id = x.Id, OrderNumber = x.OrderNumber, SourceNo = x.SourceNo, CreatedAt = x.CreatedAt,
                StudentName = x.StudentName, StudentPhone = x.StudentPhone, StudentEmail = x.StudentEmail,
                ProductSummary = string.IsNullOrEmpty(x.FirstProduct) ? "—"
                    : x.FirstProduct + (x.ItemCount > 1 ? $" +{x.ItemCount - 1} more" : ""),
                // Full basket for exports: title, what was chosen, and quantity when above one.
                ProductNames = x.AllProducts
                    .Where(p => !string.IsNullOrWhiteSpace(p.ProductTitle))
                    .Select(p => DescribeOrderLine(p.ProductTitle, p.ModeName, p.SelectedOptionsJson, p.Quantity))
                    .ToList(),
                BillingAddressFull = JoinAddress(x.BillingAddress, x.BillingCity, x.BillingState, x.BillingPincode),
                ShippingAddressFull = JoinAddress(x.ShippingAddress, x.ShippingCity, x.ShippingState, x.ShippingPincode),
                GstNumber = x.GstNumber,
                CourseLevel = x.FirstProductLevel.ToString(),
                FacultyName = x.Faculty,
                TotalAmount = x.TotalAmount, Source = x.Source, Status = x.Status,
                PaymentStatus = x.PaymentStatus, PaymentMode = x.PaymentMode,
                Enrollment = x.AnyActivated ? EnrollmentStatus.Active : EnrollmentStatus.NotActivated,
                FranchiseName = x.Franchise,
                FranchiseCommission = x.Commission < 0 ? 0 : x.Commission,
                ReferredByName = x.ReferralSourceName,
                ReferralType = x.ReferralType,
                // ── Finance fields ──
                GrossAmount = money.Gross,
                StudentDiscount = money.Discount,
                FranchiseDiscountPercent = money.FranchiseDiscountPercent,
                FranchiseDiscountAmount = money.FranchiseDiscount,
                NetAmount = money.Net,
                TaxableAmount = money.Taxable,
                GstAmount = money.Gst,
                HasAffiliate = x.HasAffiliate,
                IsCorporate = x.IsCorporate
            };
        }).ToList();
    }

    public async Task<PagedResult<OrderListItem>> ListAsync(OrderFilter filter)
    {
        var q = BuildQuery(filter);
        var total = await q.CountAsync();
        var page = ApplySort(q, filter).Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize);
        var items = await ProjectAsync(page);
        return new PagedResult<OrderListItem> { Items = items, TotalCount = total, Page = filter.Page, PageSize = filter.PageSize };
    }

    public async Task<OrderFilterMeta> GetFilterMetaAsync(Guid? productId = null)
    {
        var faculty = _db.Faculty.Where(f => f.IsActive);

        // Cascade: with a product picked, only its teachers are worth offering. Resolved through
        // ProductFaculty so a co-taught course lists everyone mapped to it, and left completely
        // alone when nothing is picked so the list stays exactly what it was.
        var picked = CatalogCascade.Selection.Of(productId: productId);
        if (CatalogCascade.Constrains(picked, CatalogCascade.Dimension.Faculty))
        {
            var ids = CatalogCascade.FacultyIdsFor(_db,
                CatalogCascade.ProductsMatching(_db, picked, CatalogCascade.Dimension.Faculty));
            faculty = faculty.Where(f => ids.Contains(f.Id));
        }

        var facultyList = await faculty.OrderBy(f => f.DisplayOrder)
            .Select(f => new IdName(f.Id, f.DisplayName)).ToListAsync();
        // Franchisees are deliberately never narrowed — no catalog relationship to cascade through.
        var franchises = await _db.Franchises.OrderBy(f => f.Name)
            .Select(f => new IdName(f.Id, f.Name)).ToListAsync();
        return new OrderFilterMeta(facultyList, franchises);
    }

    public async Task<(int updated, string? error)> BulkUpdateStatusAsync(IReadOnlyList<Guid> ids, OrderStatus status, Guid? actorId = null, string? actorName = null)
    {
        // Refuse the whole batch rather than cancelling some and skipping others — a partial bulk
        // cancel is harder to notice and harder to undo than an outright refusal.
        if (status == OrderStatus.Cancelled && !await IsSuperAdminAsync(actorId))
            return (0, SuperAdminOnly);

        if (ids.Count == 0) return (0, null);
        var orders = await _db.Orders.Where(o => ids.Contains(o.Id)).ToListAsync();
        var now = DateTime.UtcNow;
        // Track which orders newly transitioned INTO a paid state so we can enqueue serial keys after save.
        var newlyPaid = new List<Order>();
        foreach (var o in orders)
        {
            var wasPaid = o.Status is OrderStatus.Confirmed or OrderStatus.Activated or OrderStatus.Delivered;
            o.Status = status;
            if (status == OrderStatus.Confirmed && o.ConfirmedAt == null) o.ConfirmedAt = now;
            if (status == OrderStatus.Cancelled && o.CancelledAt == null) o.CancelledAt = now;
            var isPaid = status is OrderStatus.Confirmed or OrderStatus.Activated or OrderStatus.Delivered;
            // Keep payment status consistent — a confirmed/activated/delivered order is paid.
            if (isPaid && o.PaymentStatus != PaymentStatus.Success)
            {
                o.PaymentStatus = PaymentStatus.Success;
                if (o.ActivatedAt == null) o.ActivatedAt = now;
            }
            if (!wasPaid && isPaid) newlyPaid.Add(o);
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName ?? "system", "OrderBulkStatusChanged", EntityType, null,
            JsonSerializer.Serialize(new { Count = orders.Count, To = status.ToString(), OrderNumbers = orders.Select(o => o.OrderNumber) }));

        // Serial-key enqueue for every order that just became paid.
        foreach (var o in newlyPaid)
            await TryEnqueueSerialKeysAsync(o.Id, o.OrderNumber);

        Touch("orders", "dashboard");
        return (orders.Count, null);
    }

    public async Task<string> ExportCsvAsync(OrderFilter filter)
    {
        var rows = await ProjectAsync(ApplySort(BuildQuery(filter), filter));
        var sb = new StringBuilder();
        // ── Header — includes every column shown in the Finance/ERP grid + the originals ──
        sb.AppendLine("Order #,Source No.,Order Date,Student Name,Mobile,Email,Billing Address,Shipping Address,GSTIN,Products,Item Count,Course Level,Source,Franchise,Faculty,Order Status,Payment Status,Payment Method,Gross Amount,Student Discount,Franchise Discount %,Franchise Discount Amount,Net Amount (GST Inclusive),Taxable Amount,GST,Enrollment,Referred By");
        foreach (var o in rows)
        {
            sb.Append(Csv(o.OrderNumber)).Append(',')
              .Append(Csv(o.SourceNo)).Append(',')
              .Append(Csv(o.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))).Append(',')
              .Append(Csv(o.StudentName)).Append(',')
              .Append(Csv(o.StudentPhone)).Append(',')
              .Append(Csv(o.StudentEmail)).Append(',')
              .Append(Csv(o.BillingAddressFull)).Append(',')
              // Shipping falls back to billing: most orders ship where they bill, and an empty cell
              // there reads as "no address on file" rather than "same as billing".
              .Append(Csv(string.IsNullOrWhiteSpace(o.ShippingAddressFull) ? o.BillingAddressFull : o.ShippingAddressFull)).Append(',')
              .Append(Csv(o.GstNumber)).Append(',')
              // Every product, not the grid's truncated summary. " | " rather than a comma so the
              // cell stays readable once the CSV quoting is stripped by the spreadsheet.
              .Append(Csv(o.ProductNames.Count > 0 ? string.Join(" | ", o.ProductNames) : o.ProductSummary)).Append(',')
              .Append(o.ProductNames.Count.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(Csv(o.CourseLevel)).Append(',')
              .Append(Csv(o.Source.ToString())).Append(',')
              .Append(Csv(o.FranchiseName)).Append(',')
              .Append(Csv(o.FacultyName)).Append(',')
              .Append(Csv(o.Status.ToString())).Append(',')
              .Append(Csv(o.PaymentStatus.ToString())).Append(',')
              .Append(Csv(o.PaymentMode?.ToString())).Append(',')
              .Append(o.GrossAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
              .Append(o.StudentDiscount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
              .Append(o.FranchiseDiscountPercent.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
              .Append(o.FranchiseDiscountAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
              .Append(o.NetAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
              .Append(o.TaxableAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
              .Append(o.GstAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
              .Append(Csv(o.Enrollment.ToString())).Append(',')
              .Append(Csv(o.ReferredByName)).AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// One order line the way a human reads it: the course, the lecture mode, and every purchase
    /// option chosen — e.g.
    /// <c>CA Inter Audit COMBO [Recorded Lectures + Hardcopy Notes] {Additional Test Series: Without Test Series} (x2)</c>.
    ///
    /// <para>Mode and options are what separate two otherwise identical orders, so an export without
    /// them cannot answer "what did this student actually buy?".</para>
    /// </summary>
    private static string DescribeOrderLine(string title, string? modeName, string? selectedOptionsJson, int quantity)
    {
        var sb = new StringBuilder(title.Trim());

        if (!string.IsNullOrWhiteSpace(modeName))
            sb.Append(" [").Append(modeName.Trim()).Append(']');

        foreach (var opt in ParseSelectedOptions(selectedOptionsJson))
            sb.Append(" {").Append(opt).Append('}');

        if (quantity > 1) sb.Append(" (x").Append(quantity).Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// Reads the per-line option snapshot — <c>[{"groupName":…,"optionName":…,"addOn":…}]</c> — into
    /// "Group: Option" strings, with the add-on price when one was charged. Malformed or missing JSON
    /// yields nothing: a broken snapshot must never break an export.
    /// </summary>
    private static List<string> ParseSelectedOptions(string? json)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var group = el.TryGetProperty("groupName", out var g) ? g.GetString() : null;
                var option = el.TryGetProperty("optionName", out var o) ? o.GetString() : null;
                if (string.IsNullOrWhiteSpace(option)) continue;

                var line = string.IsNullOrWhiteSpace(group) ? option! : $"{group}: {option}";
                if (el.TryGetProperty("addOn", out var a)
                    && a.ValueKind == JsonValueKind.Number
                    && a.TryGetDecimal(out var addOn) && addOn != 0)
                    line += $" +₹{addOn:0.##}";

                result.Add(line);
            }
        }
        catch (JsonException) { /* snapshot unreadable — omit rather than fail the export */ }
        return result;
    }

    /// <summary>Address parts into one cell, skipping the blanks so no ", , ," gaps appear.</summary>
    private static string JoinAddress(params string?[] parts) =>
        string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    private static string Csv(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        return v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? "\"" + v.Replace("\"", "\"\"") + "\""
            : v;
    }

    public async Task<OrderStats> StatsAsync()
    {
        var nonRevenue = new[] { OrderStatus.Draft, OrderStatus.Cancelled, OrderStatus.Refunded };
        // Sales only — wallet top-ups are money in, and the courses bought with them are counted
        // when they're ordered. Including both would double-count the same rupees.
        var sales = _db.Orders.ExcludeWalletTopUps();
        return new OrderStats(
            await sales.CountAsync(),
            await sales.CountAsync(o => o.Status == OrderStatus.Confirmed),
            await sales.CountAsync(o => o.Status == OrderStatus.Pending),
            await sales.Where(o => !nonRevenue.Contains(o.Status)).SumAsync(o => (decimal?)o.TotalAmount) ?? 0);
    }

    // ── 💰 SummaryAsync ── live aggregate over the filter for the summary bar above the grid.
    // Done in ONE round-trip with a server-side GROUP BY (aggregate over the filtered query),
    // not by hydrating rows in memory — so it stays fast even on millions of orders.
    public async Task<OrderFinanceSummary> SummaryAsync(OrderFilter filter)
    {
        var q = BuildQuery(filter);
        var agg = await q.Select(o => new
        {
            Gross = o.Items.Sum(i => i.UnitPrice * i.Quantity),
            StudentDisc = o.DiscountAmount > 0 ? o.DiscountAmount : o.Items.Sum(i => i.Discount * i.Quantity),

            // The franchisee's share as recorded ON THE ORDER. This used to read only
            // FranchiseCommissionEntries — a separate ledger the franchise order flow never writes
            // (empty in production), so every franchise discount reported as ₹0 while the orders
            // themselves carried the real figure. FranchiseShareAmount is the same value the
            // re-invoice and franchisee reports treat as authoritative.
            OrderShare = o.FranchiseShareAmount,
            // Kept as a fallback for any order predating that column, so nothing regresses.
            EntryShare = o.Source == OrderSource.Franchisee
                ? _db.FranchiseCommissionEntries.Where(e => e.OrderId == o.Id).Sum(e => (decimal?)e.CommissionAmount).GetValueOrDefault()
                : 0m,

            Total = o.TotalAmount,
            Gst = o.GstAmount
        }).ToListAsync();   // small column set; the COUNT(rows) cap is whatever the filter narrows to.

        // Resolved once per row so the discount tile and the net tile can never disagree.
        var rows = agg.Select(x => new
        {
            x.Gross,
            Student = Math.Max(0m, x.StudentDisc),
            Franchise = Math.Max(0m, x.OrderShare > 0 ? x.OrderShare : x.EntryShare),
            x.Total,
            x.Gst,
        }).ToList();

        var total = rows.Count;
        var grossSum = rows.Sum(x => x.Gross);
        var studentSum = rows.Sum(x => x.Student);
        var franchiseSum = rows.Sum(x => x.Franchise);

        // Net is what the institute actually keeps: the franchisee settles TotalAmount minus their
        // share, so that share is a real deduction, not a display-only figure. Reading it straight
        // off TotalAmount made the summary bar contradict itself — Gross − Student − Franchise did
        // not equal Net whenever a franchise order was in the filter. Derived from TotalAmount
        // rather than the stored FranchiseNetPayable because that column is only populated on
        // franchise orders (zero on website/counter rows).
        var netSum = rows.Sum(x => x.Total - x.Franchise);

        // Use stored GST when present, fall back to spec formula (Net × 18/118) when not.
        var gstSum = rows.Sum(x => x.Gst > 0 ? x.Gst : Math.Round(x.Total * 18m / 118m, 2));
        var taxableSum = netSum - gstSum;

        return new OrderFinanceSummary(
            total,
            Math.Round(grossSum, 2),
            Math.Round(studentSum, 2),
            Math.Round(franchiseSum, 2),
            Math.Round(netSum, 2),
            Math.Round(gstSum, 2),
            Math.Round(taxableSum, 2));
    }


    /// <summary>
    /// Updates one order's billing and shipping address.
    ///
    /// <para>Address-only: nothing about items, totals, status or payment travels with the request.
    /// The one thing that does move is the CGST/SGST/IGST split, because the billing STATE is what
    /// decides it — leaving the split alone after a state change would make the order's own tax
    /// figures contradict the address printed beside them.</para>
    ///
    /// <para>Once a tax invoice exists the state is frozen. The invoice snapshots its own address and
    /// its own tax split at issue time and is not rewritten here — silently restating an issued tax
    /// document is not something an address form should do. Every other field stays editable, because
    /// a wrong street or PIN still has to be fixable for dispatch.</para>
    /// </summary>
    public async Task<(bool ok, string? error)> UpdateAddressesAsync(OrderAddressEdit req, Guid? actorId, CancellationToken ct = default)
    {
        var o = await _db.Orders.FirstOrDefaultAsync(x => x.Id == req.OrderId, ct);
        if (o == null) return (false, "Order not found.");

        string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        var billingPin = Clean(req.BillingPincode);
        var shippingPin = Clean(req.ShippingPincode);
        if (billingPin != null && !PinCodePattern.IsMatch(billingPin))
            return (false, "Billing PIN code must be 6 digits.");
        if (shippingPin != null && !PinCodePattern.IsMatch(shippingPin))
            return (false, "Shipping PIN code must be 6 digits.");

        var newBillingState = Clean(req.BillingState);
        var stateChanged = !string.Equals(o.BillingState?.Trim(), newBillingState, StringComparison.OrdinalIgnoreCase);

        var hasInvoice = await _db.Set<Invoice>().AnyAsync(i => i.OrderId == o.Id, ct);
        if (stateChanged && hasInvoice)
            return (false, $"A tax invoice has already been issued for this order with billing state "
                         + $"“{o.BillingState ?? "—"}”. The state sets CGST+SGST vs IGST, so it cannot be changed now — "
                         + "raise a credit note and a fresh order instead. Every other address field can still be edited.");

        // A shipping address is either wholly present or wholly absent; a lone city with no street is
        // worse than falling back to the billing address, which is what null means downstream.
        var shipAddress = Clean(req.ShippingAddress);
        var shipCity = Clean(req.ShippingCity);
        var shipState = Clean(req.ShippingState);
        var anyShipping = shipAddress != null || shipCity != null || shipState != null || shippingPin != null;
        if (anyShipping && shipAddress == null)
            return (false, "Enter the shipping street address, or clear every shipping field to ship to the billing address.");

        var changes = new List<string>();
        void Track(string field, string? before, string? after)
        {
            if (!string.Equals(before, after, StringComparison.Ordinal))
                changes.Add($"{field}: {(string.IsNullOrWhiteSpace(before) ? "(none)" : before)} -> {(string.IsNullOrWhiteSpace(after) ? "(none)" : after)}");
        }

        Track("BillingName", o.BillingName, Clean(req.BillingName));
        Track("BillingAddress", o.BillingAddress, Clean(req.BillingAddress));
        Track("BillingCity", o.BillingCity, Clean(req.BillingCity));
        Track("BillingState", o.BillingState, newBillingState);
        Track("BillingPincode", o.BillingPincode, billingPin);
        Track("ShippingAddress", o.ShippingAddress, shipAddress);
        Track("ShippingCity", o.ShippingCity, shipCity);
        Track("ShippingState", o.ShippingState, shipState);
        Track("ShippingPincode", o.ShippingPincode, shippingPin);

        if (changes.Count == 0) return (true, null);

        o.BillingName = Clean(req.BillingName);
        o.BillingAddress = Clean(req.BillingAddress);
        o.BillingCity = Clean(req.BillingCity);
        o.BillingState = newBillingState;
        o.BillingPincode = billingPin;
        o.ShippingAddress = shipAddress;
        o.ShippingCity = shipCity;
        o.ShippingState = shipState;
        o.ShippingPincode = shippingPin;

        if (stateChanged)
        {
            // Re-split the SAME tax between CGST+SGST and IGST. The amount of GST and the total the
            // customer pays are untouched — only which heads it sits under, which is exactly what the
            // billing state governs.
            var gst = o.GstAmount > 0 ? o.GstAmount : o.CgstAmount + o.SgstAmount + o.IgstAmount;
            if (gst > 0)
            {
                if (string.Equals(newBillingState, SellerStateName, StringComparison.OrdinalIgnoreCase))
                {
                    var half = Math.Round(gst / 2m, 2);
                    o.CgstAmount = half;
                    o.SgstAmount = gst - half;   // absorb rounding into SGST so the parts sum to gst
                    o.IgstAmount = 0m;
                }
                else
                {
                    o.CgstAmount = 0m;
                    o.SgstAmount = 0m;
                    o.IgstAmount = gst;
                }
                changes.Add($"GST re-split for {newBillingState ?? "(none)"}: CGST={o.CgstAmount}, SGST={o.SgstAmount}, IGST={o.IgstAmount}");
            }
        }

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorId, "Admin", "OrderAddressUpdated", "Order", o.Id.ToString(),
            $"OrderNumber={o.OrderNumber}; " + string.Join("; ", changes));

        return (true, null);
    }

    /// <summary>Where the institute is registered — an order billed here is intra-state (CGST+SGST).</summary>
    private const string SellerStateName = "Maharashtra";

    private static readonly System.Text.RegularExpressions.Regex PinCodePattern =
        new(@"^[1-9][0-9]{5}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public async Task<OrderDetail?> GetDetailAsync(Guid id)
    {
        // IgnoreQueryFilters so a soft-deleted order can still be opened (e.g. from the recycle bin).
        var o = await _db.Orders.IgnoreQueryFilters()
            .Include(x => x.Items).ThenInclude(i => i.Product).ThenInclude(p => p.PrimaryFaculty)
            .Include(x => x.Items).ThenInclude(i => i.Product).ThenInclude(p => p.Images)
            .Include(x => x.Payments)
            .Include(x => x.Franchise)
            .Include(x => x.Invoice)
            .Include(x => x.CreatedBy)      // the staff member who raised the order
            .FirstOrDefaultAsync(x => x.Id == id);
        if (o == null) return null;

        // This order's dispatch record, matched on OrderId alone — the unique index on that column
        // means there is at most one. Deliberately NOT resolved through the customer, their phone,
        // their email or a product: those all risk showing a different order's consignment. Read
        // only; the dispatch screen stays the single place shipments are created and edited.
        var shipment = await _db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.OrderId == id);

        var productIds = o.Items.Select(i => i.ProductId).Distinct().ToList();
        var facultyCommission = await ComputeFacultyCommissionAsync(o.Items);

        decimal franchiseCommission = 0;
        // Franchise-view display block — per-line share + net-based tax. Built only for franchise orders.
        var lineShareByItemId = new Dictionary<Guid, decimal>();
        decimal franchiseDiscountTotal = 0, franchiseNet = 0, fGst = 0, fCgst = 0, fSgst = 0, fIgst = 0, fTaxable = 0;
        if (o.Source == OrderSource.Franchisee)
        {
            // In the net-of-share model the franchisee keeps their share by paying less up front,
            // so the order's FranchiseShareAmount IS the commission. Older orders may instead have
            // a commission ledger entry — fall back to that when no share was recorded on the order.
            franchiseCommission = o.FranchiseShareAmount > 0
                ? o.FranchiseShareAmount
                : await _db.FranchiseCommissionEntries
                    .Where(e => e.OrderId == o.Id)
                    .SumAsync(e => (decimal?)e.CommissionAmount) ?? 0m;

            // Per-line share: the commission entries already store CommissionAmount per OrderItemId
            // (same calculator used at order time). Map them onto the lines so the products grid can
            // show each product's own franchisee discount.
            lineShareByItemId = await _db.FranchiseCommissionEntries
                .Where(e => e.OrderId == o.Id)
                .GroupBy(e => e.OrderItemId)
                .Select(g => new { OrderItemId = g.Key, Share = g.Sum(x => x.CommissionAmount) })
                .ToDictionaryAsync(x => x.OrderItemId, x => x.Share);

            // Discount total: prefer the summed per-line shares; fall back to the order's stored
            // share, then the commission figure, so the card always reconciles.
            franchiseDiscountTotal = lineShareByItemId.Count > 0
                ? lineShareByItemId.Values.Sum()
                : (o.FranchiseShareAmount > 0 ? o.FranchiseShareAmount : franchiseCommission);
            franchiseDiscountTotal = Math.Min(franchiseDiscountTotal, o.Subtotal);   // never below zero net

            // If there are no per-item commission entries (e.g. counter-created franchise orders that
            // only stored an order-level share), distribute the discount across the lines in
            // proportion to each line's gross so the products grid reconciles with the total. The
            // last line absorbs any rounding remainder.
            if (lineShareByItemId.Count == 0 && franchiseDiscountTotal > 0)
            {
                var gross = o.Items.ToDictionary(i => i.Id, i => i.UnitPrice * i.Quantity);
                var grossTotal = gross.Values.Sum();
                if (grossTotal > 0)
                {
                    var ordered = o.Items.OrderBy(i => i.Id).ToList();
                    decimal running = 0;
                    for (var idx = 0; idx < ordered.Count; idx++)
                    {
                        var item = ordered[idx];
                        decimal share;
                        if (idx == ordered.Count - 1)
                            share = Math.Round(franchiseDiscountTotal - running, 2);   // remainder
                        else
                        {
                            share = Math.Round(franchiseDiscountTotal * gross[item.Id] / grossTotal, 2);
                            running += share;
                        }
                        lineShareByItemId[item.Id] = share;
                    }
                }
            }

            // Net = subtotal − franchisee discount. Tax is recomputed on the NET (GST-inclusive),
            // split CGST/SGST intra-Maharashtra vs IGST inter-state — mirrors OrderCalculationService.
            franchiseNet = Math.Round(o.Subtotal - franchiseDiscountTotal, 2);
            fGst = Math.Round(franchiseNet * 18m / 118m, 2);
            var intraState = string.Equals((o.BillingState ?? "Maharashtra").Trim(), "Maharashtra", StringComparison.OrdinalIgnoreCase);
            if (intraState) { fCgst = Math.Round(fGst / 2m, 2); fSgst = fGst - fCgst; }
            else { fIgst = fGst; }
            fTaxable = Math.Round(franchiseNet - fGst, 2);

            // Faculty is paid out of what the COMPANY retains, not the gross — otherwise the
            // franchisee share + faculty share can exceed the order value. Scale faculty earnings
            // by the company-share ratio (company share / gross total).
            if (o.TotalAmount > 0 && o.FranchiseShareAmount > 0)
            {
                var companyShare = o.TotalAmount - o.FranchiseShareAmount;
                facultyCommission = Math.Round(facultyCommission * companyShare / o.TotalAmount, 2);
            }
        }

        return new OrderDetail
        {
            Id = o.Id, OrderNumber = o.OrderNumber, CreatedAt = o.CreatedAt, UserId = o.UserId,
            StudentName = o.StudentName, StudentPhone = o.StudentPhone, StudentEmail = o.StudentEmail, StudentCity = o.StudentCity,
            Source = o.Source, Status = o.Status, PaymentStatus = o.PaymentStatus, PaymentMode = o.PaymentMode,
            GatewayPaymentMode = o.GatewayPaymentMode,
            Enrollment = o.Items.Any(i => i.IsActivated) ? EnrollmentStatus.Active : EnrollmentStatus.NotActivated,
            CouponCode = o.CouponCode, InternalNotes = o.InternalNotes,
            Subtotal = o.Subtotal, DiscountAmount = o.DiscountAmount, GstAmount = o.GstAmount,
            CgstAmount = o.CgstAmount, SgstAmount = o.SgstAmount, IgstAmount = o.IgstAmount, TotalAmount = o.TotalAmount,
            BillingName = o.BillingName, BillingAddress = o.BillingAddress, BillingCity = o.BillingCity,
            BillingState = o.BillingState, BillingPincode = o.BillingPincode, GstNumber = o.GstNumber, GstClassification = o.GstClassification,
            ShippingAddress = o.ShippingAddress, ShippingCity = o.ShippingCity,
            ShippingState = o.ShippingState, ShippingPincode = o.ShippingPincode,
            // Drives the frozen-state notice on the address form: an issued tax invoice fixes the
            // billing state, because it already states CGST+SGST or IGST.
            HasInvoice = o.Invoice != null,
            // Dispatch — mirrors what the dispatch board shows for this same order, including its
            // "no record yet means Pending" default.
            HasShipment = shipment != null,
            ShipmentStatus = shipment?.Status ?? ShipmentStatus.Pending,
            ShipmentCourier = shipment?.Courier,
            ShipmentTrackingNumber = shipment?.TrackingNumber,
            DispatchedAt = shipment?.DispatchedAt,
            DeliveredAt = shipment?.DeliveredAt,
            FranchiseName = o.Franchise != null ? o.Franchise.Name : null,
            FranchiseCommission = franchiseCommission,
            FacultyCommission = facultyCommission,
            IsFranchiseView = o.Source == OrderSource.Franchisee,
            FranchiseDiscountTotal = franchiseDiscountTotal,
            FranchiseNetAmount = franchiseNet,
            FranchiseGst = fGst,
            FranchiseCgst = fCgst,
            FranchiseSgst = fSgst,
            FranchiseIgst = fIgst,
            FranchiseTaxable = fTaxable,
            ReferredByName = o.ReferralSourceName,
            ReferralType = o.ReferralType,
            ReferralCustomText = o.ReferralCustomText,
            // Prefer the staff member's full name; fall back to the login email so the row is never
            // blank for an account that has no name filled in.
            CreatedByName = o.CreatedBy != null
                ? (string.IsNullOrWhiteSpace(o.CreatedBy.FullName) ? o.CreatedBy.Email : o.CreatedBy.FullName)
                : null,
            IsDeleted = o.IsDeleted, InvoiceNumber = o.Invoice != null ? o.Invoice.InvoiceNumber : null,
            Items = o.Items.Select(i => new OrderItemLine
            {
                Id = i.Id, ProductId = i.ProductId, ProductTitle = i.ProductTitle, ModeName = i.ModeName,
                Sku = i.Product != null ? i.Product.Sku : null,
                ImageUrl = i.Product != null ? i.Product.Images.Where(im => im.IsPrimary).Select(im => im.ImageUrl).FirstOrDefault()
                                                ?? i.Product.Images.OrderBy(im => im.DisplayOrder).Select(im => im.ImageUrl).FirstOrDefault() : null,
                FacultyName = i.Product != null && i.Product.PrimaryFaculty != null ? i.Product.PrimaryFaculty.DisplayName : null,
                BatchType = i.Product != null ? i.Product.CourseType.ToString() : null,
                Duration = i.Product != null ? i.Product.TotalHours : null,
                Quantity = i.Quantity, UnitPrice = i.UnitPrice, Discount = i.Discount, LineTotal = i.LineTotal,
                FranchiseShare = lineShareByItemId.TryGetValue(i.Id, out var sh) ? sh : 0m
            }).ToList(),
            Payments = o.Payments.OrderByDescending(p => p.CreatedAt)
                .Select(p => new OrderPaymentLine(p.Amount, p.PaymentMode, p.Status, p.GatewayPaymentId ?? p.BankRef, p.PaidAt,
                    p.GatewayPaymentMode)).ToList()
        };
    }

    // Faculty revenue share for an order's products (from FacultySharingRules): % of line or fixed per rule.
    private async Task<decimal> ComputeFacultyCommissionAsync(ICollection<OrderItem> items)
    {
        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        if (productIds.Count == 0) return 0;
        var rules = await _db.FacultySharingRules.Where(r => r.IsActive && productIds.Contains(r.ProductId)).ToListAsync();
        if (rules.Count == 0) return 0;
        decimal total = 0;
        foreach (var i in items)
            foreach (var r in rules.Where(r => r.ProductId == i.ProductId))
                total += r.ShareType == SharingType.Percentage ? Math.Round(i.LineTotal * r.ShareValue / 100m, 2) : r.ShareValue;
        return total;
    }

    public async Task<string> CreateAsync(CreateOrderRequest r)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == r.ProductId)
            ?? throw new InvalidOperationException("Selected product not found.");

        var unit = r.UnitPrice > 0 ? r.UnitPrice : product.SellingPrice;
        var subtotal = unit;
        var discount = Math.Min(Math.Max(0, r.Discount), subtotal);
        var total = subtotal - discount;
        var gst = Math.Round(total * 18m / 118m, 2);

        var order = new Order
        {
            OrderNumber = await GenerateOrderNumberAsync(),
            StudentName = r.StudentName.Trim(),
            StudentPhone = r.StudentPhone.Trim(),
            StudentEmail = r.StudentEmail,
            StudentCity = r.StudentCity,
            Source = r.Source,
            PaymentMode = r.PaymentMode,
            Subtotal = subtotal,
            DiscountAmount = discount,
            GstAmount = gst,
            TotalAmount = total,
            Status = r.Status,
            PaymentStatus = r.Status == OrderStatus.Confirmed ? PaymentStatus.Success : PaymentStatus.Pending,
            InternalNotes = r.InternalNotes,
            ConfirmedAt = r.Status == OrderStatus.Confirmed ? DateTime.UtcNow : null
        };
        order.Items.Add(new OrderItem
        {
            ProductId = product.Id,
            ProductTitle = product.Title,
            ModeName = r.ModeName,
            Quantity = 1,
            UnitPrice = unit,
            Discount = discount,
            GstRate = product.GstRate,
            LineTotal = total
        });

        _db.Orders.Add(order);
        if (r.Status is OrderStatus.Confirmed or OrderStatus.Activated or OrderStatus.Delivered)
            product.TotalOrders++;
        await _db.SaveChangesAsync();
        await _notify.NotifyAsync(AdminNotificationType.NewOrder, NotificationSeverity.Success,
            "New order", $"Order #{order.OrderNumber} · ₹{order.TotalAmount:N0} · {order.StudentName}", $"/admin/orders/{order.Id}", order.Id.ToString());

        // Counter orders created already-paid need the same post-payment hook CreateOrderV2Async runs,
        // otherwise no invoice exists until someone opens the print page.
        if (order.Status is OrderStatus.Confirmed or OrderStatus.Activated or OrderStatus.Delivered)
            await TryEnqueueSerialKeysAsync(order.Id, order.OrderNumber);

        Touch("orders", "dashboard");
        return order.OrderNumber;
    }

    public async Task<(bool ok, string? error)> UpdateStatusAsync(Guid id, OrderStatus status, Guid? actorId = null, string? actorName = null)
    {
        if (status == OrderStatus.Cancelled && !await IsSuperAdminAsync(actorId))
            return (false, SuperAdminOnly);

        var o = await _db.Orders.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
        if (o == null) return (false, "Order not found.");
        var old = o.Status;
        if (old == status) return (true, null);   // already there — nothing to do, not an error
        o.Status = status;
        if (status == OrderStatus.Confirmed && o.ConfirmedAt == null) o.ConfirmedAt = DateTime.UtcNow;
        // Keep PaymentStatus consistent with the order status so we never end up with the
        // "Payment Pending but Order Confirmed" mismatch. Confirming an order means it's paid;
        // cancelling/refunding a not-yet-successful order leaves payment as Failed.
        if (status == OrderStatus.Confirmed && o.PaymentStatus != PaymentStatus.Success)
        {
            o.PaymentStatus = PaymentStatus.Success;
            if (o.ActivatedAt == null) o.ActivatedAt = DateTime.UtcNow;
        }
        if (status == OrderStatus.Cancelled) o.CancelledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName ?? "system", "OrderStatusChanged", EntityType, o.Id.ToString(),
            JsonSerializer.Serialize(new { o.OrderNumber, From = old.ToString(), To = status.ToString() }));
        if (status == OrderStatus.Refunded)
            await _notify.NotifyAsync(AdminNotificationType.Refund, NotificationSeverity.Warning,
                "Order refunded", $"Order #{o.OrderNumber} was marked refunded.", $"/admin/orders/{o.Id}", o.Id.ToString());

        // Customer-facing 'order_status_updated' fan-out (Email + SMS via the active templates).
        await _customerNotify.SendAsync("order_status_updated",
            new NotificationRecipient(o.StudentEmail, o.StudentPhone),
            new Dictionary<string, string>
            {
                ["name"] = o.StudentName,
                ["order_number"] = o.OrderNumber,
                ["status"] = status.ToString(),
                ["previous_status"] = old.ToString()
            });

        // Serial keys: enqueue when the order transitions INTO a paid/confirmed state. Idempotent —
        // re-runs from later status flips do nothing because EnqueueForOrderAsync skips already-enqueued items.
        var becamePaid = status is OrderStatus.Confirmed or OrderStatus.Activated or OrderStatus.Delivered
                      && old is not (OrderStatus.Confirmed or OrderStatus.Activated or OrderStatus.Delivered);
        if (becamePaid) await TryEnqueueSerialKeysAsync(o.Id, o.OrderNumber);

        // Faculty share becomes earned at the same moment the order becomes revenue. An order that
        // was created Pending and confirmed later would otherwise never get a ledger row, and would
        // silently fall through to the recompute-from-current-rules path forever.
        if (becamePaid)
        {
            try { await _facultySharing.RecordOrderFacultyShareAsync(o.Id); }
            catch (Exception ex)
            {
                _log.LogError(ex, "Faculty share ledger write threw on status change orderNumber={OrderNumber}", o.OrderNumber);
            }
        }

        Touch("orders", "dashboard");
        return (true, null);
    }

    // ── Notes ──
    public async Task<List<OrderNoteItem>> ListNotesAsync(Guid orderId) =>
        await _db.OrderNotes.Where(n => n.OrderId == orderId)
            .OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.CreatedAt)
            .Select(n => new OrderNoteItem(n.Id, n.Body, n.IsCustomerVisible, n.IsPinned, n.CreatedByName, n.CreatedAt))
            .ToListAsync();

    public async Task<Guid> AddNoteAsync(Guid orderId, AddOrderNoteRequest req, Guid? actorId, string actorName)
    {
        if (string.IsNullOrWhiteSpace(req.Body)) throw new InvalidOperationException("Note cannot be empty.");
        var note = new OrderNote
        {
            OrderId = orderId, Body = req.Body.Trim(), IsCustomerVisible = req.IsCustomerVisible,
            IsPinned = req.IsPinned, CreatedByUserId = actorId, CreatedByName = string.IsNullOrWhiteSpace(actorName) ? "system" : actorName
        };
        _db.OrderNotes.Add(note);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, "OrderNoteAdded", EntityType, orderId.ToString(),
            JsonSerializer.Serialize(new { note.IsCustomerVisible, note.IsPinned, Preview = Trunc(note.Body, 80) }));
        return note.Id;
    }

    public async Task DeleteNoteAsync(Guid noteId, Guid? actorId, string actorName)
    {
        var note = await _db.OrderNotes.FirstOrDefaultAsync(n => n.Id == noteId);
        if (note == null) return;
        _db.OrderNotes.Remove(note);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, "OrderNoteDeleted", EntityType, note.OrderId.ToString(),
            JsonSerializer.Serialize(new { Preview = Trunc(note.Body, 80) }));
    }

    public async Task ToggleNotePinAsync(Guid noteId)
    {
        var note = await _db.OrderNotes.FirstOrDefaultAsync(n => n.Id == noteId);
        if (note == null) return;
        note.IsPinned = !note.IsPinned;
        await _db.SaveChangesAsync();
    }

    // ── Soft delete / restore ──
    public async Task<(bool ok, string? error)> SoftDeleteAsync(Guid id, Guid? actorId, string actorName)
    {
        if (!await IsSuperAdminAsync(actorId)) return (false, SuperAdminOnly);

        var o = await _db.Orders.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (o == null) return (false, "Order not found.");
        o.IsDeleted = true;
        o.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, "OrderDeleted", EntityType, o.Id.ToString(),
            JsonSerializer.Serialize(new { o.OrderNumber }));
        Touch("orders", "dashboard");
        return (true, null);
    }

    public async Task<bool> RestoreAsync(Guid id, Guid? actorId, string actorName)
    {
        var o = await _db.Orders.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted);
        if (o == null) return false;
        o.IsDeleted = false;
        o.DeletedAt = null;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, "OrderRestored", EntityType, o.Id.ToString(),
            JsonSerializer.Serialize(new { o.OrderNumber }));
        Touch("orders", "dashboard");
        return true;
    }

    // ── Enrollment activation (mirrors checkout: mark items activated + grant enrollments idempotently) ──
    public async Task<int> ActivateEnrollmentAsync(Guid id, Guid? actorId, string actorName)
    {
        var o = await _db.Orders.IgnoreQueryFilters().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (o == null) return 0;

        var now = DateTime.UtcNow;
        var granted = 0;
        foreach (var item in o.Items)
        {
            if (!item.IsActivated) { item.IsActivated = true; item.ActivatedAt = now; }
            if (o.UserId.HasValue)
            {
                var exists = await _db.Enrollments.AnyAsync(e => e.UserId == o.UserId.Value && e.OrderItemId == item.Id);
                if (!exists)
                {
                    _db.Enrollments.Add(new Enrollment { UserId = o.UserId.Value, ProductId = item.ProductId, OrderItemId = item.Id, IsActive = true });
                    granted++;
                }
            }
        }
        if (o.ActivatedAt == null) o.ActivatedAt = now;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, "OrderEnrollmentActivated", EntityType, o.Id.ToString(),
            JsonSerializer.Serialize(new { o.OrderNumber, EnrollmentsGranted = granted }));
        await _notify.NotifyAsync(AdminNotificationType.EnrollmentActivated, NotificationSeverity.Info,
            "Enrollment activated", $"Order #{o.OrderNumber} — course access activated.", $"/admin/orders/{o.Id}", o.Id.ToString());
        Touch("orders", "dashboard");
        return granted;
    }

    // ── Resend the order confirmation ──
    // Delegates to the SAME sender checkout uses (INotificationSender.SendOrderConfirmationAsync)
    // rather than rebuilding the token set here. A resend that quietly differs from the original
    // is worse than no resend at all — the customer compares the two mails side by side.
    public async Task<(bool ok, string? error, string? sentTo)> ResendOrderEmailAsync(
        Guid id, Guid? actorId, string actorName, CancellationToken ct = default)
    {
        var o = await _db.Orders.IgnoreQueryFilters()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (o == null) return (false, "Order not found.", null);
        if (o.IsDeleted) return (false, "This order is in the recycle bin.", null);

        // The confirmation email states that the order is confirmed and quotes the amount paid, so
        // it must not go out before the money has actually arrived.
        if (o.PaymentStatus != PaymentStatus.Success)
            return (false, "Only paid orders can have the confirmation email resent.", null);

        if (string.IsNullOrWhiteSpace(o.StudentEmail))
            return (false, "This order has no email address. Add one on the order first.", null);

        try
        {
            await _sender.SendOrderConfirmationAsync(o);
        }
        catch (Exception ex)
        {
            // Never surface a raw exception to the admin, and never let a mail failure look like a
            // success — the whole point of this button is knowing the customer got the mail.
            _log.LogError(ex, "Resend order confirmation failed for order {OrderNumber}", o.OrderNumber);
            return (false, "Could not send the email. Please try again in a moment.", null);
        }

        await _audit.LogAsync(actorId, actorName, "OrderEmailResent", EntityType, o.Id.ToString(),
            JsonSerializer.Serialize(new { o.OrderNumber, SentTo = o.StudentEmail }));

        return (true, null, o.StudentEmail);
    }

    // ── Invoice ──
    // NOTE: invoices are only generated for PAID orders (PaymentStatus == Success). For unpaid
    // orders this returns a preview InvoiceView WITHOUT persisting anything, so the print page
    // can still render a draft but no DB row is created.
    //
    // Numbering and the snapshot are owned entirely by IInvoiceService — this page never mints its
    // own number. That keeps one continuous RIO-INV-yyyyMM-#### series across website, counter and
    // franchisee orders, and gives franchise invoices the correct billed-party and net-payable
    // treatment that only IInvoiceService applies.
    public async Task<InvoiceView?> GetOrCreateInvoiceAsync(Guid id, Guid? actorId, string actorName)
    {
        var o = await _db.Orders.IgnoreQueryFilters().Include(x => x.Items).Include(x => x.Invoice).Include(x => x.Franchise)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (o == null) return null;

        // Idempotent and self-gating: returns the existing invoice, or nothing at all when unpaid.
        if (o.Invoice == null && o.PaymentStatus == PaymentStatus.Success)
        {
            await _invoices.EnsureForOrderAsync(o.Id, actorId, actorName);
            // Re-read so the view below renders the number that was actually persisted.
            o.Invoice = await _db.Set<Invoice>()
                .FirstOrDefaultAsync(i => i.OrderId == o.Id && i.Status == InvoiceStatus.Active);
        }

        // Build the view from the persisted invoice if it exists, otherwise from the order
        // directly (unpaid preview — number shown as "DRAFT").
        return new InvoiceView
        {
            PaidFromWallet = o.PaidFromWallet,
            InvoiceNumber = o.Invoice?.InvoiceNumber
                ?? (o.PaidFromWallet ? "NOT INVOICED (paid from wallet)" : "DRAFT (unpaid)"),
            InvoiceDate = (o.Invoice?.InvoiceDate ?? DateOnly.FromDateTime(DateTime.UtcNow)).ToDateTime(TimeOnly.MinValue),
            OrderNumber = o.OrderNumber, OrderDate = o.CreatedAt,
            StudentName = o.StudentName, StudentPhone = o.StudentPhone, StudentEmail = o.StudentEmail,
            BillingAddress = o.BillingAddress, BillingCity = o.BillingCity, BillingState = o.BillingState,
            BillingPincode = o.BillingPincode, GstNumber = o.GstNumber, GstClassification = o.GstClassification ?? "B2C",
            FranchiseName = o.Franchise != null ? (string.IsNullOrWhiteSpace(o.Franchise.BusinessName) ? o.Franchise.Name : o.Franchise.BusinessName) : null,
            IsFranchiseOrder = o.Source == OrderSource.Franchisee && o.FranchiseId != null,
            FranchiseGstin = o.Franchise?.Gstin,
            FranchiseAddress = o.Franchise?.AddressLine,
            FranchiseCity = o.Franchise?.City,
            FranchiseState = o.Franchise?.State,
            FranchisePincode = o.Franchise?.PinCode,
            FranchiseShareAmount = o.FranchiseShareAmount,
            CompanyShareAmount = Math.Round(o.TotalAmount - o.FranchiseShareAmount, 2),
            NetPayable = o.FranchiseNetPayable > 0 ? o.FranchiseNetPayable : o.TotalAmount,
            PaymentStatus = o.PaymentStatus, PaymentMode = o.PaymentMode,
            // Prefer the frozen invoice snapshot; fall back to the order for the DRAFT (unpaid) preview.
            GatewayPaymentMode = o.Invoice?.GatewayPaymentMode ?? o.GatewayPaymentMode,
            Subtotal = o.Subtotal, DiscountAmount = o.DiscountAmount,
            TaxableAmount = o.Invoice?.TaxableAmount ?? Math.Round(o.TotalAmount - o.GstAmount, 2),
            CgstAmount = o.CgstAmount, SgstAmount = o.SgstAmount,
            IgstAmount = o.IgstAmount, TotalAmount = o.TotalAmount,
            Items = o.Items.Select(i => new OrderItemLine { ProductTitle = i.ProductTitle, ModeName = i.ModeName, Quantity = i.Quantity, UnitPrice = i.UnitPrice, LineTotal = i.LineTotal }).ToList()
        };
    }

    public Task<List<AuditLogItem>> GetHistoryAsync(Guid id) => _audit.ListForEntityAsync(EntityType, id.ToString(), 200);

    // ── Order item management ──
    public async Task<(bool ok, string? error)> UpdateItemAsync(Guid orderId, UpdateOrderItemRequest req, Guid? actorId, string actorName)
    {
        if (req.Quantity < 1) return (false, "Quantity must be at least 1.");
        if (req.UnitPrice < 0) return (false, "Unit price cannot be negative.");
        if (req.Discount < 0) return (false, "Discount cannot be negative.");
        if (req.Discount > req.UnitPrice * req.Quantity) return (false, "Discount cannot exceed the line amount.");

        var o = await _db.Orders.IgnoreQueryFilters().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return (false, "Order not found.");
        if (await IsFrozenForActorAsync(o, actorId)) return (false, FrozenOrderMessage);
        var item = o.Items.FirstOrDefault(i => i.Id == req.ItemId);
        if (item == null) return (false, "Item not found.");

        var before = new { item.Quantity, item.UnitPrice, item.Discount };
        item.Quantity = req.Quantity; item.UnitPrice = req.UnitPrice; item.Discount = req.Discount;
        item.LineTotal = (req.UnitPrice * req.Quantity) - req.Discount;
        RecalcInPlace(o);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorId, actorName, "OrderItemUpdated", EntityType, o.Id.ToString(),
            JsonSerializer.Serialize(new { item.ProductTitle, Before = before, After = new { item.Quantity, item.UnitPrice, item.Discount }, NewTotal = o.TotalAmount }));
        Touch("orders", "dashboard");
        return (true, null);
    }

    public async Task<(bool ok, string? error)> AddItemAsync(Guid orderId, AddOrderItemRequest req, Guid? actorId, string actorName)
    {
        if (req.Quantity < 1) return (false, "Quantity must be at least 1.");
        var o = await _db.Orders.IgnoreQueryFilters().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return (false, "Order not found.");
        if (await IsFrozenForActorAsync(o, actorId)) return (false, FrozenOrderMessage);
        var product = await _db.Products.Include(p => p.Modes).FirstOrDefaultAsync(p => p.Id == req.ProductId);
        if (product == null) return (false, "Product not found.");

        var mode = req.ModeId.HasValue ? product.Modes.FirstOrDefault(m => m.Id == req.ModeId) : null;
        var unit = mode?.Price ?? product.SellingPrice;
        o.Items.Add(new OrderItem
        {
            ProductId = product.Id, ProductModeId = mode?.Id, ProductTitle = product.Title, ModeName = mode?.ModeName,
            Quantity = req.Quantity, UnitPrice = unit, Discount = 0, GstRate = product.GstRate, LineTotal = unit * req.Quantity
        });
        RecalcInPlace(o);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorId, actorName, "OrderItemAdded", EntityType, o.Id.ToString(),
            JsonSerializer.Serialize(new { product.Title, Mode = mode?.ModeName, req.Quantity, Unit = unit, NewTotal = o.TotalAmount }));
        Touch("orders", "dashboard");
        return (true, null);
    }

    public async Task<(bool ok, string? error)> RemoveItemAsync(Guid orderId, Guid itemId, Guid? actorId, string actorName)
    {
        var o = await _db.Orders.IgnoreQueryFilters().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return (false, "Order not found.");
        if (await IsFrozenForActorAsync(o, actorId)) return (false, FrozenOrderMessage);
        var item = o.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null) return (false, "Item not found.");
        if (o.Items.Count <= 1) return (false, "An order must keep at least one item — cancel the order instead.");

        var title = item.ProductTitle;
        _db.OrderItems.Remove(item);
        o.Items.Remove(item);
        RecalcInPlace(o);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorId, actorName, "OrderItemRemoved", EntityType, o.Id.ToString(),
            JsonSerializer.Serialize(new { Product = title, NewTotal = o.TotalAmount }));
        Touch("orders", "dashboard");
        return (true, null);
    }

    public async Task<List<RioCommerce.Core.DTOs.Meta.IdName>> ListProductsForFilterAsync(string? q, int take = 50, Guid? facultyId = null)
    {
        // Active/Published products only — lightweight projection (Id + Title), ORDER BY Title for
        // a predictable alphabetical list in the order-list dropdown. Honours optional text search
        // (ILIKE on Title) so very large catalogues stream incrementally as the admin types.
        var query = _db.Products.Where(p => p.Status == ProductStatus.Active);

        // Cascade: with a faculty picked, offer only the courses they are mapped to teach. The text
        // search still applies on top — the two narrow together rather than replacing each other.
        var picked = CatalogCascade.Selection.Of(facultyId: facultyId);
        if (CatalogCascade.Constrains(picked, CatalogCascade.Dimension.Product))
        {
            var ids = CatalogCascade.ProductsMatching(_db, picked, CatalogCascade.Dimension.Product).Select(p => p.Id);
            query = query.Where(p => ids.Contains(p.Id));
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(p => EF.Functions.ILike(p.Title, $"%{s}%"));
        }
        return await query.OrderBy(p => p.Title)
            .Take(Math.Clamp(take, 1, 500))
            .Select(p => new RioCommerce.Core.DTOs.Meta.IdName(p.Id, p.Title))
            .ToListAsync();
    }

    public async Task<List<ProductPickItem>> SearchProductsAsync(string? q, int take = 12)
    {
        var query = _db.Products.Include(p => p.PrimaryFaculty).Include(p => p.Modes).Include(p => p.Images).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(p => EF.Functions.ILike(p.Title, $"%{s}%") || (p.Sku != null && EF.Functions.ILike(p.Sku, $"%{s}%")));
        }
        var products = await query.OrderByDescending(p => p.IsFeatured).ThenBy(p => p.Title).Take(take).ToListAsync();
        return products.Select(p => new ProductPickItem(
            p.Id, p.Title, p.Sku, p.SellingPrice,
            p.PrimaryFaculty != null ? p.PrimaryFaculty.DisplayName : null,
            p.Images.Where(im => im.IsPrimary).Select(im => im.ImageUrl).FirstOrDefault()
                ?? p.Images.OrderBy(im => im.DisplayOrder).Select(im => im.ImageUrl).FirstOrDefault(),
            p.Modes.Where(m => m.IsEnabled).OrderBy(m => m.DisplayOrder).Select(m => new ProductPickMode(m.Id, m.ModeName, m.Price)).ToList()
        )).ToList();
    }

    // Recompute line + order totals in-memory using the shared calculation engine (caller saves).
    private void RecalcInPlace(Order o)
    {
        foreach (var it in o.Items) it.LineTotal = (it.UnitPrice * it.Quantity) - it.Discount;
        var totals = _calc.Compute(o.Items.Select(i => new OrderLineInput(i.UnitPrice, i.Quantity, i.Discount)), o.BillingState);
        OrderCalculationService.Apply(o, totals);

        // A franchise order's invoice is raised for FranchiseNetPayable — what the franchisee
        // actually owes — so it has to track the total. One invariant covers both settlement models:
        // portal orders net the share off up front (share > 0), admin-created orders bill the full
        // amount and settle commission separately through the ledger (share = 0). Recomputing here
        // also keeps it right when an admin edits a line and the total moves.
        if (o.Source == OrderSource.Franchisee && o.FranchiseId != null)
            o.FranchiseNetPayable = Math.Max(0m, o.TotalAmount - o.FranchiseShareAmount);
    }

    /// <summary>Stamps the FRANCHISEE as the billing party on a franchise order. The student stays on
    /// the Student* fields — they receive the material and appear on the customer receipt — while the
    /// tax invoice is raised in the franchisee's legal name, at their address, against their GSTIN.
    /// Mirrors FranchisePortalService.BuildOrderAsync so an order placed by an admin and one placed
    /// in the Franchise Portal invoice identically.</summary>
    private static void ApplyFranchiseBillingParty(Order order, Franchise f)
    {
        var legalName = !string.IsNullOrWhiteSpace(f.BusinessName) ? f.BusinessName!.Trim() : f.Name.Trim();
        var isB2B = !string.IsNullOrWhiteSpace(f.Gstin);

        order.CustomerType = isB2B ? CustomerType.Organization : CustomerType.Individual;
        order.OrgName = isB2B ? legalName : null;
        order.BillingName = legalName;
        order.BillingAddress = Blank(f.AddressLine);
        order.BillingCity = Blank(f.City);
        // Place of supply follows the BILLED party, so the tax split is decided by the franchisee's
        // state — not the student's. Keep whatever was already set if the franchise has no state.
        order.BillingState = Blank(f.State) ?? order.BillingState;
        order.BillingPincode = Blank(f.PinCode);
        order.GstNumber = isB2B ? f.Gstin!.Trim().ToUpperInvariant() : null;
        order.GstClassification = isB2B ? "B2B" : "B2C";
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    private async Task<string> GenerateOrderNumberAsync()
    {
        var last = await _db.Orders.IgnoreQueryFilters().Where(o => o.OrderNumber.StartsWith("RIO"))
            .OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync();
        var next = 1043;
        if (last != null && int.TryParse(last.OrderNumber.Split('-').Last(), out var n)) next = n + 1;
        return $"RIO-{next}";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //   V2 admin Create Order — wizard support
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<ProductPickDetail?> GetProductPickDetailAsync(Guid productId)
    {
        var p = await _db.Products
            .Include(x => x.PrimaryFaculty)
            .Include(x => x.Images)
            .Include(x => x.Modes)
            .FirstOrDefaultAsync(x => x.Id == productId);
        if (p == null) return null;

        var mappings = await _db.ProductAttributeMappings
            .Include(m => m.ProductAttribute)
            .Where(m => m.ProductId == productId)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync();

        var mappingIds = mappings.Select(m => m.Id).ToList();
        var values = mappingIds.Count == 0
            ? new List<ProductAttributeValue>()
            : await _db.ProductAttributeValues
                .Where(v => mappingIds.Contains(v.ProductAttributeMappingId))
                .OrderBy(v => v.DisplayOrder).ToListAsync();

        var attributes = mappings.Select(m => new ProductPickAttributeGroup(
            m.Id,
            string.IsNullOrWhiteSpace(m.TextPrompt) ? m.ProductAttribute.Name : m.TextPrompt!,
            m.ControlType.ToString(),
            m.IsRequired,
            values.Where(v => v.ProductAttributeMappingId == m.Id)
                  .Select(v => new ProductPickAttributeValue(v.Id, v.Name, v.PriceAdjustment, v.PriceAdjustmentUsePercentage, v.IsPreSelected))
                  .ToList()
        )).ToList();

        var modes = p.Modes.Where(m => m.IsEnabled).OrderBy(m => m.DisplayOrder)
            .Select(m => new ProductPickMode(m.Id, m.ModeName, m.Price)).ToList();

        var img = p.Images.Where(im => im.IsPrimary).Select(im => im.ImageUrl).FirstOrDefault()
               ?? p.Images.OrderBy(im => im.DisplayOrder).Select(im => im.ImageUrl).FirstOrDefault();

        var batchText = p.BatchStatus switch
        {
            BatchStatus.Upcoming   => "Upcoming Batch",
            BatchStatus.Ongoing    => "Ongoing Batch",
            BatchStatus.PreRecorded=> "Pre-Recorded",
            BatchStatus.ComingSoon => "Coming Soon",
            BatchStatus.OutOfStock => "Out Of Stock",
            _ => p.BatchStatus.ToString()
        };

        return new ProductPickDetail(
            p.Id, p.Title, p.Sku, p.PrimaryFaculty?.DisplayName, batchText,
            img, p.SellingPrice, p.Mrp, p.GstRate,
            modes, attributes);
    }

    public async Task<Guid> CreateOrderV2Async(CreateOrderRequestV2 req, Guid? actorId, string? actorName)
    {
        if (req == null) throw new ArgumentNullException(nameof(req));
        if (string.IsNullOrWhiteSpace(req.StudentName)) throw new InvalidOperationException("Student name is required.");
        if (string.IsNullOrWhiteSpace(req.StudentPhone)) throw new InvalidOperationException("Student phone is required.");
        if (req.Items == null || req.Items.Count == 0) throw new InvalidOperationException("Please add at least one product.");
        if (req.ReferralSourceId == null) throw new InvalidOperationException("Referral source is required.");
        // Installment orders collect only a down payment up front, so the split-payment list may be
        // empty; otherwise at least one payment row is required.
        if (req.Installment is not { Enabled: true } && (req.Payments == null || req.Payments.Count == 0))
            throw new InvalidOperationException("Please add at least one payment row.");

        // ── Load every product (+ modes + attribute mappings + values) the order references ──
        var productIds = req.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Include(p => p.Modes)
            .Include(p => p.OptionGroups).ThenInclude(g => g.Items)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);
        foreach (var id in productIds)
            if (!products.ContainsKey(id)) throw new InvalidOperationException("One of the selected products no longer exists.");

        var allAttrValueIds = req.Items.SelectMany(i => i.SelectedAttributeValueIds).Distinct().ToList();
        var attrValues = allAttrValueIds.Count == 0
            ? new List<ProductAttributeValue>()
            : await _db.ProductAttributeValues
                .Include(v => v.ProductAttributeMapping).ThenInclude(m => m.ProductAttribute)
                .Where(v => allAttrValueIds.Contains(v.Id))
                .ToListAsync();
        var attrValueById = attrValues.ToDictionary(v => v.Id);

        // ── Franchise context (optional) — when present the order is tagged with
        //    the FranchiseId and OrderSource is forced to Franchisee regardless of
        //    what the client sent. We probe the franchise here so a bad FranchiseId
        //    fails the call BEFORE we mutate anything else. ──
        Franchise? franchise = null;
        if (req.FranchiseId.HasValue)
        {
            franchise = await _db.Franchises.FirstOrDefaultAsync(f => f.Id == req.FranchiseId.Value);
            if (franchise == null) throw new InvalidOperationException("Selected franchisee not found.");
            if (!franchise.IsActive) throw new InvalidOperationException("Selected franchisee is inactive — orders cannot be placed against it.");
        }

        // ── Build the Order header ──
        var order = new Order
        {
            OrderNumber = await GenerateOrderNumberAsync(),
            UserId = req.UserId,
            StudentName = req.StudentName.Trim(),
            StudentPhone = req.StudentPhone.Trim(),
            StudentEmail = req.StudentEmail,
            StudentCity = req.BillingCity,
            BillingAddress = req.BillingAddress,
            BillingCity = req.BillingCity,
            BillingState = string.IsNullOrWhiteSpace(req.BillingState) ? "Maharashtra" : req.BillingState,
            BillingPincode = req.BillingPincode,
            GstNumber = req.GstNumber,
            Source = franchise != null ? OrderSource.Franchisee : req.Source,
            SourceNo = Blank(req.SourceNo),
            FranchiseId = franchise?.Id,
            // Wallet-funded franchise orders aren't invoiced — the top-up that funded them already
            // was. Net Payment / Postpay orders and all non-franchise orders invoice as before.
            PaidFromWallet = franchise != null && req.FranchiseTxnMode == FranchiseTxnMode.Wallet,
            Status = req.Status,
            PaymentStatus = req.Status == OrderStatus.Confirmed ? PaymentStatus.Success : PaymentStatus.Pending,
            ConfirmedAt = req.Status == OrderStatus.Confirmed ? DateTime.UtcNow : null,
            InternalNotes = req.InternalNotes,
            ReferralSourceId = req.ReferralSourceId,
            ReferralCustomText = req.ReferralCustomText,
            // The staff member who raised this order. Order.CreatedById has existed since the
            // beginning but nothing ever set it, so every counter order looked anonymous and there
            // was no way to tell which counsellor took it. Null for orders placed by customers
            // themselves on the storefront — that path has no actor.
            CreatedById = actorId,
        };

        // The address the admin typed is the STUDENT's — persist it as the shipping address (the
        // wizard collects it and nothing was storing it). This matters most on a franchise order:
        // the billing block is about to be replaced by the franchisee's details, so this is the
        // only place the student's delivery address survives.
        order.ShippingAddress = Blank(req.ShippingAddress) ?? Blank(req.BillingAddress);
        order.ShippingCity = Blank(req.ShippingCity) ?? Blank(req.BillingCity);
        order.ShippingState = Blank(req.ShippingState) ?? Blank(req.BillingState);
        order.ShippingPincode = Blank(req.ShippingPincode) ?? Blank(req.BillingPincode);

        // A franchise order is INVOICED TO THE FRANCHISEE, never to the student — the franchisee is
        // the buying party, the student is only the recipient of the material. Applied before the
        // totals are computed below, because the CGST/SGST vs IGST split reads BillingState.
        if (franchise != null) ApplyFranchiseBillingParty(order, franchise);

        // ── Build each line: resolve mode + apply attribute price adjustments ──
        foreach (var line in req.Items)
        {
            var product = products[line.ProductId];
            var mode = line.ProductModeId.HasValue ? product.Modes.FirstOrDefault(m => m.Id == line.ProductModeId.Value) : null;
            // Mode price is ADDITIVE — add-on to product's SellingPrice, not replacement.
            // Purchase-option add-ons (server-side lookup, one per group, tamper-safe) stack too.
            decimal optAddOn = 0m;
            var optSnap = new List<object>();
            if (line.SelectedOptionIds is { Count: > 0 })
            {
                foreach (var g in product.OptionGroups.Where(g => g.IsActive))
                {
                    var picked = g.Items.FirstOrDefault(i => i.IsActive && line.SelectedOptionIds.Contains(i.Id));
                    if (picked == null) continue;
                    optAddOn += picked.PriceAddOn;
                    optSnap.Add(new { groupName = g.Name, optionName = picked.Name, addOn = picked.PriceAddOn });
                }
            }
            var basePrice = product.SellingPrice + (mode?.Price ?? 0m) + optAddOn;

            var attrSnapshot = new List<(string GroupName, string ValueName, decimal Adjust)>();
            decimal attrAdjust = 0m;
            foreach (var avId in line.SelectedAttributeValueIds)
            {
                if (!attrValueById.TryGetValue(avId, out var av)) continue;
                if (av.ProductAttributeMapping.ProductId != product.Id) continue; // ignore tampered ids
                var amount = av.PriceAdjustmentUsePercentage
                    ? Math.Round(basePrice * av.PriceAdjustment / 100m, 2)
                    : av.PriceAdjustment;
                attrAdjust += amount;
                attrSnapshot.Add((av.ProductAttributeMapping.ProductAttribute.Name, av.Name, amount));
            }

            var qty = Math.Max(1, line.Quantity);
            var unit = Math.Max(0m, basePrice + attrAdjust);
            var discount = Math.Clamp(line.Discount, 0m, unit * qty);
            var lineTotal = (unit * qty) - discount;

            var attrJson = attrSnapshot.Count == 0 ? null
                : JsonSerializer.Serialize(attrSnapshot.Select(s => new { group = s.GroupName, value = s.ValueName, adjust = s.Adjust }));

            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductTitle = product.Title,
                ProductModeId = mode?.Id,
                ModeName = mode?.ModeName,
                Quantity = qty,
                UnitPrice = unit,
                Discount = discount,
                GstRate = product.GstRate,
                LineTotal = lineTotal,
                SelectedAttributesJson = attrJson,
                SelectedOptionIdsJson = (line.SelectedOptionIds is { Count: > 0 }) ? JsonSerializer.Serialize(line.SelectedOptionIds) : null,
                SelectedOptionsJson = optSnap.Count == 0 ? null : JsonSerializer.Serialize(optSnap),
            });
        }

        // ── Apply totals via the shared calculation engine (CGST/SGST split on the state) ──
        RecalcInPlace(order);

        var installmentMode = req.Installment is { Enabled: true };

        if (installmentMode)
        {
            // Installment order: only the DOWN PAYMENT is collected now. Validate the plan against
            // the freshly-computed total, then record just the down-payment Payment row. The order
            // stays Pending (no final invoice) until the last installment is collected.
            var planErr = await _installments.ValidatePlanAsync(order.TotalAmount, req.Installment!);
            if (planErr != null) throw new InvalidOperationException(planErr);

            order.PaymentMode = req.Installment!.DownPaymentMode;
            order.PaymentStatus = PaymentStatus.Pending;   // partial — overrides the header default
            if (req.Installment!.DownPayment > 0)
            {
                order.Payments.Add(new Payment
                {
                    Amount = req.Installment!.DownPayment,
                    PaymentMode = req.Installment!.DownPaymentMode,
                    Status = PaymentStatus.Success,
                    PaidAt = DateTime.UtcNow,
                });
            }
        }
        else
        {
            // ── Validate split payment sum matches Grand Total (within ₹1 tolerance for rounding) ──
            var paySum = req.Payments.Sum(p => p.Amount);
            if (Math.Abs(paySum - order.TotalAmount) > 1m)
                throw new InvalidOperationException($"Payment split (₹{paySum:N0}) does not match Grand Total (₹{order.TotalAmount:N0}).");

            // The Order header gets the FIRST split's mode for downstream filters; each row also lands in Payments.
            order.PaymentMode = req.Payments.First().Mode;
            foreach (var sp in req.Payments)
            {
                order.Payments.Add(new Payment
                {
                    Amount = sp.Amount,
                    PaymentMode = sp.Mode,
                    Status = req.Status == OrderStatus.Confirmed ? PaymentStatus.Success : PaymentStatus.Pending,
                    PaidAt = req.Status == OrderStatus.Confirmed ? DateTime.UtcNow : null,
                });
            }
        }

        // ── Franchise wallet debit (Wallet mode only) — credit-limit aware.
        //    The franchise ledger gets a debit row; balance is updated atomically. ──
        if (franchise != null && req.FranchiseTxnMode == FranchiseTxnMode.Wallet)
        {
            var maxSpend = franchise.WalletBalance + franchise.CreditLimit;
            if (order.TotalAmount > maxSpend)
                throw new InvalidOperationException(
                    $"Wallet balance (₹{franchise.WalletBalance:N0}) + credit limit (₹{franchise.CreditLimit:N0}) is below the order total (₹{order.TotalAmount:N0}). Top up the wallet or switch to Net Payment.");

            franchise.WalletBalance -= order.TotalAmount;
            _db.Set<FranchiseLedgerEntry>().Add(new FranchiseLedgerEntry
            {
                FranchiseId = franchise.Id,
                IsCredit = false,
                Amount = order.TotalAmount,
                BalanceAfter = franchise.WalletBalance,
                Description = $"Order {order.OrderNumber}",
                OrderId = order.Id,
            });
        }

        _db.Orders.Add(order);
        if (req.Status is OrderStatus.Confirmed or OrderStatus.Activated or OrderStatus.Delivered)
        {
            foreach (var p in products.Values) p.TotalOrders++;
        }
        await _db.SaveChangesAsync();

        // ── Installment plan ── create the schedule now that the order + down-payment exist.
        // The plan keeps PaymentStatus at Pending so the final invoice is deferred until the last
        // installment is collected (CollectAsync flips it to Success and generates the invoice).
        if (installmentMode)
        {
            var (planOk, planErr) = await _installments.CreatePlanAsync(order.Id, req.Installment!, actorId, actorName);
            if (!planOk) throw new InvalidOperationException(planErr ?? "Could not create the installment plan.");
        }

        // ── Record franchise commission entries — one per OrderItem with a matching rule. ──
        if (franchise != null && order.Status is OrderStatus.Confirmed or OrderStatus.Activated or OrderStatus.Delivered)
        {
            try { await _franchise.RecordOrderCommissionAsync(order.Id); }
            catch (Exception ex)
            {
                // Don't fail the order over a commission write — log + continue.
                await _audit.WriteAsync(new AuditEntry
                {
                    ActorUserId = actorId, ActorName = actorName ?? "system",
                    Module = "Franchise", Action = "FranchiseCommissionRecordFailed",
                    EntityType = EntityType, EntityId = order.Id.ToString(),
                    EntityName = order.OrderNumber, Status = "Warning",
                    Details = ex.Message,
                });
            }
        }

        // ── Record faculty share entries — one per (OrderItem × earning faculty). ──
        // Not gated on `franchise`: a faculty earns from the sale whoever sold it, so this runs for
        // direct/website orders too. Snapshotting here is what makes a later rule change unable to
        // restate what was already earned.
        if (order.Status is OrderStatus.Confirmed or OrderStatus.Activated or OrderStatus.Delivered)
        {
            try { await _facultySharing.RecordOrderFacultyShareAsync(order.Id); }
            catch (Exception ex)
            {
                // Same policy as the commission write above — a share-ledger failure must not fail
                // the order. The entry can be replayed, an abandoned order cannot.
                await _audit.WriteAsync(new AuditEntry
                {
                    ActorUserId = actorId, ActorName = actorName ?? "system",
                    Module = "Faculty", Action = "FacultyShareRecordFailed",
                    EntityType = EntityType, EntityId = order.Id.ToString(),
                    EntityName = order.OrderNumber, Status = "Warning",
                    Details = ex.Message,
                });
            }
        }

        await _audit.WriteAsync(new AuditEntry
        {
            ActorUserId = actorId,
            ActorName = actorName ?? "system",
            Module = franchise != null ? "Franchise" : "Orders",
            Action = franchise != null ? "FranchiseOrderCreated" : "OrderCreatedManual",
            EntityType = EntityType,
            EntityId = order.Id.ToString(),
            EntityName = order.OrderNumber,
            NewValue = JsonSerializer.Serialize(new
            {
                order.OrderNumber, order.TotalAmount,
                FranchiseId = franchise?.Id, FranchiseName = franchise?.Name,
                TxnMode = req.FranchiseTxnMode?.ToString(),
                Items = order.Items.Count,
                Payments = order.Payments.Select(p => new { p.PaymentMode, p.Amount }),
            }),
            Status = "Success",
            Details = franchise != null
                ? $"Franchise {franchise.Code} · {order.Items.Count} items · ₹{order.TotalAmount:N0} · {req.FranchiseTxnMode}"
                : $"{order.Items.Count} items · ₹{order.TotalAmount:N0} · split into {order.Payments.Count} payment(s)",
        });

        await _notify.NotifyAsync(AdminNotificationType.NewOrder, NotificationSeverity.Success,
            franchise != null ? "New franchise order" : "New manual order",
            franchise != null
                ? $"Order #{order.OrderNumber} · ₹{order.TotalAmount:N0} · {franchise.Code} · {order.StudentName}"
                : $"Order #{order.OrderNumber} · ₹{order.TotalAmount:N0} · {order.StudentName}",
            $"/admin/orders/{order.Id}", order.Id.ToString());

        // ── Serial-key generation for cash / counter / wallet orders (online orders are handled by
        // CheckoutService.CompleteOrderAsync — this covers every other path that produces a paid order).
        if (order.Status is OrderStatus.Confirmed or OrderStatus.Activated or OrderStatus.Delivered)
            await TryEnqueueSerialKeysAsync(order.Id, order.OrderNumber);

        Touch("orders", "dashboard");
        return order.Id;
    }
}

using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Franchise;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;

namespace RioCommerce.Infrastructure.Services;

public class FranchiseService : IFranchiseService
{
    private static readonly OrderStatus[] NonRevenue = { OrderStatus.Draft, OrderStatus.Cancelled, OrderStatus.Refunded };
    private readonly RioCommerceDbContext _db;
    private readonly INotificationService _notify;
    private readonly IAuditService _audit;
    // Franchise online payments (wallet recharge) run through Razorpay's popup — the same
    // gateway the franchise order flow uses. Resolved by name via the factory because a bare
    // IPaymentGateway injection would bind to the last-registered implementation (Easebuzz).
    private readonly IPaymentGatewayFactory _gateways;
    private readonly IVerificationService _verify;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _config;
    private readonly IFranchiseShareCalculator _shares;
    // Money added to a wallet is invoiced through the ordinary order→invoice pipeline.
    private readonly IInvoiceService _invoices;

    /// <summary>The archived catalogue row every wallet-credit order line points at. "OrderItems"."ProductId"
    /// is a hard FK, so the order needs something real to reference; this row exists only for that.
    /// Seeded by migration 0024 — the id is fixed and must match.</summary>
    private static readonly Guid WalletCreditProductId = Guid.Parse("9a11e700-0000-4000-a000-000000000001");

    public FranchiseService(RioCommerceDbContext db, INotificationService notify, IAuditService audit, IPaymentGatewayFactory gateways, IVerificationService verify, Microsoft.Extensions.Configuration.IConfiguration config, IFranchiseShareCalculator shares, IInvoiceService invoices)
    {
        _db = db;
        _notify = notify;
        _audit = audit;
        _gateways = gateways;
        _verify = verify;
        _config = config;
        _shares = shares;
        _invoices = invoices;
    }

    // ── Wallet credit → order → invoice ───────────────────────────────────────
    // Money added to a franchisee's wallet is recorded as a real Order tagged
    // OrderSource.WalletTopUp. That gets it an invoice from the ordinary pipeline — same number
    // series, same franchisee bill-to resolution, same PDF, listed in /admin/invoices — with no
    // separate invoice machinery. The tag is what keeps it out of every sales figure
    // (OrderQueryExtensions.ExcludeWalletTopUps); the courses the money later buys are the sale.

    /// <summary>Builds the WalletTopUp order behind a wallet credit and stages it on the context —
    /// it does NOT save. The caller commits the balance change, the order and the ledger row in a
    /// single SaveChanges so they can't come apart, then calls <see cref="InvoiceWalletCreditAsync"/>.
    /// The order's Id is assigned here so the ledger row can reference it before either is written.</summary>
    private async Task<Order> BuildWalletCreditOrderAsync(
        Franchise f, decimal amount, string description, PaymentMode mode)
    {
        var settings = await _db.FranchiseSettings.AsNoTracking().FirstOrDefaultAsync() ?? new FranchiseSettings();
        var gstRate = settings.WalletInvoiceGstRate;
        // Amounts are GST-inclusive throughout this system, so back the tax out of the credited sum:
        // the franchisee's wallet is credited with exactly what they paid.
        var gst = Math.Round(amount * gstRate / (100m + gstRate), 2);

        var legalName = !string.IsNullOrWhiteSpace(f.BusinessName) ? f.BusinessName!.Trim() : f.Name.Trim();
        var isB2B = !string.IsNullOrWhiteSpace(f.Gstin);
        var intraState = string.Equals(f.State?.Trim(), "Maharashtra", StringComparison.OrdinalIgnoreCase);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = await GenerateWalletOrderNumberAsync(),
            Source = OrderSource.WalletTopUp,
            FranchiseId = f.Id,

            // Buyer and billed party are both the franchisee — there is no student on a top-up.
            StudentName = legalName,
            StudentPhone = f.ContactPhone ?? string.Empty,
            StudentEmail = f.ContactEmail,
            StudentCity = f.City,
            CustomerType = isB2B ? CustomerType.Organization : CustomerType.Individual,
            OrgName = isB2B ? legalName : null,
            BillingName = legalName,
            BillingAddress = string.IsNullOrWhiteSpace(f.AddressLine) ? null : f.AddressLine!.Trim(),
            BillingCity = string.IsNullOrWhiteSpace(f.City) ? null : f.City.Trim(),
            BillingState = string.IsNullOrWhiteSpace(f.State) ? null : f.State!.Trim(),
            BillingPincode = string.IsNullOrWhiteSpace(f.PinCode) ? null : f.PinCode!.Trim(),
            GstNumber = isB2B ? f.Gstin!.Trim().ToUpperInvariant() : null,
            GstClassification = isB2B ? "B2B" : "B2C",

            Subtotal = amount,
            DiscountAmount = 0m,
            GstAmount = gst,
            CgstAmount = intraState ? Math.Round(gst / 2m, 2) : 0m,
            SgstAmount = intraState ? gst - Math.Round(gst / 2m, 2) : 0m,
            IgstAmount = intraState ? 0m : gst,
            TotalAmount = amount,
            // The franchisee pays the full amount — no share applies to funding their own account.
            FranchiseShareAmount = 0m,
            FranchiseNetPayable = amount,

            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Success,   // the money is already in hand
            PaymentMode = mode,
            ConfirmedAt = DateTime.UtcNow,
            CustomerNotes = description
        };
        order.Items.Add(new OrderItem
        {
            ProductId = WalletCreditProductId,
            ProductTitle = "Wallet Credit",
            Quantity = 1,
            UnitPrice = amount,
            Discount = 0m,
            GstRate = gstRate,
            GstAmount = gst,
            LineTotal = amount
        });

        _db.Orders.Add(order);
        return order;
    }

    /// <summary>Raises the invoice for a saved wallet-credit order. Non-fatal by design: the credit
    /// is already committed and must stand even if invoicing hiccups — opening the order in admin
    /// re-runs EnsureForOrderAsync and picks it up.</summary>
    private async Task InvoiceWalletCreditAsync(Order? order, Guid? actorUserId, string actorName)
    {
        if (order == null) return;
        try { await _invoices.EnsureForOrderAsync(order.Id, actorUserId, actorName); }
        catch { /* invoice can be regenerated; the credit must not be lost */ }
    }

    /// <summary>Wallet-credit orders get their own WAL- series so they're obvious in any raw data
    /// dump and can never collide with the RIO- / FRN- sales sequences.</summary>
    private async Task<string> GenerateWalletOrderNumberAsync()
    {
        var nums = await _db.Orders.IgnoreQueryFilters()
            .Where(o => o.OrderNumber.StartsWith("WAL-")).Select(o => o.OrderNumber).ToListAsync();
        var max = 1000;
        foreach (var n in nums)
            if (int.TryParse(n.Split('-').Last(), out var v) && v > max) max = v;
        return $"WAL-{max + 1}";
    }

    /// <summary>Public site base URL for links in emails. Configurable via App:BaseUrl; falls back to production.</summary>
    private string LoginUrl()
    {
        var baseUrl = _config["App:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = "https://localhost";
        return $"{baseUrl.TrimEnd('/')}/login";
    }

    private static DateTime MonthStart => new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    public async Task<FranchiseStats> StatsAsync()
    {
        var ms = MonthStart;
        // Wallet top-ups are stored as orders so they invoice normally, but they're money IN —
        // never sales. Excluded here and everywhere else orders are counted or summed.
        var franchiseOrders = _db.Orders.ExcludeWalletTopUps().Where(o => o.FranchiseId != null);
        return new FranchiseStats(
            await franchiseOrders.CountAsync(o => o.CreatedAt >= ms),
            await franchiseOrders.Where(o => o.CreatedAt >= ms && !NonRevenue.Contains(o.Status)).SumAsync(o => (decimal?)o.TotalAmount) ?? 0,
            await _db.Franchises.CountAsync(f => f.IsActive),
            await franchiseOrders.CountAsync(o => o.Status == OrderStatus.Pending));
    }

    public async Task<List<FranchiseCard>> FranchisesAsync()
    {
        var ms = MonthStart;
        var franchises = await _db.Franchises.Where(f => f.IsActive).OrderBy(f => f.Name).ToListAsync();
        var orders = await _db.Orders.ExcludeWalletTopUps().Where(o => o.FranchiseId != null && o.CreatedAt >= ms)
            .Select(o => new { o.FranchiseId, o.TotalAmount, o.Status }).ToListAsync();

        return franchises.Select(f =>
        {
            var fo = orders.Where(o => o.FranchiseId == f.Id).ToList();
            var revenue = fo.Where(o => !NonRevenue.Contains(o.Status)).Sum(o => o.TotalAmount);
            var pending = fo.Count(o => o.Status == OrderStatus.Pending);
            return new FranchiseCard(f.Id, f.Name, f.Code, f.City, f.ContactPerson, f.ContactPhone, fo.Count, revenue, pending, f.WalletBalance, f.CreditLimit);
        }).ToList();
    }

    public async Task<(bool ok, string? error)> TopUpAsync(Guid franchiseId, decimal amount, string? note)
    {
        if (amount <= 0) return (false, "Top-up amount must be greater than zero.");
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == franchiseId);
        if (f == null) return (false, "Franchise not found.");
        f.WalletBalance += amount;
        var label = string.IsNullOrWhiteSpace(note) ? "Wallet top-up" : note.Trim();
        // Money in → invoiced, same as an admin credit through the Adjust form.
        var topUpOrder = await BuildWalletCreditOrderAsync(f, amount, label, PaymentMode.BankTransfer);

        _db.FranchiseLedger.Add(new FranchiseLedgerEntry
        {
            FranchiseId = franchiseId, IsCredit = true, Amount = amount, BalanceAfter = f.WalletBalance,
            Description = label,
            OrderId = topUpOrder.Id
        });
        await _db.SaveChangesAsync();
        await InvoiceWalletCreditAsync(topUpOrder, null, "admin-topup");
        return (true, null);
    }

    public async Task<(bool ok, string? error)> SetCreditLimitAsync(Guid franchiseId, decimal limit)
    {
        if (limit < 0) return (false, "Credit limit can't be negative.");
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == franchiseId);
        if (f == null) return (false, "Franchise not found.");
        f.CreditLimit = limit;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<PagedResult<FranchiseOrderRow>> OrdersAsync(Guid? franchiseId, OrderStatus? status, int page, int pageSize)
    {
        var q = _db.Orders.ExcludeWalletTopUps().Include(o => o.Items).Include(o => o.Franchise).Where(o => o.FranchiseId != null);
        if (franchiseId.HasValue) q = q.Where(o => o.FranchiseId == franchiseId);
        if (status.HasValue) q = q.Where(o => o.Status == status);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new FranchiseOrderRow
            {
                Id = o.Id, OrderNumber = o.OrderNumber, CreatedAt = o.CreatedAt,
                FranchiseName = o.Franchise != null ? o.Franchise.Name : "—",
                StudentName = o.StudentName, StudentPhone = o.StudentPhone,
                ProductSummary = o.Items.Count == 0 ? "—" : o.Items.First().ProductTitle + (o.Items.Count > 1 ? $" +{o.Items.Count - 1}" : ""),
                TotalAmount = o.TotalAmount, Status = o.Status
            }).ToListAsync();
        return new PagedResult<FranchiseOrderRow> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    // ─────────────── Phase 1 — Onboarding & lifecycle ───────────────

    public async Task<(bool ok, string? error, Guid? id)> RegisterAsync(FranchiseRegistrationRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.FranchiseeName)) return (false, "Franchisee name is required.", null);
        if (string.IsNullOrWhiteSpace(r.ContactPhone) || r.ContactPhone.Trim().Length < 10) return (false, "A valid 10-digit mobile number is required.", null);
        if (string.IsNullOrWhiteSpace(r.ContactEmail) || !r.ContactEmail.Contains('@')) return (false, "A valid email address is required.", null);
        if (string.IsNullOrWhiteSpace(r.City) || string.IsNullOrWhiteSpace(r.State)) return (false, "City and state are required.", null);

        var email = r.ContactEmail.Trim().ToLowerInvariant();
        if (await _db.Franchises.AnyAsync(f => f.ContactEmail == email))
            return (false, "A franchise application with this email already exists.", null);
        if (await _db.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == email))
            return (false, "This email is already registered. Please use a different one.", null);

        var f = new Franchise
        {
            Name = r.FranchiseeName.Trim(),
            BusinessName = string.IsNullOrWhiteSpace(r.BusinessName) ? null : r.BusinessName.Trim(),
            ContactPerson = r.FranchiseeName.Trim(),
            ContactPhone = r.ContactPhone.Trim(),
            ContactEmail = email,
            Gstin = string.IsNullOrWhiteSpace(r.Gstin) ? null : r.Gstin.Trim().ToUpperInvariant(),
            Pan = string.IsNullOrWhiteSpace(r.Pan) ? null : r.Pan.Trim().ToUpperInvariant(),
            AddressLine = string.IsNullOrWhiteSpace(r.AddressLine) ? null : r.AddressLine.Trim(),
            State = r.State.Trim(),
            City = r.City.Trim(),
            PinCode = string.IsNullOrWhiteSpace(r.PinCode) ? null : r.PinCode.Trim(),
            DocumentUrls = string.IsNullOrWhiteSpace(r.DocumentUrls) ? null : r.DocumentUrls.Trim(),
            Code = "",                                  // assigned by admin on approval
            Status = FranchiseStatus.Unverified,        // held out of the admin queue until OTP verified
            IsActive = false,
            WalletBalance = 0,
            CreditLimit = 0,
            RegisteredAt = DateTime.UtcNow
        };
        _db.Franchises.Add(f);
        await _db.SaveChangesAsync();

        // Send ONE verification code to both the email and phone on the application.
        await _verify.SendBothAsync(VerificationPurpose.FranchiseeApplication, f.ContactEmail, f.ContactPhone, f.Id, f.Name);

        // NOTE: admin is NOT notified here — that happens in VerifyApplicationAsync once the
        // contact is verified, so the admin queue only ever contains verified applications.
        return (true, null, f.Id);
    }

    public async Task<(bool ok, string? error)> ResendApplicationCodeAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return (false, "Email is required.");
        var key = email.Trim().ToLowerInvariant();
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.ContactEmail == key);
        if (f == null) return (true, null);                          // don't reveal non-existence
        if (f.Status != FranchiseStatus.Unverified) return (true, null);  // already verified — no-op

        await _verify.SendBothAsync(VerificationPurpose.FranchiseeApplication, f.ContactEmail, f.ContactPhone, f.Id, f.Name);
        return (true, null);
    }

    public async Task<(bool ok, string? error)> VerifyApplicationAsync(string target, string code)
    {
        var check = await _verify.VerifyAsync(VerificationPurpose.FranchiseeApplication, target, code);
        if (!check.Success)
            return (false, check.ErrorMessage ?? "Verification failed.");

        var f = check.SubjectId is { } sid
            ? await _db.Franchises.FirstOrDefaultAsync(x => x.Id == sid)
            : null;
        if (f == null) return (false, "Application not found for this verification.");
        if (f.Status != FranchiseStatus.Unverified)
            return (true, null); // already verified/processed — idempotent success.

        f.Status = FranchiseStatus.Pending;   // now visible to admins for review.
        await _db.SaveChangesAsync();

        // Now notify admin/applicant that a verified application is awaiting review.
        await _notify.SendAsync("franchisee_registered",
            new NotificationRecipient(f.ContactEmail, f.ContactPhone),
            new Dictionary<string, string>
            {
                ["name"] = f.Name,
                ["business"] = f.BusinessName ?? f.Name,
                ["city"] = f.City
            });

        return (true, null);
    }

    public async Task<(bool ok, string? error)> AdminVerifyApplicationAsync(Guid franchiseId, Guid actorUserId)
    {
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == franchiseId);
        if (f == null) return (false, "Application not found.");
        if (f.Status != FranchiseStatus.Unverified)
            return (true, null); // already verified/processed — idempotent success.

        // Admin override — no OTP required. Move Unverified → Pending so it enters the normal
        // approve/reject review flow (ApproveAsync/RejectAsync both require Pending).
        f.Status = FranchiseStatus.Pending;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<PagedResult<FranchiseApplicationRow>> ListApplicationsAsync(FranchiseStatus? status, string? search, int page, int pageSize)
    {
        var q = _db.Franchises.AsQueryable();
        if (status.HasValue) q = q.Where(f => f.Status == status);
        else q = q.Where(f => f.Status != FranchiseStatus.Unverified); // hide un-verified drafts from the admin queue
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(f =>
                EF.Functions.ILike(f.Name, $"%{s}%") ||
                (f.BusinessName != null && EF.Functions.ILike(f.BusinessName, $"%{s}%")) ||
                (f.ContactPhone != null && EF.Functions.ILike(f.ContactPhone, $"%{s}%")) ||
                (f.ContactEmail != null && EF.Functions.ILike(f.ContactEmail, $"%{s}%")) ||
                EF.Functions.ILike(f.City, $"%{s}%"));
        }
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(f => f.RegisteredAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(f => new FranchiseApplicationRow
            {
                Id = f.Id, Name = f.Name, BusinessName = f.BusinessName, Code = f.Code, City = f.City,
                ContactPhone = f.ContactPhone, ContactEmail = f.ContactEmail,
                Status = f.Status, IsActive = f.IsActive, RegisteredAt = f.RegisteredAt, WalletBalance = f.WalletBalance
            }).ToListAsync();
        return new PagedResult<FranchiseApplicationRow> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    public async Task<FranchiseAdminDetail?> GetDetailAsync(Guid id)
    {
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == id);
        if (f == null) return null;
        return new FranchiseAdminDetail
        {
            Id = f.Id, Name = f.Name, BusinessName = f.BusinessName, Code = f.Code, City = f.City, State = f.State,
            PinCode = f.PinCode, AddressLine = f.AddressLine, ContactPerson = f.ContactPerson, ContactPhone = f.ContactPhone,
            ContactEmail = f.ContactEmail, Gstin = f.Gstin, Pan = f.Pan, DocumentUrls = f.DocumentUrls,
            Status = f.Status, IsActive = f.IsActive, RegisteredAt = f.RegisteredAt, ApprovedAt = f.ApprovedAt,
            ApprovalRemarks = f.ApprovalRemarks, RejectionRemarks = f.RejectionRemarks,
            WalletBalance = f.WalletBalance, CreditLimit = f.CreditLimit, AdminUserId = f.AdminUserId
        };
    }

    /// <summary>
    /// Read-only pre-flight for approval: does anything already own this franchise's contact details?
    ///
    /// <para>Approval provisions a login, and users are unique on Email and — separately — on Phone.
    /// Franchisees very often already bought something as a student, so a clash is normal rather than
    /// exceptional. This looks, reports, and writes nothing; the admin decides what happens next.</para>
    /// </summary>
    public async Task<FranchiseApprovalConflict> CheckApprovalConflictAsync(Guid franchiseId, CancellationToken ct = default)
    {
        var f = await _db.Franchises.AsNoTracking().FirstOrDefaultAsync(x => x.Id == franchiseId, ct);
        if (f == null) return new FranchiseApprovalConflict { Message = "Franchise not found." };

        var result = new FranchiseApprovalConflict
        {
            FranchiseName = f.Name,
            FranchiseEmail = f.ContactEmail,
            FranchisePhone = f.ContactPhone,
        };

        // A second franchise on the same contact details means approving here would duplicate it.
        // Checked first: it makes the account question moot.
        var dupFranchise = await _db.Franchises.AsNoTracking()
            .Where(x => x.Id != f.Id
                     && ((f.ContactEmail != null && x.ContactEmail == f.ContactEmail)
                      || (f.ContactPhone != null && x.ContactPhone == f.ContactPhone)))
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (dupFranchise != null)
        {
            result.Kind = FranchiseApprovalConflictKind.DuplicateFranchise;
            result.DuplicateFranchise = new ExistingFranchiseInfo
            {
                FranchiseId = dupFranchise.Id,
                Name = dupFranchise.Name,
                Code = dupFranchise.Code,
                ContactEmail = dupFranchise.ContactEmail,
                ContactPhone = dupFranchise.ContactPhone,
                Status = dupFranchise.Status.ToString(),
                IsPending = dupFranchise.Status == FranchiseStatus.Pending,
            };
            result.Message = "Another franchise already uses this phone or email.";
            return result;
        }

        var emailOwner = string.IsNullOrWhiteSpace(f.ContactEmail) ? null
            : await LoadAccountAsync(u => u.Email == f.ContactEmail, ct);
        var phoneOwner = string.IsNullOrWhiteSpace(f.ContactPhone) ? null
            : await LoadAccountAsync(u => u.Phone == f.ContactPhone, ct);

        if (emailOwner != null) emailOwner.MatchedOnEmail = true;
        if (phoneOwner != null) phoneOwner.MatchedOnPhone = true;

        // One user holding both is the easy case — collapse to a single object so the modal shows one card.
        if (emailOwner != null && phoneOwner != null && emailOwner.UserId == phoneOwner.UserId)
        {
            emailOwner.MatchedOnPhone = true;
            phoneOwner = emailOwner;
        }

        result.EmailOwner = emailOwner;
        result.PhoneOwner = phoneOwner;

        if (emailOwner == null && phoneOwner == null) return result;   // Kind stays None

        // Two DIFFERENT people hold the two identifiers. Picking either would silently decide which
        // human this franchise belongs to, so nothing is offered — the data has to change first.
        if (emailOwner != null && phoneOwner != null && emailOwner.UserId != phoneOwner.UserId)
        {
            result.Kind = FranchiseApprovalConflictKind.MultipleAccounts;
            result.Message = "The phone number and email address belong to different existing accounts. "
                           + "Automatic approval is blocked — choose the correct account or change the "
                           + "franchise contact details.";
            return result;
        }

        result.Kind = FranchiseApprovalConflictKind.ExistingAccount;
        result.Message = "This phone number / email is already registered in the system.";
        return result;
    }

    /// <summary>Loads one account plus its active roles for display in the conflict modal.</summary>
    private async Task<ExistingAccountInfo?> LoadAccountAsync(
        System.Linq.Expressions.Expression<Func<User, bool>> predicate, CancellationToken ct)
    {
        var u = await _db.Users.AsNoTracking().Where(predicate).FirstOrDefaultAsync(ct);
        if (u == null) return null;

        var roles = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == u.Id && ur.IsActive)
            .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
            .ToListAsync(ct);

        return new ExistingAccountInfo
        {
            UserId = u.Id,
            FullName = u.FullName,
            Email = u.Email,
            Phone = u.Phone,
            IsActive = u.IsActive,
            CreatedAt = u.CreatedAt,
            Roles = roles,
            AccountType = DescribeAccountType(roles),
        };
    }

    /// <summary>Turns a role list into the one word the admin needs to read before reusing an account.</summary>
    private static string DescribeAccountType(IReadOnlyCollection<string> roles)
    {
        if (roles.Any(r => string.Equals(r, "franchise_admin", StringComparison.OrdinalIgnoreCase))) return "Franchise Admin";
        if (roles.Any(r => r.Contains("admin", StringComparison.OrdinalIgnoreCase))) return "Admin";
        if (roles.Any(r => string.Equals(r, "student", StringComparison.OrdinalIgnoreCase))) return "Student";
        if (roles.Any(r => string.Equals(r, "customer", StringComparison.OrdinalIgnoreCase))) return "Customer";
        return roles.Count == 0 ? "Other" : string.Join(", ", roles);
    }

    public async Task<(bool ok, string? error)> ApproveAsync(FranchiseApprovalRequest req, Guid actorUserId)
    {
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == req.FranchiseId);
        if (f == null) return (false, "Franchise not found.");
        if (f.Status != FranchiseStatus.Pending) return (false, "Only pending applications can be approved.");
        if (string.IsNullOrWhiteSpace(f.ContactEmail)) return (false, "Franchise has no contact email — cannot provision login.");

        // ── Nothing is staged until the conflict picture is known ───────────────────────────────
        // A clash used to surface only as a unique-index violation out of SaveChangesAsync, thrown
        // straight past this method's (bool, string) contract — so the approval screen never got an
        // answer, sat on "Approving…" for ever, and nothing was written. It is resolved up front now,
        // and only ever by an explicit admin decision carried on the request.
        var conflict = await CheckApprovalConflictAsync(f.Id);

        if (conflict.Kind == FranchiseApprovalConflictKind.DuplicateFranchise)
            return (false, "Another franchise already uses this phone/email: "
                         + $"{conflict.DuplicateFranchise!.Name} ({conflict.DuplicateFranchise.Code ?? "no code"}, "
                         + $"{conflict.DuplicateFranchise.Status}). Open that franchise instead of creating a duplicate.");

        if (conflict.Kind == FranchiseApprovalConflictKind.MultipleAccounts)
            return (false, conflict.Message);

        User? existingUser = null;
        if (conflict.Kind == FranchiseApprovalConflictKind.ExistingAccount)
        {
            // The admin must have SEEN this and chosen. Silence is not consent.
            if (req.AccountResolution == FranchiseAccountResolution.None)
                return (false, "An existing account already uses this phone/email. Review the conflict and "
                             + "choose how to proceed before approving.");

            var chosen = conflict.EmailOwner ?? conflict.PhoneOwner!;
            // Guard against a stale screen: the user the admin looked at must be the user we found now.
            if (req.ExistingUserId is not { } chosenId || chosenId != chosen.UserId)
                return (false, "The existing account changed since the conflict was shown. Reopen the "
                             + "approval so the current account details can be reviewed.");

            existingUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == chosenId);
            if (existingUser == null) return (false, "The selected existing account no longer exists.");

            if (req.AccountResolution == FranchiseAccountResolution.ReplaceExistingContact)
            {
                if (!req.ConfirmContactReplacement)
                    return (false, "Replacing the existing account's contact details needs explicit confirmation.");
                if (!req.ReplaceEmail && !req.ReplacePhone)
                    return (false, "Select which contact detail to replace, or choose Use Existing Account instead.");
            }
        }
        else if (req.AccountResolution != FranchiseAccountResolution.None)
        {
            // The screen thought there was a clash and there is not (someone else fixed it meanwhile).
            // Fall through to a clean approval rather than acting on a decision about nothing.
            req.AccountResolution = FranchiseAccountResolution.None;
        }

        // Assign an admin-overridable short code (auto-derive from City if blank), guaranteeing uniqueness.
        var desired = (string.IsNullOrWhiteSpace(req.Code) ? f.City : req.Code).Trim().ToUpperInvariant();
        var baseCode = new string((desired + "XXX").Where(char.IsLetterOrDigit).ToArray()).PadRight(3, 'X')[..3];
        var code = baseCode; var suffix = 1;
        while (await _db.Franchises.AnyAsync(x => x.Code == code && x.Id != f.Id)) { code = baseCode + (++suffix); }

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "franchise_admin");
        if (role == null) return (false, "franchise_admin role is missing.");

        User user;
        string? plainPassword = null;         // null = the account keeps the password it already had
        var replacedFields = new List<string>();

        if (existingUser != null)
        {
            // ── Reuse ────────────────────────────────────────────────────────────────────────────
            // The person already has an identity here, with orders, wallet, course access and serial
            // keys hanging off it. Reuse touches NONE of that: no password reset, no email/phone
            // rewrite (unless separately confirmed), no second account for the same human.
            user = existingUser;

            if (req.AccountResolution == FranchiseAccountResolution.ReplaceExistingContact)
            {
                // Only the fields explicitly ticked, one at a time, and only when the new value is
                // actually free — otherwise the same unique index we are avoiding would throw again.
                if (req.ReplaceEmail && !string.Equals(user.Email, f.ContactEmail, StringComparison.OrdinalIgnoreCase))
                {
                    if (await _db.Users.AnyAsync(u => u.Id != user.Id && u.Email == f.ContactEmail))
                        return (false, $"Cannot replace the email — {f.ContactEmail} is already used by another account.");
                    replacedFields.Add($"Email: {user.Email ?? "(none)"} -> {f.ContactEmail}");
                    user.Email = f.ContactEmail;
                }
                if (req.ReplacePhone && !string.Equals(user.Phone, f.ContactPhone, StringComparison.Ordinal))
                {
                    if (!string.IsNullOrWhiteSpace(f.ContactPhone)
                        && await _db.Users.AnyAsync(u => u.Id != user.Id && u.Phone == f.ContactPhone))
                        return (false, $"Cannot replace the phone — {f.ContactPhone} is already used by another account.");
                    replacedFields.Add($"Phone: {user.Phone ?? "(none)"} -> {f.ContactPhone}");
                    user.Phone = f.ContactPhone;
                }
            }

            // Grant franchise_admin without disturbing the roles they already hold.
            var link = await _db.UserRoles.FirstOrDefaultAsync(ur => ur.UserId == user.Id && ur.RoleId == role.Id);
            if (link == null) _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, IsActive = true });
            else link.IsActive = true;

            if (!user.IsActive) user.IsActive = true;
        }
        else
        {
            // ── Fresh login ──────────────────────────────────────────────────────────────────────
            plainPassword = GeneratePassword();
            user = new User
            {
                Id = Guid.NewGuid(),
                FullName = f.Name + (string.IsNullOrWhiteSpace(f.BusinessName) ? "" : $" ({f.BusinessName})"),
                Email = f.ContactEmail,
                Phone = f.ContactPhone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(plainPassword),
                City = f.City,
                State = f.State,
                IsActive = true,
                IsVerified = true
            };
            _db.Users.Add(user);
            _db.UserRoles.Add(new UserRole { User = user, RoleId = role.Id, IsActive = true });
        }

        f.Code = code;
        f.Status = FranchiseStatus.Approved;
        f.IsActive = true;
        f.ApprovedAt = DateTime.UtcNow;
        f.ApprovedById = actorUserId;
        f.ApprovalRemarks = string.IsNullOrWhiteSpace(req.Remarks) ? null : req.Remarks.Trim();
        f.CreditLimit = req.CreditLimit < 0 ? 0 : req.CreditLimit;
        f.AdminUserId = user.Id;

        if (req.OpeningBalance is decimal opening && opening > 0)
        {
            // Deliberately NOT invoiced. An opening balance is a starting figure set at approval —
            // there's no payment context here to say whether money actually changed hands. If the
            // franchisee did pay a joining deposit, record it through Wallet → Adjust with
            // "Money received" ticked, which raises the invoice properly.
            //
            // This is the FRANCHISE wallet (Franchises.WalletBalance + franchise_ledger). Reusing a
            // student's login does not touch their customer-side records in any way.
            f.WalletBalance = opening;
            _db.FranchiseLedger.Add(new FranchiseLedgerEntry
            {
                FranchiseId = f.Id, IsCredit = true, Amount = opening, BalanceAfter = opening,
                Description = "Opening balance (on approval)"
            });
        }

        // One SaveChanges = one transaction: franchise, user/role link and the opening-balance ledger
        // either all land or none do. Nothing below this line can leave a half-approved franchise.
        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorUserId, "Admin", "FranchiseApproved", "Franchise", f.Id.ToString(),
            $"Code={f.Code}; CreditLimit={f.CreditLimit}; Opening={req.OpeningBalance ?? 0}");

        // Reusing or rewriting someone's identity is a decision worth being able to answer for later,
        // so it is logged separately from the approval itself. Passwords are never recorded.
        if (existingUser != null)
        {
            if (replacedFields.Count > 0)
                await _audit.LogAsync(actorUserId, "Admin", "ExistingContactReplaced", "User", user.Id.ToString(),
                    $"FranchiseId={f.Id}; " + string.Join("; ", replacedFields));
            else
                await _audit.LogAsync(actorUserId, "Admin", "ExistingAccountReused", "User", user.Id.ToString(),
                    $"FranchiseId={f.Id}; RoleGranted=franchise_admin; PasswordUnchanged=true");
        }

        // Sent only after the commit above succeeded. A reused account keeps its own password, so the
        // credentials line must say so rather than quote a password that was never set.
        await _notify.SendAsync("franchisee_approved",
            new NotificationRecipient(f.ContactEmail, f.ContactPhone),
            new Dictionary<string, string>
            {
                ["name"] = f.Name,
                ["business"] = f.BusinessName ?? f.Name,
                ["code"] = f.Code,
                ["email"] = user.Email ?? f.ContactEmail,
                ["password"] = plainPassword ?? "(unchanged — sign in with your existing password)",
                ["login_url"] = LoginUrl()
            });

        // Auto-assign active products' default shares to the new franchise (honours global settings).
        try { await AutoAssignProductsToFranchiseAsync(f.Id); } catch { /* non-fatal — admin can bulk-assign later */ }

        return (true, null);
    }


    /// <summary>
    /// Updates a franchise's own particulars — GSTIN, PAN, address, contact person.
    ///
    /// <para>Everything financial and every lifecycle field is deliberately out of reach: wallet
    /// balance, credit limit, commissions, Status, IsActive, the short Code and the login email are
    /// never read from the request, so this cannot change what a franchisee owes, earns or signs in
    /// with. Until now the only way to set a GSTIN after approval was to re-run the Excel bulk
    /// import, which touches far more fields than one.</para>
    /// </summary>
    public async Task<(bool ok, string? error)> UpdateProfileAsync(FranchiseProfileEdit req, Guid actorUserId)
    {
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == req.FranchiseId);
        if (f == null) return (false, "Franchise not found.");

        if (string.IsNullOrWhiteSpace(req.Name)) return (false, "Franchisee name is required.");
        if (string.IsNullOrWhiteSpace(req.City)) return (false, "City is required.");

        // Normalise to the same shape the bulk import stores, so a value typed here and a value
        // imported from a spreadsheet are never subtly different.
        var gstin = string.IsNullOrWhiteSpace(req.Gstin) ? null : req.Gstin.Trim().ToUpperInvariant();
        var pan = string.IsNullOrWhiteSpace(req.Pan) ? null : req.Pan.Trim().ToUpperInvariant();
        var pin = string.IsNullOrWhiteSpace(req.PinCode) ? null : req.PinCode.Trim();
        var phone = string.IsNullOrWhiteSpace(req.ContactPhone) ? null : req.ContactPhone.Trim();

        // A GSTIN decides whether the franchisee's commission carries GST and whether the institute
        // can claim input credit, so a malformed one is worse than none at all.
        if (gstin != null && !GstinPattern.IsMatch(gstin))
            return (false, "GSTIN must be 15 characters in the standard format (e.g. 08ABCDE1234F1Z5).");
        if (pan != null && !PanPattern.IsMatch(pan))
            return (false, "PAN must be 10 characters in the standard format (e.g. ABCDE1234F).");
        if (pin != null && !PinPattern.IsMatch(pin))
            return (false, "PIN code must be 6 digits.");
        if (phone != null && !PhonePattern.IsMatch(phone))
            return (false, "Contact phone must be a 10-digit mobile number.");

        // GSTIN carries the state code in its first two digits; a mismatch with the franchise's own
        // state would silently produce wrong CGST/SGST vs IGST on every commission invoice.
        if (gstin != null)
        {
            var stateName = string.IsNullOrWhiteSpace(req.State) ? f.State : req.State;
            var expected = GstStateCode(stateName);
            if (expected != null && !gstin.StartsWith(expected, StringComparison.Ordinal))
                return (false, $"This GSTIN starts with {gstin[..2]}, which is not the GST state code for {stateName} ({expected}). "
                             + "Check the GSTIN, or correct the state.");
        }

        // What actually changed, for the audit trail — "profile updated" tells nobody anything.
        var changes = new List<string>();
        void Track(string field, string? before, string? after)
        {
            if (!string.Equals(before, after, StringComparison.Ordinal))
                changes.Add($"{field}: {(string.IsNullOrWhiteSpace(before) ? "(none)" : before)} -> {(string.IsNullOrWhiteSpace(after) ? "(none)" : after)}");
        }

        Track("Name", f.Name, req.Name.Trim());
        Track("BusinessName", f.BusinessName, string.IsNullOrWhiteSpace(req.BusinessName) ? null : req.BusinessName.Trim());
        Track("ContactPerson", f.ContactPerson, string.IsNullOrWhiteSpace(req.ContactPerson) ? null : req.ContactPerson.Trim());
        Track("ContactPhone", f.ContactPhone, phone);
        Track("AddressLine", f.AddressLine, string.IsNullOrWhiteSpace(req.AddressLine) ? null : req.AddressLine.Trim());
        Track("City", f.City, req.City.Trim());
        Track("State", f.State, string.IsNullOrWhiteSpace(req.State) ? null : req.State.Trim());
        Track("PinCode", f.PinCode, pin);
        Track("Gstin", f.Gstin, gstin);
        Track("Pan", f.Pan, pan);

        if (changes.Count == 0) return (true, null);   // nothing to write, nothing to log

        f.Name = req.Name.Trim();
        f.BusinessName = string.IsNullOrWhiteSpace(req.BusinessName) ? null : req.BusinessName.Trim();
        f.ContactPerson = string.IsNullOrWhiteSpace(req.ContactPerson) ? null : req.ContactPerson.Trim();
        f.ContactPhone = phone;
        f.AddressLine = string.IsNullOrWhiteSpace(req.AddressLine) ? null : req.AddressLine.Trim();
        f.City = req.City.Trim();
        f.State = string.IsNullOrWhiteSpace(req.State) ? null : req.State.Trim();
        f.PinCode = pin;
        f.Gstin = gstin;
        f.Pan = pan;

        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorUserId, "Admin", "FranchiseProfileUpdated", "Franchise", f.Id.ToString(),
            string.Join("; ", changes));

        return (true, null);
    }

    // Standard formats — 2-digit state code, 10-char PAN, entity digit, 'Z', checksum.
    private static readonly System.Text.RegularExpressions.Regex GstinPattern =
        new(@"^[0-3][0-9][A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex PanPattern =
        new(@"^[A-Z]{5}[0-9]{4}[A-Z]$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex PinPattern =
        new(@"^[1-9][0-9]{5}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex PhonePattern =
        new(@"^[6-9][0-9]{9}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>GST state code for a state name, or null when the name is not one we recognise —
    /// an unknown name skips the check rather than blocking a legitimate GSTIN.</summary>
    private static string? GstStateCode(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;
        var key = state.Trim().ToUpperInvariant();
        return key switch
        {
            "JAMMU AND KASHMIR" or "JAMMU & KASHMIR" => "01",
            "HIMACHAL PRADESH" => "02",
            "PUNJAB" => "03",
            "CHANDIGARH" => "04",
            "UTTARAKHAND" or "UTTARAKHAND STATE" => "05",
            "HARYANA" => "06",
            "DELHI" or "NEW DELHI" => "07",
            "RAJASTHAN" => "08",
            "UTTAR PRADESH" => "09",
            "BIHAR" => "10",
            "SIKKIM" => "11",
            "ARUNACHAL PRADESH" => "12",
            "NAGALAND" => "13",
            "MANIPUR" => "14",
            "MIZORAM" => "15",
            "TRIPURA" => "16",
            "MEGHALAYA" => "17",
            "ASSAM" => "18",
            "WEST BENGAL" => "19",
            "JHARKHAND" => "20",
            "ODISHA" or "ORISSA" => "21",
            "CHHATTISGARH" => "22",
            "MADHYA PRADESH" => "23",
            "GUJARAT" => "24",
            "MAHARASHTRA" => "27",
            "KARNATAKA" => "29",
            "GOA" => "30",
            "LAKSHADWEEP" => "31",
            "KERALA" => "32",
            "TAMIL NADU" => "33",
            "PUDUCHERRY" or "PONDICHERRY" => "34",
            "ANDAMAN AND NICOBAR ISLANDS" or "ANDAMAN & NICOBAR ISLANDS" => "35",
            "TELANGANA" => "36",
            "ANDHRA PRADESH" => "37",
            "LADAKH" => "38",
            _ => null,
        };
    }

    public Task<List<FranchiseImportRow>> ParseFranchiseExcelAsync(Stream xlsx, CancellationToken ct = default)
    {
        var rows = new List<FranchiseImportRow>();
        using var wb = new XLWorkbook(xlsx);
        var ws = wb.Worksheets.First();

        // Map header name (lower-cased, trimmed) → column number.
        var used = ws.RangeUsed();
        if (used == null) return Task.FromResult(rows);
        var firstRow = used.FirstRow();
        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in firstRow.Cells())
        {
            var key = (cell.GetString() ?? "").Trim().ToLowerInvariant();
            if (key.Length > 0 && !col.ContainsKey(key)) col[key] = cell.Address.ColumnNumber;
        }

        string? Get(IXLRangeRow r, params string[] names)
        {
            foreach (var n in names)
                if (col.TryGetValue(n, out var c))
                {
                    var v = r.Cell(c).GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            return null;
        }

        foreach (var r in used.RowsUsed().Skip(1)) // skip header
        {
            ct.ThrowIfCancellationRequested();
            var email = Get(r, "email", "username", "alternate email");
            var name = Get(r, "name");
            if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(name)) continue; // blank row

            rows.Add(new FranchiseImportRow
            {
                Name = name ?? "",
                BusinessName = Get(r, "description"),          // holds the brand/person label in the ERP export
                ContactPerson = Get(r, "description"),
                Email = email ?? "",
                Phone = Get(r, "mobile", "alternate mobile"),
                City = Get(r, "city"),
                State = Get(r, "state name", "state"),
                AddressLine = Get(r, "address"),
                PinCode = Get(r, "pincode"),
                Gstin = Get(r, "gstin"),
                Pan = Get(r, "pan"),
            });
        }

        return Task.FromResult(rows);
    }

    public async Task<FranchiseImportResult> BulkImportAsync(FranchiseImportRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var result = new FranchiseImportResult { DryRun = request.DryRun };

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "franchise_admin", ct);
        if (role == null) { result.Ok = false; result.Error = "franchise_admin role is missing."; return result; }

        // De-dupe input rows by email (keep first), so one email can't insert twice in a single run.
        var rows = request.Rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Email))
            .GroupBy(r => r.Email.Trim().ToLowerInvariant())
            .Select(g => g.First())
            .ToList();
        result.Read = rows.Count;

        // Track codes assigned during THIS run so uniqueness holds even before SaveChanges.
        var usedCodes = new HashSet<string>(
            await _db.Franchises.Select(f => f.Code).Where(c => c != null && c != "").ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);

        // Track phones assigned to users during THIS batch, so two Excel rows with the same phone
        // don't collide on the users.Phone unique index within a single run.
        var usedPhones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var newFranchiseIds = new List<Guid>();

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var item = new FranchiseImportItemResult { Name = row.Name, Email = row.Email };

            // ── Validate the essentials (mirrors RegisterAsync's guards) ──
            if (string.IsNullOrWhiteSpace(row.Name))
            { item.Outcome = "error"; item.Message = "Name is required."; result.Errors++; result.Items.Add(item); continue; }
            var email = row.Email.Trim().ToLowerInvariant();
            if (!email.Contains('@'))
            { item.Outcome = "error"; item.Message = "Valid email is required."; result.Errors++; result.Items.Add(item); continue; }
            if (string.IsNullOrWhiteSpace(row.City) || string.IsNullOrWhiteSpace(row.State))
            { item.Outcome = "error"; item.Message = "City and State are required."; result.Errors++; result.Items.Add(item); continue; }

            var phone = NormalizePhone(row.Phone);

            // ── Idempotency: existing franchise by email? update it (never duplicate). ──
            // Case-insensitive match — franchises imported earlier (or created via admin approve)
            // may have stored ContactEmail in mixed case, so compare lower-to-lower.
            var existing = await _db.Franchises.FirstOrDefaultAsync(f => f.ContactEmail != null && f.ContactEmail.ToLower() == email, ct);
            if (existing != null)
            {
                item.Code = existing.Code;
                if (!request.DryRun)
                {
                    existing.Name = row.Name.Trim();
                    existing.BusinessName = string.IsNullOrWhiteSpace(row.BusinessName) ? existing.BusinessName : row.BusinessName!.Trim();
                    existing.ContactPerson = string.IsNullOrWhiteSpace(row.ContactPerson) ? row.Name.Trim() : row.ContactPerson!.Trim();
                    existing.ContactPhone = phone ?? existing.ContactPhone;
                    existing.City = row.City!.Trim();
                    existing.State = row.State!.Trim();
                    existing.AddressLine = string.IsNullOrWhiteSpace(row.AddressLine) ? existing.AddressLine : row.AddressLine!.Trim();
                    existing.PinCode = string.IsNullOrWhiteSpace(row.PinCode) ? existing.PinCode : row.PinCode!.Trim();
                    existing.Gstin = string.IsNullOrWhiteSpace(row.Gstin) ? existing.Gstin : row.Gstin!.Trim().ToUpperInvariant();
                    existing.Pan = string.IsNullOrWhiteSpace(row.Pan) ? existing.Pan : row.Pan!.Trim().ToUpperInvariant();
                    // Wallet / credit / commissions left untouched.
                }
                item.Outcome = "updated"; result.Updated++; result.Items.Add(item); continue;
            }

            // Guard: if a user with this email already exists (e.g. a customer), skip to avoid a
            // conflicting login. Admin can link manually.
            if (await _db.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == email, ct))
            { item.Outcome = "skipped"; item.Message = "A user with this email already exists."; result.Skipped++; result.Items.Add(item); continue; }

            // ── Assign a unique Code from City (same scheme as ApproveAsync). ──
            var desired = row.City!.Trim().ToUpperInvariant();
            var baseCode = new string((desired + "XXX").Where(char.IsLetterOrDigit).ToArray()).PadRight(3, 'X')[..3];
            var code = baseCode; var suffix = 1;
            while (usedCodes.Contains(code)) code = baseCode + (++suffix);
            usedCodes.Add(code);
            item.Code = code;

            if (!request.DryRun)
            {
                // The users.Phone unique index is filtered (unique only when NOT NULL). If this phone
                // is already taken by another user — or by an earlier row in this same batch — leave
                // the login user's phone NULL to avoid the 23505 collision. The phone is still kept on
                // the Franchise.ContactPhone (no uniqueness there), so no contact info is lost.
                string? userPhone = phone;
                if (!string.IsNullOrWhiteSpace(userPhone))
                {
                    var phoneTaken = usedPhones.Contains(userPhone)
                        || await _db.Users.AnyAsync(u => u.Phone == userPhone, ct);
                    if (phoneTaken) userPhone = null;
                    else usedPhones.Add(userPhone);
                }

                var user = new User
                {
                    Id = Guid.NewGuid(),
                    FullName = row.Name.Trim() + (string.IsNullOrWhiteSpace(row.BusinessName) ? "" : $" ({row.BusinessName!.Trim()})"),
                    Email = email,
                    Phone = userPhone,
                    PasswordHash = null,        // no password — set via forgot-password, no email sent
                    City = row.City!.Trim(),
                    State = row.State!.Trim(),
                    IsActive = true,
                    IsVerified = true,
                };
                _db.Users.Add(user);
                _db.UserRoles.Add(new UserRole { User = user, RoleId = role.Id, IsActive = true });

                var f = new Franchise
                {
                    Id = Guid.NewGuid(),
                    Name = row.Name.Trim(),
                    BusinessName = string.IsNullOrWhiteSpace(row.BusinessName) ? null : row.BusinessName!.Trim(),
                    ContactPerson = string.IsNullOrWhiteSpace(row.ContactPerson) ? row.Name.Trim() : row.ContactPerson!.Trim(),
                    ContactPhone = phone,
                    ContactEmail = email,
                    Gstin = string.IsNullOrWhiteSpace(row.Gstin) ? null : row.Gstin!.Trim().ToUpperInvariant(),
                    Pan = string.IsNullOrWhiteSpace(row.Pan) ? null : row.Pan!.Trim().ToUpperInvariant(),
                    AddressLine = string.IsNullOrWhiteSpace(row.AddressLine) ? null : row.AddressLine!.Trim(),
                    State = row.State!.Trim(),
                    City = row.City!.Trim(),
                    PinCode = string.IsNullOrWhiteSpace(row.PinCode) ? null : row.PinCode!.Trim(),
                    Code = code,
                    Status = FranchiseStatus.Approved,
                    IsActive = true,
                    WalletBalance = 0,
                    CreditLimit = 0,
                    RegisteredAt = DateTime.UtcNow,
                    ApprovedAt = DateTime.UtcNow,
                    ApprovedById = actorUserId,
                    ApprovalRemarks = "Imported from legacy ERP",
                    AdminUserId = user.Id,
                };
                _db.Franchises.Add(f);
                newFranchiseIds.Add(f.Id);
            }

            item.Outcome = "created"; result.Created++; result.Items.Add(item);
        }

        if (!request.DryRun)
        {
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // Surface the REAL database error (e.g. Npgsql not-null / unique / FK violation)
                // into the result instead of letting it bubble up and terminate the Blazor circuit.
                var deepest = ex;
                while (deepest.InnerException != null) deepest = deepest.InnerException;
                result.Ok = false;
                result.Error = $"Database save failed: {deepest.GetType().Name}: {deepest.Message}";
                return result;
            }

            // Auto-assign default product shares to each newly created franchise (non-fatal per franchise).
            if (request.AutoAssignProductShares)
            {
                foreach (var fid in newFranchiseIds)
                {
                    try { await AutoAssignProductsToFranchiseAsync(fid); }
                    catch { /* non-fatal — admin can bulk-assign later */ }
                }
            }

            await _audit.LogAsync(actorUserId, "Admin", "FranchiseBulkImport", "Franchise", "",
                $"Created={result.Created}; Updated={result.Updated}; Skipped={result.Skipped}; Errors={result.Errors}");
        }

        result.Ok = true;
        return result;
    }

    private static string? NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length > 10 && digits.StartsWith("91")) digits = digits[^10..];
        return digits.Length == 0 ? null : digits;
    }

    public async Task<(bool ok, string? error)> RejectAsync(FranchiseRejectionRequest req, Guid actorUserId)
    {
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == req.FranchiseId);
        if (f == null) return (false, "Franchise not found.");
        if (f.Status != FranchiseStatus.Pending && f.Status != FranchiseStatus.Unverified)
            return (false, "Only pending or unverified applications can be rejected.");
        if (string.IsNullOrWhiteSpace(req.Remarks)) return (false, "Rejection remarks are required.");

        f.Status = FranchiseStatus.Rejected;
        f.IsActive = false;
        f.RejectionRemarks = req.Remarks.Trim();
        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorUserId, "Admin", "FranchiseRejected", "Franchise", f.Id.ToString(), f.RejectionRemarks);

        if (!string.IsNullOrWhiteSpace(f.ContactEmail))
        {
            await _notify.SendAsync("franchisee_rejected",
                new NotificationRecipient(f.ContactEmail, f.ContactPhone),
                new Dictionary<string, string>
                {
                    ["name"] = f.Name,
                    ["business"] = f.BusinessName ?? f.Name,
                    ["remarks"] = f.RejectionRemarks!
                });
        }
        return (true, null);
    }

    public async Task<(bool ok, string? error)> SetActiveAsync(Guid franchiseId, bool isActive, Guid actorUserId)
    {
        var f = await _db.Franchises.Include(x => x.AdminUser).FirstOrDefaultAsync(x => x.Id == franchiseId);
        if (f == null) return (false, "Franchise not found.");
        if (f.Status != FranchiseStatus.Approved) return (false, "Only approved franchises can be activated/deactivated.");
        f.IsActive = isActive;
        if (f.AdminUser != null) f.AdminUser.IsActive = isActive;     // also gate the login
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorUserId, "Admin", isActive ? "FranchiseActivated" : "FranchiseDeactivated",
            "Franchise", f.Id.ToString(), null);
        return (true, null);
    }

    public async Task<(bool ok, string? error)> ResetPasswordAsync(Guid franchiseId, Guid actorUserId)
    {
        var f = await _db.Franchises.Include(x => x.AdminUser).FirstOrDefaultAsync(x => x.Id == franchiseId);
        if (f == null) return (false, "Franchise not found.");
        if (f.AdminUser == null) return (false, "No linked user account.");
        if (string.IsNullOrWhiteSpace(f.ContactEmail)) return (false, "Franchise has no contact email.");

        var plain = GeneratePassword();
        f.AdminUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(plain);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorUserId, "Admin", "FranchisePasswordReset", "Franchise", f.Id.ToString(), null);

        await _notify.SendAsync("franchisee_password_reset",
            new NotificationRecipient(f.ContactEmail, f.ContactPhone),
            new Dictionary<string, string>
            {
                ["name"] = f.Name,
                ["email"] = f.ContactEmail,
                ["password"] = plain,
                ["login_url"] = LoginUrl()
            });
        return (true, null);
    }

    // Generates a 12-character mixed-case alphanumeric password using cryptographic randomness.
    private static string GeneratePassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
        var sb = new System.Text.StringBuilder(12);
        for (var i = 0; i < 12; i++) sb.Append(chars[bytes[i] % chars.Length]);
        return sb.ToString();
    }

    // ─────────────── Phase 2 — Per-franchise product commission ───────────────

    public async Task<List<FranchiseCommissionRow>> ListCommissionsAsync(Guid franchiseId)
    {
        var rules = await _db.FranchiseCommissions.Where(c => c.FranchiseId == franchiseId)
            .ToDictionaryAsync(c => c.ProductId);
        var products = await _db.Products.Where(p => p.Status == ProductStatus.Active)
            .OrderBy(p => p.Title)
            .Select(p => new { p.Id, p.Slug, p.Title, p.SellingPrice,
                               p.EnableDefaultFranchiseShare, p.DefaultFranchiseShareType, p.DefaultFranchiseShareValue })
            .ToListAsync();
        return products.Select(p =>
        {
            rules.TryGetValue(p.Id, out var r);
            return new FranchiseCommissionRow
            {
                ProductId = p.Id, Slug = p.Slug, Title = p.Title,
                SellingPrice = p.SellingPrice,
                DefaultShareEnabled = p.EnableDefaultFranchiseShare,
                DefaultShareType = p.DefaultFranchiseShareType,
                DefaultShareValue = p.DefaultFranchiseShareValue,
                HasRule = r != null,
                Type = r?.Type ?? p.DefaultFranchiseShareType,
                Value = r?.Value ?? p.DefaultFranchiseShareValue
            };
        }).ToList();
    }

    public async Task<(bool ok, string? error)> SetCommissionAsync(SetCommissionRequest req, Guid actorUserId)
    {
        var (ok, err) = ValidateCommission(req.Type, req.Value);
        if (!ok) return (false, err);
        if (!await _db.Franchises.AnyAsync(f => f.Id == req.FranchiseId)) return (false, "Franchise not found.");
        if (!await _db.Products.AnyAsync(p => p.Id == req.ProductId)) return (false, "Product not found.");

        var row = await _db.FranchiseCommissions.FirstOrDefaultAsync(c => c.FranchiseId == req.FranchiseId && c.ProductId == req.ProductId);
        if (row == null)
            _db.FranchiseCommissions.Add(new FranchiseCommission
            {
                FranchiseId = req.FranchiseId, ProductId = req.ProductId,
                Type = req.Type, Value = req.Value, EffectiveFrom = DateTime.UtcNow, IsActive = true
            });
        else
        {
            row.Type = req.Type; row.Value = req.Value; row.IsActive = true;
            row.EffectiveFrom = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorUserId, "Admin", "FranchiseCommissionSet", "Franchise", req.FranchiseId.ToString(),
            $"Product={req.ProductId}; Type={req.Type}; Value={req.Value}");
        return (true, null);
    }

    public async Task<(bool ok, string? error, int updated)> BulkSetCommissionsAsync(BulkSetCommissionRequest req, Guid actorUserId)
    {
        var (ok, err) = ValidateCommission(req.Type, req.Value);
        if (!ok) return (false, err, 0);
        if (!await _db.Franchises.AnyAsync(f => f.Id == req.FranchiseId)) return (false, "Franchise not found.", 0);
        if (req.ProductIds.Count == 0) return (false, "Select at least one product.", 0);

        var existing = await _db.FranchiseCommissions
            .Where(c => c.FranchiseId == req.FranchiseId && req.ProductIds.Contains(c.ProductId))
            .ToListAsync();
        var existingIds = existing.Select(c => c.ProductId).ToHashSet();
        foreach (var c in existing) { c.Type = req.Type; c.Value = req.Value; c.IsActive = true; c.EffectiveFrom = DateTime.UtcNow; }
        foreach (var pid in req.ProductIds.Where(p => !existingIds.Contains(p)))
            _db.FranchiseCommissions.Add(new FranchiseCommission
            {
                FranchiseId = req.FranchiseId, ProductId = pid,
                Type = req.Type, Value = req.Value, EffectiveFrom = DateTime.UtcNow, IsActive = true
            });
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorUserId, "Admin", "FranchiseCommissionBulkSet", "Franchise", req.FranchiseId.ToString(),
            $"Products={req.ProductIds.Count}; Type={req.Type}; Value={req.Value}");
        return (true, null, req.ProductIds.Count);
    }

    public async Task<ErpShareImportResult> ImportErpFranchiseSharesAsync(ErpShareImportRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var result = new ErpShareImportResult { DryRun = request.DryRun };
        try
        {
            return await ImportErpFranchiseSharesInnerAsync(request, actorUserId, result, ct);
        }
        catch (Exception ex)
        {
            // Surface the REAL exception (unwrap to the innermost cause) so we can see what actually
            // failed instead of only the Blazor/SignalR NullReferenceException that bubbles up when
            // an unhandled exception passes through the circuit.
            var deepest = ex;
            while (deepest.InnerException != null) deepest = deepest.InnerException;
            result.Ok = false;
            result.Error = $"{deepest.GetType().Name}: {deepest.Message}"
                + (string.IsNullOrEmpty(deepest.StackTrace) ? "" : $"\n\n{deepest.StackTrace}");
            return result;
        }
    }

    private async Task<ErpShareImportResult> ImportErpFranchiseSharesInnerAsync(ErpShareImportRequest request, Guid actorUserId, ErpShareImportResult result, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.SourceConnectionString))
        { result.Ok = false; result.Error = "ERP SQL Server connection string is required."; return result; }

        // New-side lookups: SKU (lower) -> product Guid, email (lower) -> franchise Guid.
        var productBySku = await _db.Products
            .Where(p => p.Sku != null && p.Sku != "")
            .ToDictionaryAsync(p => p.Sku!.ToLower(), p => p.Id, ct);
        var franchiseByEmail = await _db.Franchises
            .Where(f => f.ContactEmail != null && f.ContactEmail != "")
            .ToDictionaryAsync(f => f.ContactEmail!.ToLower(), f => f.Id, ct);

        // Read ERP shares joined to Products (SKUCode) and Franchisee (Email), active only.
        var rows = new List<(string? Sku, string? Email, short SharingType, double Amount)>();
        try
        {
            await using var sql = new SqlConnection(request.SourceConnectionString);
            await sql.OpenAsync(ct);
            const string q = @"
SELECT pr.[SKUCode], f.[Email], s.[eSharingType], s.[SharingAmount]
FROM dbo.ProductFranchiseeShare s
JOIN dbo.Products pr    ON pr.[Id] = s.[ProductId]
JOIN dbo.Franchisee f   ON f.[Id]  = s.[FranchiseeId]
WHERE s.[IsActive] = 1;";
            await using var cmd = new SqlCommand(q, sql);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                rows.Add((
                    rd.IsDBNull(0) ? null : rd.GetString(0)?.Trim(),
                    rd.IsDBNull(1) ? null : rd.GetString(1)?.Trim(),
                    rd.IsDBNull(2) ? (short)0 : rd.GetInt16(2),
                    rd.IsDBNull(3) ? 0d : Convert.ToDouble(rd.GetValue(3))));
            }
        }
        catch (Exception ex)
        {
            result.Ok = false;
            result.Error = "Could not read ERP: " + ex.Message;
            return result;
        }

        result.Read = rows.Count;

        // Cap the per-row items sent back to the browser. Rendering all ~6k rows through the Blazor
        // SignalR circuit overwhelms it (EndInvokeDotNet/SendAsync failure). Counts stay exact; we
        // only keep a sample of rows for display, prioritising problems (skipped/error) over the
        // routine "mapped" rows the admin doesn't need to scroll through.
        const int ItemCap = 300;
        void AddItem(ErpShareItemResult it)
        {
            if (it.Outcome == "mapped")
            {
                if (result.Items.Count < ItemCap) result.Items.Add(it);
            }
            else
            {
                // Always try to surface problems; if at cap, drop an older "mapped" row to make room.
                if (result.Items.Count >= ItemCap)
                {
                    var idx = result.Items.FindIndex(x => x.Outcome == "mapped");
                    if (idx >= 0) result.Items.RemoveAt(idx);
                }
                if (result.Items.Count < ItemCap) result.Items.Add(it);
            }
        }

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var item = new ErpShareItemResult { Sku = row.Sku ?? "", FranchiseeEmail = row.Email ?? "" };

            if (string.IsNullOrWhiteSpace(row.Sku) || !productBySku.TryGetValue(row.Sku!.ToLowerInvariant(), out var productId))
            { item.Outcome = "skipped"; item.Message = "No new product with this SKU."; result.Skipped++; AddItem(item); continue; }

            if (string.IsNullOrWhiteSpace(row.Email) || !franchiseByEmail.TryGetValue(row.Email!.ToLowerInvariant(), out var franchiseId))
            { item.Outcome = "skipped"; item.Message = "No franchise with this email."; result.Skipped++; AddItem(item); continue; }

            // eSharingType: 1 = Percent, 2 = Fixed.
            var type = row.SharingType == 2 ? CommissionType.Fixed : CommissionType.Percent;
            var value = (decimal)row.Amount;

            if (request.DryRun)
            { item.Outcome = "mapped"; result.Mapped++; AddItem(item); continue; }

            try
            {
                var (ok, err) = await SetCommissionAsync(new SetCommissionRequest
                {
                    FranchiseId = franchiseId, ProductId = productId, Type = type, Value = value
                }, actorUserId);
                if (ok) { item.Outcome = "mapped"; result.Mapped++; }
                else { item.Outcome = "error"; item.Message = err; result.Errors++; }
            }
            catch (Exception ex) { item.Outcome = "error"; item.Message = ex.Message; result.Errors++; }

            AddItem(item);
        }

        if (!request.DryRun)
            await _audit.LogAsync(actorUserId, "Admin", "FranchiseShareErpImport", "Franchise", "",
                $"Mapped={result.Mapped}; Skipped={result.Skipped}; Errors={result.Errors} of {result.Read}.");

        result.Ok = true;
        return result;
    }

    public async Task<(bool ok, string? error, int updated)> BulkAssignAcrossFranchiseesAsync(BulkAssignAcrossFranchiseesRequest req, Guid actorUserId)
    {
        var (ok, err) = ValidateCommission(req.Type, req.Value);
        if (!ok) return (false, err, 0);
        if (req.ProductIds.Count == 0) return (false, "Select at least one product.", 0);

        // Resolve target franchisees: all active, or the explicit selection.
        List<Guid> franchiseIds;
        if (req.AllFranchisees)
            franchiseIds = await _db.Franchises.Where(f => f.IsActive).Select(f => f.Id).ToListAsync();
        else
        {
            franchiseIds = req.FranchiseIds.Distinct().ToList();
            if (franchiseIds.Count == 0) return (false, "Select at least one franchisee (or choose all).", 0);
            var validCount = await _db.Franchises.CountAsync(f => franchiseIds.Contains(f.Id));
            if (validCount != franchiseIds.Count) return (false, "One or more selected franchisees were not found.", 0);
        }
        if (franchiseIds.Count == 0) return (false, "No franchisees to assign.", 0);

        var now = DateTime.UtcNow;
        var pairs = 0;

        // Load all existing rules for the target (franchise, product) pairs in one query, then upsert.
        var existing = await _db.FranchiseCommissions
            .Where(c => franchiseIds.Contains(c.FranchiseId) && req.ProductIds.Contains(c.ProductId))
            .ToListAsync();
        var byKey = existing.ToDictionary(c => (c.FranchiseId, c.ProductId));

        foreach (var fid in franchiseIds)
        foreach (var pid in req.ProductIds.Distinct())
        {
            if (byKey.TryGetValue((fid, pid), out var rule))
            {
                rule.Type = req.Type; rule.Value = req.Value; rule.IsActive = true; rule.EffectiveFrom = now;
            }
            else
            {
                _db.FranchiseCommissions.Add(new FranchiseCommission
                {
                    FranchiseId = fid, ProductId = pid,
                    Type = req.Type, Value = req.Value, EffectiveFrom = now, IsActive = true
                });
            }
            pairs++;
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorUserId, "Admin", "FranchiseCommissionBulkAssignAcross", "Franchise",
            req.AllFranchisees ? "ALL" : string.Join(",", franchiseIds),
            $"Franchisees={franchiseIds.Count}; Products={req.ProductIds.Count}; Pairs={pairs}; Type={req.Type}; Value={req.Value}");
        return (true, null, pairs);
    }

    public async Task<(bool ok, string? error)> ClearCommissionAsync(Guid franchiseId, Guid productId, Guid actorUserId)
    {
        var row = await _db.FranchiseCommissions.FirstOrDefaultAsync(c => c.FranchiseId == franchiseId && c.ProductId == productId);
        if (row == null) return (true, null);     // already absent → no-op
        _db.FranchiseCommissions.Remove(row);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorUserId, "Admin", "FranchiseCommissionCleared", "Franchise", franchiseId.ToString(),
            $"Product={productId}");
        return (true, null);
    }

    public async Task<List<FranchiseAssignmentRow>> ListAssignmentsAsync()
    {
        // One row per FranchiseCommission, joined to franchise + product for display and
        // compared against the product's own default share to label Default vs Custom.
        var rows = await _db.FranchiseCommissions
            .Join(_db.Franchises, c => c.FranchiseId, f => f.Id, (c, f) => new { c, f })
            .Join(_db.Products, x => x.c.ProductId, p => p.Id, (x, p) => new { x.c, x.f, p })
            .OrderByDescending(x => x.c.EffectiveFrom)
            .Select(x => new FranchiseAssignmentRow
            {
                FranchiseId = x.c.FranchiseId,
                ProductId = x.c.ProductId,
                FranchiseName = x.f.Name,
                FranchiseCode = x.f.Code,
                ProductTitle = x.p.Title,
                Type = x.c.Type,
                AssignedValue = x.c.Value,
                ProductDefaultEnabled = x.p.EnableDefaultFranchiseShare,
                ProductDefaultType = x.p.DefaultFranchiseShareType,
                ProductDefaultValue = x.p.DefaultFranchiseShareValue,
                IsActive = x.c.IsActive,
                AssignedAt = x.c.EffectiveFrom
            })
            .ToListAsync();

        // "Custom" when the assigned share differs from the product's default (or the product has no default).
        foreach (var r in rows)
            r.IsCustom = !(r.ProductDefaultEnabled
                           && r.Type == r.ProductDefaultType
                           && r.AssignedValue == r.ProductDefaultValue);
        return rows;
    }

    public async Task<(bool ok, string? error)> UpdateAssignmentAsync(UpdateAssignmentRequest req, Guid actorUserId)
    {
        var (valid, err) = ValidateCommission(req.Type, req.Value);
        if (!valid) return (false, err);

        var row = await _db.FranchiseCommissions
            .FirstOrDefaultAsync(c => c.FranchiseId == req.FranchiseId && c.ProductId == req.ProductId);
        if (row == null) return (false, "Assignment not found.");

        row.Type = req.Type;
        row.Value = req.Value;
        row.EffectiveFrom = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorUserId, "Admin", "FranchiseAssignmentUpdated", "Franchise", req.FranchiseId.ToString(),
            $"Product={req.ProductId}; {req.Type}={req.Value}");
        return (true, null);
    }

    public async Task<(bool ok, string? error)> ToggleAssignmentAsync(Guid franchiseId, Guid productId, bool active, Guid actorUserId)
    {
        var row = await _db.FranchiseCommissions
            .FirstOrDefaultAsync(c => c.FranchiseId == franchiseId && c.ProductId == productId);
        if (row == null) return (false, "Assignment not found.");
        if (row.IsActive == active) return (true, null);

        row.IsActive = active;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorUserId, "Admin", active ? "FranchiseAssignmentEnabled" : "FranchiseAssignmentDisabled",
            "Franchise", franchiseId.ToString(), $"Product={productId}");
        return (true, null);
    }

    public async Task<(bool ok, string? error)> RemoveAssignmentAsync(Guid franchiseId, Guid productId, Guid actorUserId)
        => await ClearCommissionAsync(franchiseId, productId, actorUserId);

    public async Task<PagedResult<FranchiseEarningRow>> GetEarningsAsync(Guid franchiseId, DateTime? from, DateTime? to, int page, int pageSize)
    {
        var q = _db.FranchiseCommissionEntries.Where(e => e.FranchiseId == franchiseId);
        if (from.HasValue) q = q.Where(e => e.EarnedAt >= from);
        if (to.HasValue) q = q.Where(e => e.EarnedAt <= to);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(e => e.EarnedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new FranchiseEarningRow
            {
                Id = e.Id, OrderNumber = e.OrderNumber, ProductId = e.ProductId, ProductTitle = e.ProductTitle,
                BaseAmount = e.BaseAmount, Type = e.Type, Value = e.Value,
                CommissionAmount = e.CommissionAmount, EarnedAt = e.EarnedAt
            }).ToListAsync();
        return new PagedResult<FranchiseEarningRow> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    public async Task<FranchiseEarningsSummary> GetEarningsSummaryAsync(Guid franchiseId)
    {
        var q = _db.FranchiseCommissionEntries.Where(e => e.FranchiseId == franchiseId);
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var lastMonthStart = monthStart.AddMonths(-1);

        var orderCount = await q.Select(e => e.OrderId).Distinct().CountAsync();
        var total = await q.SumAsync(e => (decimal?)e.CommissionAmount) ?? 0;
        var mtd = await q.Where(e => e.EarnedAt >= monthStart).SumAsync(e => (decimal?)e.CommissionAmount) ?? 0;
        var last = await q.Where(e => e.EarnedAt >= lastMonthStart && e.EarnedAt < monthStart).SumAsync(e => (decimal?)e.CommissionAmount) ?? 0;
        return new FranchiseEarningsSummary(orderCount, total, mtd, last);
    }

    public async Task RecordOrderCommissionAsync(Guid orderId)
    {
        var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null || order.FranchiseId == null) return;
        if (await _db.FranchiseCommissionEntries.AnyAsync(e => e.OrderId == orderId)) return;   // idempotent

        var franchiseId = order.FranchiseId.Value;
        var settings = await GetSettingsAsync();
        // A registered franchisee's commission carries GST; an unregistered one's does not, so the
        // ledger has to know before it can record what was earned.
        var commissionGstin = await _db.Franchises.AsNoTracking()
            .Where(x => x.Id == franchiseId).Select(x => x.Gstin).FirstOrDefaultAsync();
        var isGstRegistered = !string.IsNullOrWhiteSpace(commissionGstin);
        var productIds = order.Items.Select(i => i.ProductId).ToList();
        var rules = await _db.FranchiseCommissions
            .Where(c => c.FranchiseId == franchiseId && productIds.Contains(c.ProductId))
            .ToDictionaryAsync(c => c.ProductId);
        var products = await _db.Products.Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        foreach (var item in order.Items)
        {
            products.TryGetValue(item.ProductId, out var product);
            if (product == null) continue;
            rules.TryGetValue(item.ProductId, out var rule);

            // Single source of truth — same calculator the portal listing & admin grid use,
            // so a franchisee's displayed earning matches what they actually earn at order time.
            var calc = _shares.Calculate(product, rule, settings, isGstRegistered);
            if (!calc.HasShare) continue;

            // Per-unit base × quantity. The base is the PRE-GST (taxable) value of the price the
            // share applies to — special when active and allowed by settings, else regular — which
            // is what the commission was actually computed against.
            var perUnitGross = settings.ApplyShareOnSpecialPrice
                ? _shares.EffectivePrice(product)
                : product.SellingPrice;
            var baseAmount = Math.Round(
                FranchiseCommissionMath.BaseFromGross(perUnitGross, product.GstRate) * item.Quantity, 2);

            // Scale the per-unit split out to the line quantity; the calculator already applied
            // Steps 1–3, so this must not re-derive the commission from the base.
            var split = new FranchiseCommissionMath.CommissionSplit(
                calc.CommissionAmount, calc.GstOnCommission, calc.CalculatedFranchiseAmount)
                .Times(item.Quantity);
            if (split.TotalPayout <= 0) continue;

            _db.FranchiseCommissionEntries.Add(new FranchiseCommissionEntry
            {
                FranchiseId = franchiseId, OrderId = orderId, OrderNumber = order.OrderNumber,
                OrderItemId = item.Id, ProductId = item.ProductId, ProductTitle = item.ProductTitle,
                BaseAmount = baseAmount, Type = calc.ShareType, Value = calc.ShareValue,
                CommissionAmount = split.Commission,
                GstOnCommission = split.GstOnCommission,
                TotalPayout = split.TotalPayout,
                EarnedAt = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Resolves which commission applies for a (product, franchise) pair, in priority order:
    /// 1) an active per-franchise override row (when overrides are allowed),
    /// 2) the product's default franchise share (when enabled),
    /// 3) none. Shared by order commission, earnings/settlement/invoice reports, and previews.
    /// </summary>
    public static (CommissionType Type, decimal Value)? ResolveCommission(
        Product? product, FranchiseCommission? rule, FranchiseSettings settings)
    {
        if (rule is { IsActive: true } && settings.AllowFranchiseSpecificOverride)
            return (rule.Type, rule.Value);
        if (product is { EnableDefaultFranchiseShare: true } && product.DefaultFranchiseShareValue > 0)
            return (product.DefaultFranchiseShareType, product.DefaultFranchiseShareValue);
        return null;
    }

    /// <summary>Per-unit base a percentage share is applied to: special price when active and
    /// allowed by settings, otherwise the regular selling price.</summary>
    public static decimal FranchiseShareBase(Product? product, FranchiseSettings settings)
    {
        if (product == null) return 0;
        return settings.ApplyShareOnSpecialPrice ? product.EffectiveSellingPrice : product.SellingPrice;
    }

    /// <summary>Loads the singleton franchise settings, creating defaults if somehow missing.</summary>
    public async Task<FranchiseSettings> GetSettingsAsync()
    {
        var s = await _db.FranchiseSettings.FirstOrDefaultAsync();
        if (s == null)
        {
            s = new FranchiseSettings { Id = Guid.NewGuid() };
            _db.FranchiseSettings.Add(s);
            await _db.SaveChangesAsync();
        }
        return s;
    }

    public async Task SaveSettingsAsync(FranchiseSettings settings, Guid actorUserId)
    {
        var s = await GetSettingsAsync();
        s.AutoAssignNewProductsToAllFranchises = settings.AutoAssignNewProductsToAllFranchises;
        s.AutoAssignAllProductsToNewFranchise = settings.AutoAssignAllProductsToNewFranchise;
        s.AllowFranchiseSpecificOverride = settings.AllowFranchiseSpecificOverride;
        s.ApplyShareOnSpecialPrice = settings.ApplyShareOnSpecialPrice;
        s.EnableAutomaticAssignment = settings.EnableAutomaticAssignment;
        s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorUserId, "Admin", "FranchiseSettingsSaved", "FranchiseSettings", s.Id.ToString(),
            $"AutoNewProd={s.AutoAssignNewProductsToAllFranchises}; AutoNewFranchise={s.AutoAssignAllProductsToNewFranchise}; Override={s.AllowFranchiseSpecificOverride}; SpecialPrice={s.ApplyShareOnSpecialPrice}; AutoEnabled={s.EnableAutomaticAssignment}");
    }

    public async Task<int> AutoAssignProductToFranchisesAsync(Guid productId)
    {
        var settings = await GetSettingsAsync();
        if (!settings.EnableAutomaticAssignment || !settings.AutoAssignNewProductsToAllFranchises) return 0;

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product is not { EnableDefaultFranchiseShare: true } || product.DefaultFranchiseShareValue <= 0) return 0;

        var franchiseIds = await _db.Franchises.Where(f => f.IsActive).Select(f => f.Id).ToListAsync();
        var existing = await _db.FranchiseCommissions
            .Where(c => c.ProductId == productId && franchiseIds.Contains(c.FranchiseId))
            .Select(c => c.FranchiseId).ToListAsync();
        var existingSet = existing.ToHashSet();

        var now = DateTime.UtcNow; var created = 0;
        foreach (var fid in franchiseIds)
        {
            if (existingSet.Contains(fid)) continue;     // don't clobber an existing override
            _db.FranchiseCommissions.Add(new FranchiseCommission
            {
                FranchiseId = fid, ProductId = productId,
                Type = product.DefaultFranchiseShareType, Value = product.DefaultFranchiseShareValue,
                EffectiveFrom = now, IsActive = true
            });
            created++;
        }
        if (created > 0) await _db.SaveChangesAsync();
        return created;
    }

    public async Task<int> AutoAssignProductsToFranchiseAsync(Guid franchiseId)
    {
        var settings = await GetSettingsAsync();
        if (!settings.EnableAutomaticAssignment || !settings.AutoAssignAllProductsToNewFranchise) return 0;

        var products = await _db.Products
            .Where(p => p.Status == ProductStatus.Active && p.EnableDefaultFranchiseShare && p.DefaultFranchiseShareValue > 0)
            .Select(p => new { p.Id, p.DefaultFranchiseShareType, p.DefaultFranchiseShareValue })
            .ToListAsync();
        if (products.Count == 0) return 0;

        var existing = await _db.FranchiseCommissions
            .Where(c => c.FranchiseId == franchiseId)
            .Select(c => c.ProductId).ToListAsync();
        var existingSet = existing.ToHashSet();

        var now = DateTime.UtcNow; var created = 0;
        foreach (var p in products)
        {
            if (existingSet.Contains(p.Id)) continue;
            _db.FranchiseCommissions.Add(new FranchiseCommission
            {
                FranchiseId = franchiseId, ProductId = p.Id,
                Type = p.DefaultFranchiseShareType, Value = p.DefaultFranchiseShareValue,
                EffectiveFrom = now, IsActive = true
            });
            created++;
        }
        if (created > 0) await _db.SaveChangesAsync();
        return created;
    }

    private static (bool ok, string? error) ValidateCommission(CommissionType type, decimal value)
    {
        if (value < 0) return (false, "Value can't be negative.");
        if (type == CommissionType.Percent && value > 100) return (false, "Percent commission can't exceed 100%.");
        return (true, null);
    }

    // ─────────────── Phase 3 — Wallet self-service recharge + manual adjustment ───────────────

    public async Task<(bool ok, string? error, RechargeIntent? intent)> InitiateRechargeAsync(Guid franchiseId, decimal amount)
    {
        if (amount <= 0) return (false, "Enter an amount greater than zero.", null);
        if (amount > 500000m) return (false, "Single recharge can't exceed ₹5,00,000. Please split across smaller top-ups.", null);
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == franchiseId);
        if (f == null) return (false, "Franchise not found.", null);
        if (f.Status != FranchiseStatus.Approved || !f.IsActive) return (false, "Account isn't active — contact support.", null);

        var rechargeId = Guid.NewGuid();
        // Reference shown to the gateway for traceability (not the order_number scheme).
        var reference = $"WRCH-{rechargeId.ToString("N")[..10].ToUpper()}";
        GatewayOrder go;
        try
        {
            // Razorpay popup flow — no GatewayCreateContext needed (prefill comes from the browser).
            go = await _gateways.Get("Razorpay").CreateOrderAsync(reference, amount);
        }
        catch (Exception ex)
        {
            // Surface a clean message (e.g. keys not configured) instead of a 500.
            return (false, ex.Message, null);
        }

        _db.WalletRecharges.Add(new WalletRecharge
        {
            Id = rechargeId, FranchiseId = franchiseId, Amount = amount,
            Status = WalletRechargeStatus.Pending,
            GatewayName = go.GatewayName, GatewayOrderId = go.GatewayOrderId
        });
        await _db.SaveChangesAsync();
        return (true, null, new RechargeIntent(rechargeId, go.GatewayName, go.GatewayOrderId, go.GatewayKey, amount));
    }

    public async Task<(bool ok, string? error, decimal newBalance)> ConfirmRechargeAsync(Guid franchiseId, ConfirmRechargeRequest req)
    {
        var r = await _db.WalletRecharges.FirstOrDefaultAsync(x => x.Id == req.RechargeId && x.FranchiseId == franchiseId);
        if (r == null) return (false, "Recharge not found.", 0);
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == franchiseId);
        if (f == null) return (false, "Franchise not found.", 0);
        if (r.Status == WalletRechargeStatus.Success) return (true, null, f.WalletBalance);    // idempotent
        if (r.Status == WalletRechargeStatus.Failed) return (false, r.FailureReason ?? "Recharge failed.", f.WalletBalance);

        // Gateway must agree the GatewayOrderId on this request matches the one we issued.
        if (!string.Equals(r.GatewayOrderId, req.GatewayOrderId, StringComparison.Ordinal))
        { r.Status = WalletRechargeStatus.Failed; r.FailureReason = "Gateway order id mismatch."; await _db.SaveChangesAsync(); return (false, r.FailureReason, f.WalletBalance); }

        var ok = _gateways.Get("Razorpay").VerifyPayment(r.GatewayOrderId, req.GatewayPaymentId, req.GatewaySignature);
        if (!ok)
        { r.Status = WalletRechargeStatus.Failed; r.FailureReason = "Gateway signature verification failed."; await _db.SaveChangesAsync(); return (false, r.FailureReason, f.WalletBalance); }

        // Credit wallet + write ledger row.
        f.WalletBalance += r.Amount;
        // Gateway-verified money in → invoiced like any other top-up.
        var topUpOrder = await BuildWalletCreditOrderAsync(
            f, r.Amount, $"Wallet recharge ({r.GatewayName})", PaymentMode.Razorpay);

        var ledger = new FranchiseLedgerEntry
        {
            FranchiseId = franchiseId, IsCredit = true, Amount = r.Amount, BalanceAfter = f.WalletBalance,
            Description = $"Wallet recharge ({r.GatewayName})",
            OrderId = topUpOrder.Id
        };
        _db.FranchiseLedger.Add(ledger);

        r.Status = WalletRechargeStatus.Success;
        r.GatewayPaymentId = req.GatewayPaymentId;
        r.GatewaySignature = req.GatewaySignature;
        r.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();          // ledger.Id is now populated
        r.LedgerEntryId = ledger.Id;
        await _db.SaveChangesAsync();
        await InvoiceWalletCreditAsync(topUpOrder, null, "wallet-recharge");

        if (!string.IsNullOrWhiteSpace(f.ContactEmail))
        {
            await _notify.SendAsync("wallet_recharged",
                new NotificationRecipient(f.ContactEmail, f.ContactPhone),
                new Dictionary<string, string>
                {
                    ["name"] = f.Name,
                    ["amount"] = r.Amount.ToString("N2"),
                    ["balance"] = f.WalletBalance.ToString("N2"),
                    ["recharge_id"] = r.Id.ToString()
                });
        }
        return (true, null, f.WalletBalance);
    }

    public async Task<List<WalletRechargeRow>> ListRechargesAsync(Guid franchiseId, int take = 50)
        => await _db.WalletRecharges.Where(r => r.FranchiseId == franchiseId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .Select(r => new WalletRechargeRow
            {
                Id = r.Id, Amount = r.Amount, Status = r.Status,
                GatewayName = r.GatewayName, GatewayOrderId = r.GatewayOrderId, GatewayPaymentId = r.GatewayPaymentId,
                CreatedAt = r.CreatedAt, CompletedAt = r.CompletedAt, FailureReason = r.FailureReason
            }).ToListAsync();

    public async Task<FranchiseWalletStatement?> WalletStatementAsync(
        Guid franchiseId, DateTime? from = null, DateTime? to = null, int take = 500)
    {
        var f = await _db.Franchises.AsNoTracking().FirstOrDefaultAsync(x => x.Id == franchiseId);
        if (f == null) return null;

        var q = _db.FranchiseLedger.AsNoTracking().Where(e => e.FranchiseId == franchiseId);
        if (from.HasValue) q = q.Where(e => e.CreatedAt >= from.Value);
        if (to.HasValue) q = q.Where(e => e.CreatedAt <= to.Value);

        // Totals run over the whole filtered window so they stay correct when the list below is capped.
        var credited = await q.Where(e => e.IsCredit).SumAsync(e => (decimal?)e.Amount) ?? 0m;
        var debited = await q.Where(e => !e.IsCredit).SumAsync(e => (decimal?)e.Amount) ?? 0m;
        var count = await q.CountAsync();

        var rows = await q.OrderByDescending(e => e.CreatedAt).Take(take).ToListAsync();

        // Resolve order numbers for the deduction rows. IgnoreQueryFilters so a soft-deleted order
        // still shows its number rather than a bare dash on a ledger row that debited real money.
        var orderNumbers = await _db.Orders.IgnoreQueryFilters()
            .Where(o => o.FranchiseId == franchiseId)
            .Select(o => new { o.Id, o.OrderNumber })
            .ToDictionaryAsync(o => o.Id, o => o.OrderNumber);

        var entries = rows.Select(e => new FranchiseLedgerItem(
            e.Id, e.IsCredit, e.Amount, e.BalanceAfter, e.Description,
            e.OrderId.HasValue && orderNumbers.TryGetValue(e.OrderId.Value, out var n) ? n : null,
            e.CreatedAt, e.OrderId)).ToList();

        return new FranchiseWalletStatement(
            f.Id, f.Name, f.Code, f.BusinessName,
            f.WalletBalance, f.CreditLimit, f.WalletBalance + f.CreditLimit,
            credited, debited, count, entries);
    }

    public async Task<string> GetStatementCsvAsync(Guid franchiseId, DateTime? from, DateTime? to)
    {
        var q = _db.FranchiseLedger.Where(e => e.FranchiseId == franchiseId);
        if (from.HasValue) q = q.Where(e => e.CreatedAt >= from);
        if (to.HasValue) q = q.Where(e => e.CreatedAt <= to);
        var rows = await q.OrderBy(e => e.CreatedAt).ToListAsync();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Date,Type,Amount,BalanceAfter,Description,OrderId");
        foreach (var e in rows)
        {
            var desc = (e.Description ?? "").Replace("\"", "\"\"");
            sb.AppendLine($"{e.CreatedAt:yyyy-MM-dd HH:mm:ss},{(e.IsCredit ? "Credit" : "Debit")},{e.Amount:F2},{e.BalanceAfter:F2},\"{desc}\",{e.OrderId}");
        }
        return sb.ToString();
    }

    public async Task<(bool ok, string? error)> AdjustWalletAsync(AdjustWalletRequest req, Guid actorUserId)
    {
        if (req.Amount <= 0) return (false, "Amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(req.Reason)) return (false, "Reason is required.");
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == req.FranchiseId);
        if (f == null) return (false, "Franchise not found.");

        if (req.IsCredit)
            f.WalletBalance += req.Amount;
        else
        {
            // Debit can drive the balance negative only down to -CreditLimit.
            var minAllowed = -f.CreditLimit;
            if (f.WalletBalance - req.Amount < minAllowed)
                return (false, $"Debit would breach credit limit (₹{f.CreditLimit:N0}). Current balance ₹{f.WalletBalance:N2}.");
            f.WalletBalance -= req.Amount;
        }

        // Money the franchisee actually paid gets invoiced: a WalletTopUp order carries it through
        // the ordinary invoice pipeline. Refunds and corrections are credits too but are NOT money
        // coming in, so they raise no invoice — that would inflate revenue and charge GST twice.
        Order? topUpOrder = null;
        if (req.IsCredit && req.MoneyReceived)
            topUpOrder = await BuildWalletCreditOrderAsync(f, req.Amount, req.Reason.Trim(), req.PaymentMode);

        _db.FranchiseLedger.Add(new FranchiseLedgerEntry
        {
            FranchiseId = f.Id, IsCredit = req.IsCredit, Amount = req.Amount, BalanceAfter = f.WalletBalance,
            Description = (req.IsCredit ? "Manual credit: " : "Manual debit: ") + req.Reason.Trim(),
            // Links the ledger row to its invoice, so the Wallet page can offer the PDF.
            OrderId = topUpOrder?.Id
        });
        // One save: balance + top-up order + ledger row commit together or not at all.
        await _db.SaveChangesAsync();
        await InvoiceWalletCreditAsync(topUpOrder, actorUserId, "Admin");

        await _audit.LogAsync(actorUserId, "Admin", req.IsCredit ? "WalletManualCredit" : "WalletManualDebit",
            "Franchise", f.Id.ToString(), $"Amount={req.Amount}; Reason={req.Reason}");
        return (true, null);
    }
}

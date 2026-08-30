using RioCommerce.Core.DTOs.Customers;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.Customers;

/// <inheritdoc cref="IFranchiseCustomerBackfillService"/>
public class FranchiseCustomerBackfillService : IFranchiseCustomerBackfillService
{
    private readonly RioCommerceDbContext _db;
    private readonly IStudentAccountProvisioner _students;
    private readonly ILogger<FranchiseCustomerBackfillService> _log;

    public FranchiseCustomerBackfillService(
        RioCommerceDbContext db,
        IStudentAccountProvisioner students,
        ILogger<FranchiseCustomerBackfillService> log)
    {
        _db = db;
        _students = students;
        _log = log;
    }

    public async Task<StudentLinkBackfillReport> RunAsync(bool dryRun, OrderSource? source = OrderSource.Franchisee, CancellationToken ct = default)
    {
        var query = _db.Orders.AsQueryable();
        if (source.HasValue) query = query.Where(o => o.Source == source.Value);

        // A dry run reads with NO tracking, so there is physically nothing for a SaveChanges anywhere
        // in the process to flush. The live pass needs tracking — it updates Order.UserId.
        if (dryRun) query = query.AsNoTracking();

        // Ascending order number matters: the FIRST order a student appears on is the one that creates
        // their account and whose number goes into the provenance note. Their later orders then find
        // that account instead of making another.
        var orders = await query.OrderBy(o => o.OrderNumber).ToListAsync(ct);

        _log.LogInformation("Franchise customer backfill starting. DryRun={DryRun} Source={Source} Orders={Count}",
            dryRun, source, orders.Count);

        var rows = new List<StudentLinkBackfillRow>(orders.Count);
        // Phone → the account this pass has already settled on, and the order that settled it.
        var resolved = new Dictionary<string, (Guid UserId, string OrderNumber)>(StringComparer.OrdinalIgnoreCase);
        // Phones this pass created (or would create) an account for — the "users created" figure, and
        // what separates a genuinely pre-existing customer from one we made a moment ago.
        var createdHere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int linkExisting = 0, createNew = 0, sameStudent = 0, noAction = 0, review = 0, failed = 0;

        foreach (var order in orders)
        {
            ct.ThrowIfCancellationRequested();

            // The idempotency guard. On a second pass every order the first one touched lands here.
            if (order.UserId.HasValue)
            {
                noAction++;
                rows.Add(Row(order, order.UserId, StudentLinkAction.NoAction, "Order is already linked to a customer."));
                continue;
            }

            StudentPhoneMatch match;
            try
            {
                match = await _students.FindCustomerByPhoneAsync(order.StudentPhone, ct);
            }
            catch (Exception ex)
            {
                // Never swallowed: counted, logged with the order context, and carried on the row.
                failed++;
                _log.LogError(ex, "Backfill lookup failed OrderNumber={OrderNumber} Phone={Phone}", order.OrderNumber, order.StudentPhone);
                rows.Add(Row(order, null, StudentLinkAction.ReviewRequired, "Customer lookup failed.", error: ex.Message));
                continue;
            }

            if (!match.PhoneUsable)
            {
                review++;
                _log.LogWarning("Backfill REVIEW_REQUIRED OrderNumber={OrderNumber} Phone={Phone} — not a usable identity.",
                    order.OrderNumber, order.StudentPhone);
                rows.Add(Row(order, null, StudentLinkAction.ReviewRequired,
                    "Mobile number is not a usable identity key (fewer than 10 digits)."));
                continue;
            }

            var phone = match.CanonicalPhone!;

            // ── This student was already settled earlier in this pass (their 2nd/3rd order) ────────
            if (resolved.TryGetValue(phone, out var prior))
            {
                var reason = createdHere.Contains(phone)
                    ? $"Same student as {prior.OrderNumber} — links to the account that order creates."
                    : $"Same student as {prior.OrderNumber} — links to the same existing account.";

                if (createdHere.Contains(phone)) sameStudent++; else linkExisting++;

                if (!await TryLinkAsync(order, prior.UserId, dryRun, ct))
                {
                    failed++;
                    if (createdHere.Contains(phone)) sameStudent--; else linkExisting--;
                    rows.Add(Row(order, null, StudentLinkAction.ReviewRequired, "Linking the order failed.", error: LastError));
                    continue;
                }

                rows.Add(Row(order, prior.UserId, StudentLinkAction.LinkExisting, reason));
                continue;
            }

            // The provenance line for an account this order would create. Uses THIS order's number
            // because ascending iteration makes it the student's earliest.
            var note = $"Created from Franchise Order {order.OrderNumber}";

            // ── An account already owns this phone ────────────────────────────────────────────────
            if (match.UserId is { } existingId)
            {
                linkExisting++;
                if (!await TryLinkAsync(order, existingId, dryRun, ct))
                {
                    failed++; linkExisting--;
                    rows.Add(Row(order, null, StudentLinkAction.ReviewRequired, "Linking the order failed.", error: LastError));
                    continue;
                }

                resolved[phone] = (existingId, order.OrderNumber);
                rows.Add(Row(order, existingId, StudentLinkAction.LinkExisting,
                    "An account already exists with this mobile number; it is linked and left unchanged."));
                continue;
            }

            // ── Nobody owns this phone: a new student account ─────────────────────────────────────
            if (dryRun)
            {
                createNew++;
                createdHere.Add(phone);
                // A dry run has no id to report, but it must still remember the decision so this
                // student's later orders report as links rather than as a second creation.
                resolved[phone] = (Guid.Empty, order.OrderNumber);
                rows.Add(Row(order, null, StudentLinkAction.CreateNew,
                    "No account exists for this mobile number; one would be created.", proposedComment: note));
                continue;
            }

            // The SAME entry point the live franchise order path uses — one implementation of
            // "resolve or create a student", including its unique-phone race recovery.
            var result = await _students.ResolveOrCreateAsync(new StudentAccountRequest(
                FullName: order.StudentName,
                Phone: order.StudentPhone,
                Email: order.StudentEmail,
                City: order.StudentCity,
                State: order.ShippingState,
                SourceNote: note), ct);

            if (!result.Linked)
            {
                failed++;
                _log.LogError("Backfill could not provision a customer OrderNumber={OrderNumber} Phone={Phone} Outcome={Outcome} Note={Note}",
                    order.OrderNumber, order.StudentPhone, result.Outcome, result.Note);
                rows.Add(Row(order, null, StudentLinkAction.ReviewRequired,
                    "Customer could not be provisioned; the order is left unlinked.", error: result.Note));
                continue;
            }

            var userId = result.UserId!.Value;

            // A race we lost hands back an existing account, not one we made — so it is a link, not a
            // creation, and must not be counted as a new user.
            var wasCreated = result.Outcome == StudentAccountOutcome.CreatedNew;
            if (wasCreated) { createNew++; createdHere.Add(phone); } else linkExisting++;

            if (!await TryLinkAsync(order, userId, dryRun, ct))
            {
                failed++;
                if (wasCreated) createNew--; else linkExisting--;
                rows.Add(Row(order, userId, StudentLinkAction.ReviewRequired,
                    "Customer exists but the order could not be linked to it.", error: LastError));
                continue;
            }

            resolved[phone] = (userId, order.OrderNumber);
            rows.Add(Row(order, userId,
                wasCreated ? StudentLinkAction.CreateNew : StudentLinkAction.LinkExisting,
                wasCreated
                    ? "No account existed for this mobile number; a student account was created."
                    : "An account already exists with this mobile number; it is linked and left unchanged.",
                proposedComment: wasCreated ? note : null));
        }

        var report = new StudentLinkBackfillReport(
            DryRun: dryRun,
            OrdersScanned: orders.Count,
            LinkExisting: linkExisting,
            CreateNew: createNew,
            LinkedToSameStudent: sameStudent,
            NoAction: noAction,
            ReviewRequired: review,
            Failed: failed,
            UsersCreated: createNew,
            OrdersLinked: linkExisting + createNew + sameStudent,
            Rows: rows);

        _log.LogInformation(
            "Franchise customer backfill finished. DryRun={DryRun} Scanned={Scanned} LinkExisting={Link} CreateNew={Create} SameStudent={Same} NoAction={NoAction} Review={Review} Failed={Failed}",
            dryRun, report.OrdersScanned, report.LinkExisting, report.CreateNew, report.LinkedToSameStudent,
            report.NoAction, report.ReviewRequired, report.Failed);

        return report;
    }

    /// <summary>The message from the most recent failed link, for the row that reports it.</summary>
    private string? LastError;

    /// <summary>
    /// Sets <c>Order.UserId</c> and commits that one order. Each order is its own SaveChanges — and so
    /// its own database transaction — which is what keeps a single bad row from taking the rest of the
    /// batch with it. On failure the in-memory change is rolled back so the next order's save cannot
    /// carry it along, and the error is logged rather than swallowed.
    /// </summary>
    private async Task<bool> TryLinkAsync(Order order, Guid userId, bool dryRun, CancellationToken ct)
    {
        LastError = null;
        if (dryRun) return true;   // nothing is written, by contract

        var previous = order.UserId;
        order.UserId = userId;
        try
        {
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Backfill linked OrderNumber={OrderNumber} Phone={Phone} UserId={UserId}",
                order.OrderNumber, order.StudentPhone, userId);
            return true;
        }
        catch (Exception ex)
        {
            order.UserId = previous;
            LastError = ex.Message;
            _log.LogError(ex, "Backfill failed to link OrderNumber={OrderNumber} Phone={Phone} UserId={UserId}",
                order.OrderNumber, order.StudentPhone, userId);
            return false;
        }
    }

    private static StudentLinkBackfillRow Row(
        Order order, Guid? userId, StudentLinkAction action, string reason,
        string? proposedComment = null, string? error = null)
        => new(order.Id, order.OrderNumber, order.StudentName, order.StudentPhone, order.StudentEmail,
               userId == Guid.Empty ? null : userId, action, reason, proposedComment, error);
}

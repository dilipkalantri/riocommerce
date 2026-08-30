using RioCommerce.Core.Enums;

namespace RioCommerce.Core.Entities;

/// <summary>
/// Installment plan attached to a counter (admin-created) order. Holds the agreed total, the
/// down payment collected at creation, and the schedule of remaining installments. While the
/// plan is active the order sits at <see cref="PaymentStatus.Pending"/> and no final tax invoice
/// is generated; when the last installment is fully collected the plan is marked complete, the
/// order flips to <see cref="PaymentStatus.Success"/> / <see cref="OrderStatus.Delivered"/>, and
/// the invoice is generated.
///
/// Tax lives in <see cref="TotalAmount"/> (the order total) — installments are collection slices,
/// not separately-taxed sales.
/// </summary>
public class OrderInstallmentPlan : BaseEntity
{
    public Guid OrderId { get; set; }

    /// <summary>The full amount to be collected — equals the order's grand total.</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Amount collected up-front at order creation (may be 0).</summary>
    public decimal DownPayment { get; set; }

    /// <summary>Number of scheduled installments AFTER the down payment.</summary>
    public int InstallmentCount { get; set; }

    /// <summary>Sum of all collected money so far (down payment + paid installments).</summary>
    public decimal PaidAmount { get; set; }

    /// <summary>True once every installment is fully paid.</summary>
    public bool IsCompleted { get; set; }

    public DateTime? CompletedAt { get; set; }

    public Order Order { get; set; } = null!;
    public ICollection<OrderInstallment> Installments { get; set; } = new List<OrderInstallment>();

    /// <summary>Convenience: total still owed across the whole plan.</summary>
    public decimal OutstandingAmount => Math.Max(0m, TotalAmount - PaidAmount);
}

public enum InstallmentStatus
{
    Pending,
    PartiallyPaid,
    Paid
}

/// <summary>One scheduled installment within an <see cref="OrderInstallmentPlan"/>.</summary>
public class OrderInstallment : BaseEntity
{
    public Guid PlanId { get; set; }
    public Guid OrderId { get; set; }

    /// <summary>1-based position in the schedule.</summary>
    public int InstallmentNumber { get; set; }

    public DateTime DueDate { get; set; }

    /// <summary>The amount due for this installment.</summary>
    public decimal Amount { get; set; }

    /// <summary>How much has been collected against this installment.</summary>
    public decimal PaidAmount { get; set; }

    public InstallmentStatus Status { get; set; } = InstallmentStatus.Pending;

    public DateTime? PaidAt { get; set; }

    /// <summary>Mode used to collect this installment (Cash / UPI / Cheque / Bank transfer …).</summary>
    public PaymentMode? PaidVia { get; set; }

    /// <summary>UTR / cheque no / reference captured at the counter.</summary>
    public string? PaymentReference { get; set; }

    /// <summary>Reminder bookkeeping so the scheduled task doesn't send duplicates.</summary>
    public DateTime? ReminderSentAt { get; set; }

    public OrderInstallmentPlan Plan { get; set; } = null!;

    public decimal PendingAmount => Math.Max(0m, Amount - PaidAmount);
}

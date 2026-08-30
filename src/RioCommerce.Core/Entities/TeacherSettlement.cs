using RioCommerce.Core.Enums;

namespace RioCommerce.Core.Entities;

/// <summary>
/// One faculty member's settlement for one period — the tracked counterpart of the
/// <see cref="FacultyShareEntry"/> earnings ledger.
///
/// <para>Deliberately independent of <see cref="Payout"/> / <see cref="SettlementBatch"/>. Those
/// model a bank-export batch lifecycle with approvals; a teacher settlement is a simpler running
/// account — what was earned in a period, what has been paid against it, and what is still owed.
/// Keeping them separate means part-payments can be recorded against a period without dragging a
/// payout batch through an approval cycle it does not need.</para>
///
/// <para><b>Earnings are snapshotted, not recomputed.</b> <see cref="TotalPayable"/> is frozen when
/// the settlement is generated so that a later change to a sharing rule — or a refund on an old
/// order — cannot silently restate a period that has already been part-paid. Regenerating a
/// settlement is an explicit action.</para>
/// </summary>
public class TeacherSettlement : BaseEntity
{
    /// <summary>Human-readable reference — format <c>RIO-TS-YYYYMM-NNNN</c>.</summary>
    public string SettlementNumber { get; set; } = string.Empty;

    public Guid FacultyId { get; set; }
    public Faculty? Faculty { get; set; }

    /// <summary>Snapshot of the faculty name at generation, so a rename does not rewrite history.</summary>
    public string FacultyName { get; set; } = string.Empty;

    // ── Period (inclusive start, exclusive end — matches how the earnings query slices) ──
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }

    /// <summary>Gross sales value of the products that generated this faculty's share in the
    /// period — the "Total Sales" column of the report. Context for the payable, not a component
    /// of it.</summary>
    public decimal TotalSales { get; set; }

    /// <summary>Units sold across the period, summed from the earning lines.</summary>
    public int TotalQuantity { get; set; }

    /// <summary>The bare share — taxable value of the faculty's own supply, excluding GST. This is
    /// the institute's cost and the figure that belongs in a revenue-cost total.</summary>
    public decimal ShareAmount { get; set; }

    /// <summary>GST the faculty additionally invoices; zero when they are not registered.
    /// Recoverable as input tax credit, so it is tracked apart from the share.</summary>
    public decimal GstOnShare { get; set; }

    /// <summary>The cash owed for the period = <see cref="ShareAmount"/> + <see cref="GstOnShare"/>
    /// − <see cref="TdsDeduction"/>.</summary>
    public decimal TotalPayable { get; set; }

    /// <summary>TDS withheld (194J). Deducted from the payable, never from the share itself.</summary>
    public decimal TdsDeduction { get; set; }

    /// <summary>Manual correction applied to this period, signed. Bonuses positive, deductions
    /// negative; already reflected in <see cref="TotalPayable"/>.</summary>
    public decimal Adjustments { get; set; }

    public decimal AmountPaid { get; set; }

    /// <summary>Stored rather than computed so the report can sort and filter on it in SQL.
    /// Kept equal to <see cref="TotalPayable"/> − <see cref="AmountPaid"/> by the service.</summary>
    public decimal BalancePayable { get; set; }

    public TeacherSettlementStatus Status { get; set; } = TeacherSettlementStatus.Pending;

    public DateTime? SettledOn { get; set; }
    public string? PaymentReference { get; set; }
    public Enums.PaymentMode? PaidVia { get; set; }
    public string? Notes { get; set; }

    public Guid? CreatedById { get; set; }
    public string CreatedByName { get; set; } = "system";

    public ICollection<TeacherSettlementItem> Items { get; set; } = new List<TeacherSettlementItem>();
    public ICollection<TeacherSettlementPayment> Payments { get; set; } = new List<TeacherSettlementPayment>();
}

/// <summary>
/// The per-product breakdown behind a settlement — the report's "Product/Course-wise Sales" rows.
/// Each line rolls up every <see cref="FacultyShareEntry"/> in the period for one product, so the
/// settlement stays auditable back to the individual orders that earned it.
/// </summary>
public class TeacherSettlementItem : BaseEntity
{
    public Guid SettlementId { get; set; }
    public TeacherSettlement Settlement { get; set; } = null!;

    public Guid ProductId { get; set; }
    public string ProductTitle { get; set; } = string.Empty;

    public Guid? SubjectId { get; set; }
    public string? SubjectName { get; set; }

    /// <summary>Units sold of this product in the period.</summary>
    public int Quantity { get; set; }

    /// <summary>Number of distinct orders contributing to this line.</summary>
    public int OrderCount { get; set; }

    /// <summary>Gross sales value of this product in the period.</summary>
    public decimal GrossSales { get; set; }

    /// <summary>Pre-GST value the share was computed on.</summary>
    public decimal TaxableBase { get; set; }

    /// <summary>The rule in force, snapshotted — "20%" or "₹150/unit" on the report.</summary>
    public SharingType ShareType { get; set; }
    public decimal ShareValue { get; set; }

    public decimal ShareAmount { get; set; }
    public decimal GstOnShare { get; set; }
    public decimal TotalPayout { get; set; }
}

/// <summary>
/// One payment made against a settlement. A period is often cleared in parts, and
/// <c>AmountPaid</c> alone cannot answer "when, and by what reference" — this table can.
/// </summary>
public class TeacherSettlementPayment : BaseEntity
{
    public Guid SettlementId { get; set; }
    public TeacherSettlement Settlement { get; set; } = null!;

    public decimal Amount { get; set; }
    public DateTime PaidOnUtc { get; set; } = DateTime.UtcNow;
    public Enums.PaymentMode? PaidVia { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }

    public Guid? RecordedById { get; set; }
    public string RecordedByName { get; set; } = "system";
}

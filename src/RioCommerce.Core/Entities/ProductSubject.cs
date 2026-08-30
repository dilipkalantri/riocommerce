namespace RioCommerce.Core.Entities;

/// <summary>
/// One subject a product covers. A combo course teaches several — "CA Inter Audit &amp; Costing"
/// is genuinely both — and a single column could only ever record one of them.
///
/// <para>Deliberately mirrors <see cref="ProductFaculty"/>, down to <see cref="IsPrimary"/>:
/// <c>Product.SubjectId</c> stays the primary subject and keeps driving everything that was already
/// reading it (the storefront card, settlement and re-invoice totals), while this table carries the
/// full set for browsing and filtering. A product taught under one subject therefore behaves exactly
/// as it did before — it simply has one row here instead of none.</para>
/// </summary>
public class ProductSubject : BaseEntity
{
    public Guid ProductId { get; set; }
    public Guid SubjectId { get; set; }

    /// <summary>True on the row matching <c>Product.SubjectId</c>. Exactly one per product.</summary>
    public bool IsPrimary { get; set; }

    public Product Product { get; set; } = null!;
    public Subject Subject { get; set; } = null!;
}

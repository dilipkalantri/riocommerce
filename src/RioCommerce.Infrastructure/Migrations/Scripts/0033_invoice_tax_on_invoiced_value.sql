-- 0033 — Invoice tax split stated on the INVOICED value, not the student-facing gross.
--
-- Defect this repairs
-- -------------------
-- InvoiceService wrote TaxableAmount and the CGST/SGST/IGST split from the ORDER (the gross a
-- student would pay) while writing TotalAmount as the NET a franchisee is actually billed. The
-- stored document therefore did not add up. On HJC-INV-202608-0002 (order FRN-1006):
--
--     taxable 100.00 + CGST 9.00 + SGST 9.00 = 118.00   against a stated total of  98.00
--
-- A second, unrelated group is caught by the same test: admin-created orders record only a GST
-- TOTAL with no CGST/SGST/IGST components, so their invoices stored all three as zero and the
-- taxable value as the full total (1000.00 + 0 + 0 + 0 against a stated 1180.00).
--
-- Neither group was ever displayed wrongly — the invoice PDF and the GST report both recompute
-- tax on the invoiced net (InvoicePdfRenderer.Totals, GstRowCalculator). Only the persisted row
-- disagreed with them. This script brings the row into line with the two readers; it does not
-- restate any figure a franchisee or the department has already been shown.
--
-- The going-forward fix is in InvoiceService, which now derives the split via GstRowCalculator so
-- all three paths share one implementation. This script exists only for rows written before it.
--
-- Method — identical to GstRowCalculator.For:
--   gst     = round(order.GstAmount * invoice.TotalAmount / order.TotalAmount, 2)
--   igst    = gst           when the order used IGST (or is inter-state with no recorded split)
--   cgst    = round(gst/2)  otherwise, with the rounding paisa absorbed into SGST
--   taxable = invoice.TotalAmount - gst
--
-- Idempotent: the WHERE clause selects only rows whose components do not tie to their own total,
-- so a re-run after a partial apply matches nothing. Invoices with no surviving order are left
-- untouched — there is nothing left to scale against, and guessing would corrupt a filed return.

WITH corrected AS (
    SELECT
        i."Id",
        i."TotalAmount" AS invoiced,
        ROUND(o."GstAmount" * i."TotalAmount" / o."TotalAmount", 2) AS gst,
        (
            o."IgstAmount" > 0
            OR (
                o."CgstAmount" <= 0
                AND o."SgstAmount" <= 0
                AND COALESCE(NULLIF(TRIM(o."BillingState"), ''), 'Maharashtra') !~* '^maharashtra$'
            )
        ) AS use_igst
    FROM invoices i
    JOIN orders o ON o."Id" = i."OrderId"
    WHERE o."TotalAmount" > 0
      AND ROUND(i."TaxableAmount" + i."CgstAmount" + i."SgstAmount" + i."IgstAmount", 2)
          <> ROUND(i."TotalAmount", 2)
)
UPDATE invoices i
SET "TaxableAmount" = ROUND(c.invoiced - c.gst, 2),
    "CgstAmount"    = CASE WHEN c.use_igst THEN 0 ELSE ROUND(c.gst / 2, 2) END,
    "SgstAmount"    = CASE WHEN c.use_igst THEN 0 ELSE c.gst - ROUND(c.gst / 2, 2) END,
    "IgstAmount"    = CASE WHEN c.use_igst THEN c.gst ELSE 0 END,
    "UpdatedAt"     = NOW()
FROM corrected c
WHERE i."Id" = c."Id";

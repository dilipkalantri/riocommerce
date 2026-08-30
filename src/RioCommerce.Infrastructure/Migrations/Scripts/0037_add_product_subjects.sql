-- 0037 — a product can cover several subjects.
--
-- A combo course ("CA Inter Audit & Costing") genuinely teaches more than one subject, but
-- products."SubjectId" can only hold one, so the others were simply unrecorded and the course was
-- invisible when browsing by them. This adds the many-to-many alongside that column — it does NOT
-- replace it.
--
-- Backward-compatible and non-destructive:
--   * products."SubjectId" is untouched and remains the PRIMARY subject. Everything already reading
--     it — the storefront card, teacher settlement, franchise re-invoice — keeps the same value and
--     therefore the same numbers. Financial output cannot move.
--   * Every existing product with a subject is backfilled as exactly ONE row, IsPrimary = true, so a
--     single-subject product behaves precisely as it did before.
--   * Products with no subject get no row, which is the same "unassigned" state as today.
--   * IF NOT EXISTS / ON CONFLICT throughout — safe to re-run.
--   * ON DELETE CASCADE on ProductId only: deleting a product drops its links, while a Subject still
--     cannot be deleted out from under a product (RESTRICT), matching the existing FK on
--     products."SubjectId" that SubjectAdminService relies on.

CREATE TABLE IF NOT EXISTS "ProductSubjects" (
    "Id"        uuid PRIMARY KEY,
    "ProductId" uuid NOT NULL,
    "SubjectId" uuid NOT NULL,
    "IsPrimary" boolean NOT NULL DEFAULT false,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT "FK_ProductSubjects_products_ProductId"
        FOREIGN KEY ("ProductId") REFERENCES products ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ProductSubjects_Subjects_SubjectId"
        FOREIGN KEY ("SubjectId") REFERENCES "Subjects" ("Id") ON DELETE RESTRICT
);

-- One row per (product, subject): the pair IS the fact, so a duplicate is meaningless and a repeated
-- save must not be able to create one.
CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductSubjects_ProductId_SubjectId"
    ON "ProductSubjects" ("ProductId", "SubjectId");

-- Reverse lookup: "which products cover this subject" is the query every browse/filter screen runs.
CREATE INDEX IF NOT EXISTS "IX_ProductSubjects_SubjectId"
    ON "ProductSubjects" ("SubjectId");

-- ── Backfill ────────────────────────────────────────────────────────────────────────────────────
-- Every product that already has a subject gets that subject as its primary. Idempotent: the unique
-- index makes a second run a no-op, and the NOT EXISTS guard keeps it cheap.
INSERT INTO "ProductSubjects" ("Id", "ProductId", "SubjectId", "IsPrimary", "CreatedAt", "UpdatedAt")
SELECT gen_random_uuid(), p."Id", p."SubjectId", true, now(), now()
FROM products p
WHERE p."SubjectId" IS NOT NULL
  AND NOT EXISTS (
        SELECT 1 FROM "ProductSubjects" ps
        WHERE ps."ProductId" = p."Id" AND ps."SubjectId" = p."SubjectId"
  );

COMMENT ON TABLE "ProductSubjects" IS
    'Every subject a product covers. products."SubjectId" remains the primary and still drives the storefront card and all financial reporting; this table drives browsing, filtering and the cascade.';
COMMENT ON COLUMN "ProductSubjects"."IsPrimary" IS
    'True on the row mirroring products."SubjectId". Exactly one per product.';

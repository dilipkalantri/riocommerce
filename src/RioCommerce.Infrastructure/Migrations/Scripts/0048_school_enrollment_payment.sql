-- School enrolment → online payment → one invoice per student.
--
-- Three additive changes. Nothing is dropped except one index, which is REPLACED by a strictly
-- wider one in the same script, so no existing guarantee is lost at any point.
--
-- NOTE ON TABLE NAMES: this database mixes conventions and there are TWO invoice-ish tables.
-- EF maps the Invoice entity to the LOWERCASE public.invoices (44 columns, carries
-- IX_invoices_OrderId_Active) — see InvoiceConfiguration.ToTable("invoices"). The PascalCase
-- public."Invoices" is a 13-column legacy orphan that nothing reads; it is deliberately NOT
-- touched here. Targeting it would have left the real unique index in force and silently blocked
-- every invoice after the first.
--   lowercase/unquoted : users, orders, products, invoices
--   quoted PascalCase  : "Schools", "SchoolStudents", "Enrollments"

-- ── 1. Pending roster: which students an enrolment order covers ──────────────
-- The selected students used to survive only as free text inside an OrderNote, which cannot be
-- queried, authorised against, or invoiced from. This is the structural link.
--
-- Rows are written when the (unpaid) order is created and are the authority for what to confirm
-- and invoice once payment verifies. They are NOT access grants — the existing "Enrollments"
-- table keeps that job, and rows are added there only after a verified payment.
CREATE TABLE IF NOT EXISTS "SchoolEnrollmentStudents" (
    "Id"            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "OrderId"       uuid NOT NULL REFERENCES orders ("Id") ON DELETE CASCADE,
    "SchoolId"      uuid NOT NULL REFERENCES "Schools" ("Id") ON DELETE RESTRICT,
    "StudentUserId" uuid NOT NULL REFERENCES users ("Id") ON DELETE RESTRICT,
    "ProductId"     uuid NOT NULL REFERENCES products ("Id") ON DELETE RESTRICT,
    "UnitPrice"     numeric(18,2) NOT NULL,
    -- Set once the payment for this order verifies. Null = still pending payment.
    "ConfirmedAt"   timestamp with time zone,
    "InvoiceId"     uuid REFERENCES invoices ("Id") ON DELETE SET NULL,
    "CreatedAt"     timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAt"     timestamp with time zone NOT NULL DEFAULT now()
);

-- A student appears at most once per order: makes the roster idempotent under a double-submit
-- and guarantees exactly one invoice per student per order downstream.
CREATE UNIQUE INDEX IF NOT EXISTS "IX_SchoolEnrollmentStudents_Order_Student"
    ON "SchoolEnrollmentStudents" ("OrderId", "StudentUserId");
CREATE INDEX IF NOT EXISTS "IX_SchoolEnrollmentStudents_School"
    ON "SchoolEnrollmentStudents" ("SchoolId");
CREATE INDEX IF NOT EXISTS "IX_SchoolEnrollmentStudents_Student"
    ON "SchoolEnrollmentStudents" ("StudentUserId");

-- ── 2. The student an invoice is raised for ──────────────────────────────────
-- NULL on every existing and every ordinary invoice — those are billed to the order's buyer and
-- keep their current behaviour exactly. Only school-enrolment invoices carry a student.
ALTER TABLE invoices
    ADD COLUMN IF NOT EXISTS "StudentUserId" uuid;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_invoices_StudentUserId') THEN
        ALTER TABLE invoices
            ADD CONSTRAINT "FK_invoices_StudentUserId" FOREIGN KEY ("StudentUserId")
            REFERENCES users ("Id") ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_invoices_StudentUserId" ON invoices ("StudentUserId");

-- ── 3. Widen "one live invoice per order" to "one live invoice per order PER STUDENT" ──
--
-- The old index was UNIQUE ("OrderId") WHERE "Status" = 0. A school order covering five students
-- needs five live invoices against one OrderId, so it must go — but the guarantee it provided for
-- every OTHER order has to survive untouched.
--
-- COALESCE, not a plain two-column index: PostgreSQL treats NULLs as DISTINCT in a unique index,
-- so UNIQUE ("OrderId","StudentUserId") would happily allow two rows of ("X", NULL) and silently
-- drop the one-invoice-per-order rule for every normal order. Folding NULL onto a fixed sentinel
-- keeps those rows colliding exactly as before. (PG 15's NULLS NOT DISTINCT would express this
-- directly; this server is 14.24, so the expression index is the portable way.)
--
-- Net effect:
--   ordinary invoice (StudentUserId NULL) → at most ONE live invoice per order, as today
--   school invoice   (StudentUserId set)  → at most ONE live invoice per (order, student)
DROP INDEX IF EXISTS "IX_invoices_OrderId_Active";

CREATE UNIQUE INDEX IF NOT EXISTS "IX_invoices_Order_Student_Active"
    ON invoices ("OrderId", COALESCE("StudentUserId", '00000000-0000-0000-0000-000000000000'::uuid))
    WHERE "Status" = 0;

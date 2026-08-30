-- 0028_order_reporting_module.sql
--
-- Order Management & Reporting module. Adds only what the existing schema genuinely cannot
-- express; the eight reports otherwise read tables that already exist (orders, "OrderItems",
-- invoices, "Subjects", "ProductFaculty", "FacultyShareEntries", "FranchiseCommissionEntries",
-- shipments, order_installments).
--
-- Four changes:
--   1. orders."SourceNo"        — originating system's reference for the order
--   2. shipments                — line items + a constrained status, so dispatch is reportable
--   3. franchise_reinvoices     — the original-invoice → re-invoice relationship (all new)
--   4. teacher_settlements      — faculty settlement periods tracked apart from payouts (all new)
--
-- NAMING — this schema is historically mixed: some tables are snake_case (orders, invoices,
-- shipments) and some are PascalCase-quoted ("OrderItems", "Subjects"). New tables here follow the
-- snake_case convention used by every table that has an IEntityTypeConfiguration, while COLUMNS
-- stay PascalCase-quoted to match EF's default property mapping used everywhere else.


-- ── 1. Order source reference ───────────────────────────────────────────────────────────────
-- Free text: the format differs per source (a CaseHub case ID, a counter receipt number, a
-- franchisee's own reference) and none of them are ours to validate. Indexed because the order
-- list searches on it alongside order number, name, email and phone.
--
-- NOTE: the business meaning of "Source No." was still being confirmed with the client when this
-- was written. The column is deliberately source-agnostic so that if it turns out to be fed by a
-- CaseHub integration rather than typed in, only the population changes — not the schema.

ALTER TABLE public.orders
    ADD COLUMN IF NOT EXISTS "SourceNo" varchar(80);

CREATE INDEX IF NOT EXISTS "IX_orders_SourceNo"
    ON public.orders ("SourceNo") WHERE "SourceNo" IS NOT NULL;


-- ── 2. Shipments: line items + reportable status ────────────────────────────────────────────
-- Shipment.Status was free text. It is now a ShipmentStatus enum persisted BY NAME into the same
-- varchar(20) column, so no data conversion happens here — the existing values 'Pending',
-- 'Dispatched' and 'Delivered' already spell the enum members exactly. This normalises the few
-- rows that could have drifted (case, whitespace, NULL) so the enum parses cleanly on read.

UPDATE public.shipments SET "Status" = 'Pending'    WHERE "Status" IS NULL OR btrim("Status") = '';
UPDATE public.shipments SET "Status" = 'Pending'    WHERE lower(btrim("Status")) = 'pending'    AND "Status" <> 'Pending';
UPDATE public.shipments SET "Status" = 'Dispatched' WHERE lower(btrim("Status")) = 'dispatched' AND "Status" <> 'Dispatched';
UPDATE public.shipments SET "Status" = 'Delivered'  WHERE lower(btrim("Status")) = 'delivered'  AND "Status" <> 'Delivered';
UPDATE public.shipments SET "Status" = 'Cancelled'  WHERE lower(btrim("Status")) = 'cancelled'  AND "Status" <> 'Cancelled';

-- Anything still outside the four names would fail to parse into the enum and take the whole
-- shipping report down with it. Park it at Pending rather than guessing.
UPDATE public.shipments
   SET "Status" = 'Pending'
 WHERE "Status" NOT IN ('Pending', 'Dispatched', 'Delivered', 'Cancelled');

ALTER TABLE public.shipments ALTER COLUMN "Status" SET DEFAULT 'Pending';
ALTER TABLE public.shipments ALTER COLUMN "Status" SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_shipments_Status') THEN
        ALTER TABLE public.shipments
            ADD CONSTRAINT "CK_shipments_Status"
            CHECK ("Status" IN ('Pending', 'Dispatched', 'Delivered', 'Cancelled'));
    END IF;
END $$;

-- The shipping report filters on status and slices by dispatch date; neither was indexed.
CREATE INDEX IF NOT EXISTS "IX_shipments_Status"       ON public.shipments ("Status");
CREATE INDEX IF NOT EXISTS "IX_shipments_DispatchedAt" ON public.shipments ("DispatchedAt");

-- What was actually in the box. Without this the report can say an order shipped but not what or
-- how many, and a split consignment is unrepresentable.
CREATE TABLE IF NOT EXISTS public.shipment_items (
    "Id"           uuid         DEFAULT gen_random_uuid() NOT NULL,
    "ShipmentId"   uuid         NOT NULL,
    -- Nullable: a replacement copy need not correspond to an original sale line.
    "OrderItemId"  uuid,
    "ProductId"    uuid         NOT NULL,
    "ProductTitle" varchar(500) NOT NULL DEFAULT '',
    "Quantity"     integer      NOT NULL DEFAULT 1,
    "CreatedAt"    timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt"    timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "PK_shipment_items" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_shipment_items_shipments_ShipmentId"
        FOREIGN KEY ("ShipmentId") REFERENCES public.shipments("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_shipment_items_ShipmentId" ON public.shipment_items ("ShipmentId");
CREATE INDEX IF NOT EXISTS "IX_shipment_items_ProductId"  ON public.shipment_items ("ProductId");

-- Backfill lines for shipments that already left the building, from the order they belong to.
-- Historical dispatches would otherwise show a blank product column forever. Only touches
-- shipments that have no lines yet, so it is safe on re-run.
INSERT INTO public.shipment_items ("ShipmentId", "OrderItemId", "ProductId", "ProductTitle", "Quantity")
SELECT s."Id", oi."Id", oi."ProductId", oi."ProductTitle", oi."Quantity"
  FROM public.shipments s
  JOIN public."OrderItems" oi ON oi."OrderId" = s."OrderId"
 WHERE NOT EXISTS (SELECT 1 FROM public.shipment_items si WHERE si."ShipmentId" = s."Id");


-- ── 3. Franchisee re-invoices ───────────────────────────────────────────────────────────────
-- A tax invoice raised ON a franchisee against an invoice already issued to the end customer.
-- Every party and money field is a snapshot at issue, matching how public.invoices behaves: once
-- issued the row is immutable, and a mistake is corrected by cancelling and re-issuing.
--
-- Status is a plain integer (Draft = 0, Issued = 1, Cancelled = 2) rather than a Postgres enum,
-- following InvoiceStatus — see InvoiceConfiguration.

CREATE TABLE IF NOT EXISTS public.franchise_reinvoices (
    "Id"                     uuid          DEFAULT gen_random_uuid() NOT NULL,
    -- HJC-RINV-YYYYMM-NNNN. A separate series from the HJC-INV- customer numbers so the two
    -- documents can never be confused in a ledger.
    "ReInvoiceNumber"        varchar(50)   NOT NULL,
    "ReInvoiceDate"          date          NOT NULL DEFAULT CURRENT_DATE,

    -- Link back to the original customer invoice, plus snapshots so the report renders the pairing
    -- without a join and survives the original being cancelled.
    "OriginalInvoiceId"      uuid          NOT NULL,
    "OriginalInvoiceNumber"  varchar(50)   NOT NULL DEFAULT '',
    "OriginalInvoiceDate"    date          NOT NULL DEFAULT CURRENT_DATE,
    "OrderId"                uuid          NOT NULL,
    "OrderNumber"            varchar(50)   NOT NULL DEFAULT '',

    -- Franchisee being billed (snapshot).
    "FranchiseId"            uuid          NOT NULL,
    "FranchiseName"          varchar(200)  NOT NULL DEFAULT '',
    "FranchiseCode"          varchar(40),
    "FranchiseGstin"         varchar(20),
    "PlaceOfSupply"          varchar(100),

    -- Money snapshot. TotalGst is stored rather than derived so report totals cannot drift from
    -- the document they are summarising.
    "TaxableAmount"          numeric(12,2) NOT NULL DEFAULT 0,
    "CgstAmount"             numeric(12,2) NOT NULL DEFAULT 0,
    "SgstAmount"             numeric(12,2) NOT NULL DEFAULT 0,
    "IgstAmount"             numeric(12,2) NOT NULL DEFAULT 0,
    "TotalGst"               numeric(12,2) NOT NULL DEFAULT 0,
    "TotalAmount"            numeric(12,2) NOT NULL DEFAULT 0,
    "GstRate"                numeric(5,2)  NOT NULL DEFAULT 0,
    -- The commission the franchisee retained on the original sale — why the two documents differ.
    "FranchiseShareAmount"   numeric(12,2) NOT NULL DEFAULT 0,
    "Currency"               varchar(3)    NOT NULL DEFAULT 'INR',

    "Status"                 integer       NOT NULL DEFAULT 1,
    "CancelledAt"            timestamp with time zone,
    "CancelledReason"        varchar(500),
    "IssuedById"             uuid,
    "IssuedByName"           varchar(200),
    "Notes"                  varchar(1000),

    "CreatedAt"              timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt"              timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "PK_franchise_reinvoices" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_franchise_reinvoices_invoices_OriginalInvoiceId"
        FOREIGN KEY ("OriginalInvoiceId") REFERENCES public.invoices("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_franchise_reinvoices_Franchises_FranchiseId"
        FOREIGN KEY ("FranchiseId") REFERENCES public."Franchises"("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_franchise_reinvoices_ReInvoiceNumber"
    ON public.franchise_reinvoices ("ReInvoiceNumber");
CREATE INDEX IF NOT EXISTS "IX_franchise_reinvoices_OriginalInvoiceId"
    ON public.franchise_reinvoices ("OriginalInvoiceId");
CREATE INDEX IF NOT EXISTS "IX_franchise_reinvoices_FranchiseId"
    ON public.franchise_reinvoices ("FranchiseId");
CREATE INDEX IF NOT EXISTS "IX_franchise_reinvoices_ReInvoiceDate"
    ON public.franchise_reinvoices ("ReInvoiceDate");
CREATE INDEX IF NOT EXISTS "IX_franchise_reinvoices_OrderId"
    ON public.franchise_reinvoices ("OrderId");

-- One LIVE re-invoice per original invoice. Filtered on status so that cancelling one frees the
-- original to be re-invoiced, which is the whole correction path for this document type.
CREATE UNIQUE INDEX IF NOT EXISTS "IX_franchise_reinvoices_Original_Active"
    ON public.franchise_reinvoices ("OriginalInvoiceId") WHERE "Status" <> 2;

CREATE TABLE IF NOT EXISTS public.franchise_reinvoice_items (
    "Id"            uuid          DEFAULT gen_random_uuid() NOT NULL,
    "ReInvoiceId"   uuid          NOT NULL,
    "ProductId"     uuid          NOT NULL,
    "ProductTitle"  varchar(500)  NOT NULL DEFAULT '',
    -- Subject snapshotted so the report can filter by subject even after a product is
    -- re-categorised.
    "SubjectId"     uuid,
    "SubjectName"   varchar(200),
    "OrderItemId"   uuid,
    "Quantity"      integer       NOT NULL DEFAULT 1,
    "UnitPrice"     numeric(12,2) NOT NULL DEFAULT 0,
    "TaxableAmount" numeric(12,2) NOT NULL DEFAULT 0,
    "GstRate"       numeric(5,2)  NOT NULL DEFAULT 0,
    "CgstAmount"    numeric(12,2) NOT NULL DEFAULT 0,
    "SgstAmount"    numeric(12,2) NOT NULL DEFAULT 0,
    "IgstAmount"    numeric(12,2) NOT NULL DEFAULT 0,
    "GstAmount"     numeric(12,2) NOT NULL DEFAULT 0,
    "LineTotal"     numeric(12,2) NOT NULL DEFAULT 0,
    "CreatedAt"     timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt"     timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "PK_franchise_reinvoice_items" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_franchise_reinvoice_items_reinvoices_ReInvoiceId"
        FOREIGN KEY ("ReInvoiceId") REFERENCES public.franchise_reinvoices("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_franchise_reinvoice_items_ReInvoiceId"
    ON public.franchise_reinvoice_items ("ReInvoiceId");
CREATE INDEX IF NOT EXISTS "IX_franchise_reinvoice_items_ProductId"
    ON public.franchise_reinvoice_items ("ProductId");
CREATE INDEX IF NOT EXISTS "IX_franchise_reinvoice_items_SubjectId"
    ON public.franchise_reinvoice_items ("SubjectId");


-- ── 4. Teacher settlements ──────────────────────────────────────────────────────────────────
-- Deliberately independent of payouts / settlement_batches. Those model a bank-export batch with
-- an approval lifecycle; a teacher settlement is a running account — earned in a period, paid
-- against, balance outstanding. Keeping them apart lets a part-payment be recorded without
-- dragging a payout batch through an approval cycle it does not need.
--
-- TotalPayable is SNAPSHOTTED at generation. A later sharing-rule change or a refund on an old
-- order must not silently restate a period that has already been part-paid; regenerating is an
-- explicit action.

CREATE TABLE IF NOT EXISTS public.teacher_settlements (
    "Id"                uuid          DEFAULT gen_random_uuid() NOT NULL,
    "SettlementNumber"  varchar(50)   NOT NULL,
    "FacultyId"         uuid          NOT NULL,
    "FacultyName"       varchar(200)  NOT NULL DEFAULT '',

    -- Inclusive start, exclusive end — matches how the earnings query slices FacultyShareEntries.
    "PeriodStartUtc"    timestamp with time zone NOT NULL,
    "PeriodEndUtc"      timestamp with time zone NOT NULL,

    -- Context for the payable, not a component of it.
    "TotalSales"        numeric(14,2) NOT NULL DEFAULT 0,
    "TotalQuantity"     integer       NOT NULL DEFAULT 0,

    -- The bare share is the institute's cost; the GST on it is a pass-through recoverable as input
    -- tax credit. Never summed into one figure — see the faculty share specification.
    "ShareAmount"       numeric(12,2) NOT NULL DEFAULT 0,
    "GstOnShare"        numeric(12,2) NOT NULL DEFAULT 0,
    -- ShareAmount + GstOnShare - TdsDeduction (+ Adjustments, signed).
    "TotalPayable"      numeric(12,2) NOT NULL DEFAULT 0,
    -- TDS (194J) is withheld on the professional fee, not on the GST charged on it.
    "TdsDeduction"      numeric(12,2) NOT NULL DEFAULT 0,
    "Adjustments"       numeric(12,2) NOT NULL DEFAULT 0,
    "AmountPaid"        numeric(12,2) NOT NULL DEFAULT 0,
    -- Stored, not computed, so the report can sort and filter on it in SQL.
    "BalancePayable"    numeric(12,2) NOT NULL DEFAULT 0,

    -- Pending = 0, PartiallyPaid = 1, Paid = 2, Cancelled = 3.
    "Status"            integer       NOT NULL DEFAULT 0,
    "SettledOn"         timestamp with time zone,
    "PaymentReference"  varchar(120),
    "PaidVia"           public.payment_mode,
    "Notes"             varchar(1000),
    "CreatedById"       uuid,
    "CreatedByName"     varchar(200)  NOT NULL DEFAULT 'system',

    "CreatedAt"         timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt"         timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "PK_teacher_settlements" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_teacher_settlements_faculty_FacultyId"
        FOREIGN KEY ("FacultyId") REFERENCES public.faculty("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_teacher_settlements_SettlementNumber"
    ON public.teacher_settlements ("SettlementNumber");
CREATE INDEX IF NOT EXISTS "IX_teacher_settlements_FacultyId"
    ON public.teacher_settlements ("FacultyId");
CREATE INDEX IF NOT EXISTS "IX_teacher_settlements_Status"
    ON public.teacher_settlements ("Status");
CREATE INDEX IF NOT EXISTS "IX_teacher_settlements_Period"
    ON public.teacher_settlements ("PeriodStartUtc", "PeriodEndUtc");

-- One settlement per faculty per period. Regenerating updates the existing row; without this a
-- re-run would create a second row and double the payable.
CREATE UNIQUE INDEX IF NOT EXISTS "IX_teacher_settlements_Faculty_Period"
    ON public.teacher_settlements ("FacultyId", "PeriodStartUtc", "PeriodEndUtc");

-- The per-product breakdown — the report's "Product/Course-wise Sales" rows. Each line rolls up
-- every FacultyShareEntry in the period for one product, keeping the settlement auditable back to
-- the orders that earned it.
CREATE TABLE IF NOT EXISTS public.teacher_settlement_items (
    "Id"            uuid          DEFAULT gen_random_uuid() NOT NULL,
    "SettlementId"  uuid          NOT NULL,
    "ProductId"     uuid          NOT NULL,
    "ProductTitle"  varchar(500)  NOT NULL DEFAULT '',
    "SubjectId"     uuid,
    "SubjectName"   varchar(200),
    "Quantity"      integer       NOT NULL DEFAULT 0,
    "OrderCount"    integer       NOT NULL DEFAULT 0,
    "GrossSales"    numeric(14,2) NOT NULL DEFAULT 0,
    "TaxableBase"   numeric(14,2) NOT NULL DEFAULT 0,
    -- The rule in force, snapshotted — renders as "20%" or "₹150/unit" on the report.
    "ShareType"     public.sharing_type NOT NULL DEFAULT 'percentage',
    "ShareValue"    numeric(10,2) NOT NULL DEFAULT 0,
    "ShareAmount"   numeric(12,2) NOT NULL DEFAULT 0,
    "GstOnShare"    numeric(12,2) NOT NULL DEFAULT 0,
    "TotalPayout"   numeric(12,2) NOT NULL DEFAULT 0,
    "CreatedAt"     timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt"     timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "PK_teacher_settlement_items" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_teacher_settlement_items_settlements_SettlementId"
        FOREIGN KEY ("SettlementId") REFERENCES public.teacher_settlements("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_teacher_settlement_items_SettlementId"
    ON public.teacher_settlement_items ("SettlementId");
CREATE INDEX IF NOT EXISTS "IX_teacher_settlement_items_ProductId"
    ON public.teacher_settlement_items ("ProductId");
CREATE INDEX IF NOT EXISTS "IX_teacher_settlement_items_SubjectId"
    ON public.teacher_settlement_items ("SubjectId");

-- A period is usually cleared in parts. AmountPaid alone cannot answer "when, and against what
-- reference" — this can.
CREATE TABLE IF NOT EXISTS public.teacher_settlement_payments (
    "Id"             uuid          DEFAULT gen_random_uuid() NOT NULL,
    "SettlementId"   uuid          NOT NULL,
    "Amount"         numeric(12,2) NOT NULL DEFAULT 0,
    "PaidOnUtc"      timestamp with time zone DEFAULT now() NOT NULL,
    "PaidVia"        public.payment_mode,
    "Reference"      varchar(120),
    "Notes"          varchar(500),
    "RecordedById"   uuid,
    "RecordedByName" varchar(200)  NOT NULL DEFAULT 'system',
    "CreatedAt"      timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt"      timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "PK_teacher_settlement_payments" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_teacher_settlement_payments_settlements_SettlementId"
        FOREIGN KEY ("SettlementId") REFERENCES public.teacher_settlements("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_teacher_settlement_payments_SettlementId"
    ON public.teacher_settlement_payments ("SettlementId");


-- ── 5. Reporting access paths ───────────────────────────────────────────────────────────────
-- The reports slice orders and order items by date, product, subject and franchise far more often
-- than the transactional screens do. These cover the joins the eight reports actually make.

CREATE INDEX IF NOT EXISTS "IX_OrderItems_ProductId"        ON public."OrderItems" ("ProductId");
CREATE INDEX IF NOT EXISTS "IX_OrderItems_OrderId_Product"  ON public."OrderItems" ("OrderId", "ProductId");
CREATE INDEX IF NOT EXISTS "IX_orders_CreatedAt"            ON public.orders ("CreatedAt");
CREATE INDEX IF NOT EXISTS "IX_orders_Franchise_CreatedAt"  ON public.orders ("FranchiseId", "CreatedAt");
CREATE INDEX IF NOT EXISTS "IX_invoices_InvoiceDate"        ON public.invoices ("InvoiceDate");
CREATE INDEX IF NOT EXISTS "IX_products_SubjectId"          ON public.products ("SubjectId");

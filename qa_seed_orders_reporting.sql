-- ============================================================================
-- qa_seed_orders_reporting.sql
--
-- Seeds 19 realistic orders covering every order path, so the owner can verify
-- the new Reporting module (commit 44527d2) against known numbers.
--
--   7 × Website — customer self-purchase
--   4 × Counter — counsellor at the admin desk
--   4 × Franchisee WITH GSTIN
--   4 × Franchisee WITHOUT GSTIN
--
-- Covers ALL EIGHT reports in the sidebar. What feeds each:
--
--   Sales              orders + "OrderItems"
--   GST                invoices                          (16 rows)
--   Faculty-Wise       "FacultyShareEntries"
--   Franchisee-Wise    orders + "FranchiseCommissionEntries"
--   Product / Subject  orders + "OrderItems" -> products.SubjectId
--   Re-Invoices        franchise_reinvoices + items      (4 rows)
--   Shipping           shipments + shipment_items        (6 consignments)
--   Teacher Settlement "FacultyShareEntries" as UNSETTLED earnings; the
--                      settled half is optional block 1b - read its warning.
--
-- Also writes refunds, so the GST report's refund column and Net GST line have
-- something to show.
--
-- ============================================================================
-- NUMBERING - THIS SCRIPT WRITES INTO THE LIVE SEQUENCES
-- ============================================================================
-- Every document number continues the platform's own series, read from the
-- database at seed time. Nothing is hardcoded and nothing is reserved:
--
--   Website + Counter orders   HJC-{n}                   floor 1043
--                              CheckoutService.GenerateOrderNumberAsync and
--                              OrderAdminService.GenerateOrderNumberAsync -
--                              ONE shared series; both take the newest order
--                              starting "HJC" and increment the last segment.
--   Franchisee portal orders   FRN-{n}                   floor 1004
--                              FranchisePortalService, max numeric suffix + 1.
--   Invoices                   HJC-INV-{yyyyMM}-{NNNN}   0001 per month
--   Re-invoices                HJC-RINV-{yyyyMM}-{NNNN}  0001 per current month
--   Settlements                HJC-TS-{yyyyMM}-{NNNN}    0001 per current month
--
-- Numbers are assigned as max+1, max+2, ... so the app's next generated number
-- follows on cleanly whichever row it happens to read as "last".
--
-- CONSEQUENCES YOU ARE ACCEPTING BY USING REAL NUMBERS:
--
--   a) These orders are INDISTINGUISHABLE from real ones in the admin UI.
--      There is no QA- prefix to spot them by any more. The only handles are
--      orders."InternalNotes" (carries the marker below) and orders."SourceNo"
--      (every seeded row's reference contains "QA"). Both are preserved so the
--      rollback in section 3 stays exact - it deletes by marker, not by number.
--
--   b) The live sequence advances by 11 (HJC) and 8 (FRN). Real orders placed
--      after this will simply carry on from there. Rolling back does NOT give
--      those numbers back - you will have a gap, exactly as a cancelled order
--      would leave. That is cosmetic, not a data problem.
--
--   c) orders."OrderNumber" is UNIQUE-indexed. If a real order takes one of
--      these numbers between the read and the write, this transaction fails
--      and rolls back whole - it cannot half-apply or duplicate. Still, run it
--      in a quiet window rather than mid-day.
--
--   d) Invoice numbers land inside the real monthly GST series. Two seeded
--      invoices fall in 202607 and fourteen in 202608. If July has already
--      been filed, adding invoices to it after the fact is a filing question,
--      not just a data one - see note 4 below if that matters to you.
--
-- ROLLBACK MARKER: every seeded order carries this exact string in
-- "InternalNotes", and section 3 keys off it:
--     [QA-REPORTS-2026-08-12]
--
-- ── OTHER THINGS TO KNOW ────────────────────────────────────────────────────
-- 1. This writes rows directly. It does NOT run the app's side effects: no
--    Superclass/serial-key provisioning, no payment gateway record, no invoice
--    PDF, no notification email/SMS, no wallet debit, no audit log. Intentional
--    for report testing. To exercise those, place one order through the UI.
--
-- 2. Prices are multiples of 118 so the 18% extraction is exact to the paise
--    (11,800 -> 10,000 + 1,800) and report totals can be checked by eye. They
--    do NOT match catalogue prices - the seeded line price overrides whatever
--    the product is listed at.
--
-- 3. All franchise ORDERS bill to Maharashtra (intra-state) regardless of the
--    franchisee's real state, so the tax split stays predictable. The
--    RE-INVOICES are different on purpose: they take their place of supply
--    from the franchisee's own State column, because that is what
--    FranchiseReInvoiceService does - the institute is billing the franchisee,
--    not the student. So a re-invoice for an out-of-state franchisee will
--    correctly show IGST where the underlying order showed CGST+SGST.
--
-- 4. Dates run 28 Jul - 12 Aug 2026 IST, inside the reports' default 30-day
--    window. If you would rather keep every seeded invoice inside the current
--    GST month, shift the two July orders (refs QA-9001, QA-9002) forward to
--    August in the qa_ord block below before running.
--
-- Deliberate edge cases, so the filters are actually exercised:
--   ref QA-9003  two different products on one order -> line apportionment
--   ref QA-9005  payment still pending               -> payment-status filter
--   ref QA-9006  cancelled                           -> must NOT count as revenue
--   ref QA-9007  refunded + succeeded refund row     -> GST refund column, Net GST
--   ref QA-9015  franchise order paid FROM WALLET    -> no invoice is raised
--   ref QA-9016  stamped 01:30 IST                   -> IST-vs-UTC day boundary
--   refs 9002/9004/9011  Karnataka, Gujarat, Rajasthan -> IGST not CGST+SGST
--
-- "ref" values below are AUTHORING KEYS ONLY - internal handles used to wire
-- lines to orders inside this script. They are never written to the database.
-- The real order number is assigned at seed time and printed by the RAISE
-- NOTICE at the end.
-- ============================================================================


-- ────────────────────────────────────────────────────────────────────────────
-- 0. PRE-FLIGHT - read-only. Run these first and sanity-check the results.
-- ────────────────────────────────────────────────────────────────────────────

-- 0a. Where each live sequence currently stands. These are the numbers the
--     seed will continue from.
SELECT 'HJC- orders (website + counter)' AS series,
       COALESCE(MAX(NULLIF(regexp_replace("OrderNumber", '^.*-', ''), '')::bigint), 1042) AS current_max,
       'next: HJC-' || (COALESCE(MAX(NULLIF(regexp_replace("OrderNumber", '^.*-', ''), '')::bigint), 1042) + 1) AS next_number
  FROM public.orders
 WHERE "OrderNumber" LIKE 'HJC%' AND regexp_replace("OrderNumber", '^.*-', '') ~ '^[0-9]+$'
UNION ALL
SELECT 'FRN- orders (franchisee portal)',
       COALESCE(MAX(NULLIF(regexp_replace("OrderNumber", '^.*-', ''), '')::bigint), 1003),
       'next: FRN-' || (COALESCE(MAX(NULLIF(regexp_replace("OrderNumber", '^.*-', ''), '')::bigint), 1003) + 1)
  FROM public.orders
 WHERE "OrderNumber" LIKE 'FRN-%' AND regexp_replace("OrderNumber", '^.*-', '') ~ '^[0-9]+$';

-- 0b. Invoice series per month that this seed will touch.
SELECT substring("InvoiceNumber" from 9 for 6) AS month,
       count(*)                                AS issued,
       max(substring("InvoiceNumber" from 16)) AS highest_suffix
  FROM public.invoices
 WHERE "InvoiceNumber" LIKE 'HJC-INV-2026%'
 GROUP BY 1 ORDER BY 1;

-- 0c. Franchisees. The script wants TWO with a GSTIN and TWO without; if only
--     one of a kind exists it reuses it for both slots.
SELECT "Code", "Name", "Gstin", "State", "IsActive", "Status",
       CASE WHEN "Gstin" IS NOT NULL AND btrim("Gstin") <> '' THEN 'REGISTERED'
            ELSE 'UNREGISTERED' END AS kind
  FROM public."Franchises"
 WHERE "IsActive" AND "Status" = 'approved'
 ORDER BY kind, "Code"
 LIMIT 30;

-- 0d. Products that light up the most report columns: has a Subject
--     (Product/Subject report) and attached faculty (Faculty-Wise report).
--     The script needs SIX.
SELECT p."Id", p."Title", p."Sku", s."Name" AS subject,
       (SELECT count(*) FROM public."ProductFaculty" pf WHERE pf."ProductId" = p."Id") AS faculty_count
  FROM public.products p
  LEFT JOIN public."Subjects" s ON s."Id" = p."SubjectId"
 WHERE p."Status" = 'active' AND p."SubjectId" IS NOT NULL
 ORDER BY faculty_count DESC, p."Title"
 LIMIT 20;

-- 0e. The counsellor to stamp as the counter orders' creator.
SELECT u."Id", u."Email", u."FullName"
  FROM public.users u
  JOIN public."UserRoles" ur ON ur."UserId" = u."Id"
 WHERE u."IsActive" ORDER BY u."CreatedAt" LIMIT 10;

-- 0f. Confirm nothing is already seeded.
SELECT "OrderNumber", "SourceNo", "CreatedAt"
  FROM public.orders WHERE "InternalNotes" LIKE '%[QA-REPORTS-2026-08-12]%';


-- ────────────────────────────────────────────────────────────────────────────
-- 1. SEED - one transaction. Rolls back entirely on any error.
-- ────────────────────────────────────────────────────────────────────────────

BEGIN;

-- Order-level spec. "ref" is an authoring key, never stored. "order_no" is
-- filled in by the DO block with the real platform number.
-- All money is DERIVED from the lines below, so nothing can drift: subtotal =
-- SUM(unit x qty), total = subtotal - discount, GST is the 18% extraction from
-- total, and the franchise split follows the share spec.
CREATE TEMP TABLE qa_ord (
    ref           text PRIMARY KEY,
    order_no      text,                   -- assigned at seed time
    order_id      uuid NOT NULL DEFAULT gen_random_uuid(),
    source        text NOT NULL,          -- website | counter | franchisee
    fr_slot       int,                    -- 1,2 = GSTIN franchisees; 3,4 = non-GSTIN
    share_pct     numeric(6,2) NOT NULL DEFAULT 0,
    student       text NOT NULL,
    phone         text NOT NULL,
    email         text,
    city          text,
    bill_state    text NOT NULL,
    gst_class     text NOT NULL,
    cust_type     text NOT NULL,          -- individual | organization
    org_name      text,
    cust_gstin    text,
    status        text NOT NULL,
    pay_status    text NOT NULL,
    pay_mode      text,
    gateway_mode  text,
    discount      numeric(12,2) NOT NULL DEFAULT 0,
    ist_ts        timestamptz NOT NULL,
    source_no     text,
    from_wallet   boolean NOT NULL DEFAULT false,
    make_invoice  boolean NOT NULL DEFAULT true,
    refund_amount numeric(12,2) NOT NULL DEFAULT 0
) ON COMMIT DROP;

-- One row per order line. prod_slot 1..6 maps to the six products resolved below.
CREATE TEMP TABLE qa_line (
    ref        text NOT NULL,
    prod_slot  int  NOT NULL,
    qty        int  NOT NULL,
    unit_price numeric(12,2) NOT NULL
) ON COMMIT DROP;


-- ══ WEBSITE - customer self-purchase ════════════════════════════════════════
INSERT INTO qa_ord (ref,source,student,phone,email,city,bill_state,gst_class,cust_type,
                    org_name,cust_gstin,status,pay_status,pay_mode,gateway_mode,discount,
                    ist_ts,source_no,make_invoice,refund_amount) VALUES
('QA-9001','website','Rohit Deshmukh','9822011001','rohit.deshmukh.qa@example.com','Pune',
 'Maharashtra','B2C','individual',NULL,NULL,'confirmed','success','razorpay','UPI',0,
 TIMESTAMPTZ '2026-07-28 14:20:00+05:30','CASE-QA-77101',true,0),

-- Organisation buyer, Karnataka -> IGST. Coupon discount at order level.
('QA-9002','website','Meera Iyer','9845022002','accounts.qa@vidyasetu.example','Bengaluru',
 'Karnataka','B2B','organization','Vidyasetu Learning LLP','29AABCV1234M1ZP',
 'confirmed','success','razorpay','Net Banking',2360.00,
 TIMESTAMPTZ '2026-07-30 11:05:00+05:30','CASE-QA-77102',true,0),

-- TWO different products on one order - the line-apportionment test.
('QA-9003','website','Kunal Bhosale','9823033003','kunal.bhosale.qa@example.com','Thane',
 'Maharashtra','B2C','individual',NULL,NULL,'confirmed','success','easebuzz','Debit Card',0,
 TIMESTAMPTZ '2026-08-01 09:15:00+05:30','CASE-QA-77103',true,0),

('QA-9004','website','Nisha Patel','9924044004','nisha.patel.qa@example.com','Surat',
 'Gujarat','B2C','individual',NULL,NULL,'confirmed','success','razorpay','Credit Card',0,
 TIMESTAMPTZ '2026-08-02 16:45:00+05:30','CASE-QA-77104',true,0),

-- Payment still pending: excluded by a payment-status filter, and no invoice.
('QA-9005','website','Aarav Joshi','9860055005','aarav.joshi.qa@example.com','Pune',
 'Maharashtra','B2C','individual',NULL,NULL,'pending','pending','razorpay',NULL,0,
 TIMESTAMPTZ '2026-08-03 20:10:00+05:30','CASE-QA-77105',false,0),

-- Cancelled: must NOT appear as revenue by default.
('QA-9006','website','Tanvi Rao','9700066006','tanvi.rao.qa@example.com','Mumbai',
 'Maharashtra','B2C','individual',NULL,NULL,'cancelled','failed','razorpay',NULL,0,
 TIMESTAMPTZ '2026-08-04 12:00:00+05:30','CASE-QA-77106',false,0),

-- Refunded in full: invoice exists, refund row reverses the tax.
('QA-9007','website','Imran Shaikh','9970077007','imran.shaikh.qa@example.com','Aurangabad',
 'Maharashtra','B2C','individual',NULL,NULL,'refunded','refunded','razorpay','UPI',0,
 TIMESTAMPTZ '2026-08-05 15:30:00+05:30','CASE-QA-77107',true,14160.00);

-- ══ COUNTER - counsellor places the order at the admin desk ═════════════════
INSERT INTO qa_ord (ref,source,student,phone,email,city,bill_state,gst_class,cust_type,
                    org_name,cust_gstin,status,pay_status,pay_mode,gateway_mode,discount,
                    ist_ts,source_no,make_invoice,refund_amount) VALUES
('QA-9008','counter','Sanika Kulkarni','9890088008','sanika.k.qa@example.com','Nashik',
 'Maharashtra','B2C','individual',NULL,NULL,'confirmed','success','cash',NULL,0,
 TIMESTAMPTZ '2026-08-05 18:40:00+05:30','CTR-QA-4417',true,0),

('QA-9009','counter','Vivek Nair','9846099009','vivek.nair.qa@example.com','Kolhapur',
 'Maharashtra','B2C','individual',NULL,NULL,'confirmed','success','cheque',NULL,0,
 TIMESTAMPTZ '2026-08-06 13:25:00+05:30','CTR-QA-4418',true,0),

-- Organisation walk-in, Maharashtra GSTIN -> B2B but still intra-state.
('QA-9010','counter','Priya Menon','9820010010','finance.qa@arthashastra.example','Mumbai',
 'Maharashtra','B2B','organization','Arthashastra Tutorials Pvt Ltd','27AAECA9876P1ZK',
 'confirmed','success','bank_transfer',NULL,0,
 TIMESTAMPTZ '2026-08-07 10:50:00+05:30','CTR-QA-4419',true,0),

('QA-9011','counter','Harsh Agarwal','9414011011','harsh.agarwal.qa@example.com','Jaipur',
 'Rajasthan','B2C','individual',NULL,NULL,'confirmed','success','upi',NULL,0,
 TIMESTAMPTZ '2026-08-08 17:05:00+05:30','CTR-QA-4420',true,0);

-- ══ FRANCHISEE **WITH** GSTIN - commission carries GST ══════════════════════
INSERT INTO qa_ord (ref,source,fr_slot,share_pct,student,phone,email,city,bill_state,
                    gst_class,cust_type,org_name,cust_gstin,status,pay_status,pay_mode,
                    gateway_mode,discount,ist_ts,source_no,from_wallet,make_invoice,refund_amount) VALUES
('QA-9012','franchisee',1,20.00,'Aditya Rane','9823012012','aditya.rane.qa@example.com','Nagpur',
 'Maharashtra','B2B','individual',NULL,NULL,'confirmed','success','bank_transfer',NULL,0,
 TIMESTAMPTZ '2026-08-08 10:15:00+05:30','FRN-QA-5521',false,true,0),

('QA-9013','franchisee',1,15.00,'Shreya Kale','9767013013','shreya.kale.qa@example.com','Nagpur',
 'Maharashtra','B2B','individual',NULL,NULL,'confirmed','success','razorpay','UPI',0,
 TIMESTAMPTZ '2026-08-09 11:30:00+05:30','FRN-QA-5522',false,true,0),

('QA-9014','franchisee',2,25.00,'Omkar Jadhav','9922014014','omkar.jadhav.qa@example.com','Solapur',
 'Maharashtra','B2B','individual',NULL,NULL,'confirmed','success','bank_transfer',NULL,0,
 TIMESTAMPTZ '2026-08-10 14:00:00+05:30','FRN-QA-5523',false,true,0),

-- Paid FROM WALLET: no tax invoice - the top-up that funded it already carried one.
('QA-9015','franchisee',2,20.00,'Ritika Sharma','9911015015','ritika.sharma.qa@example.com','Solapur',
 'Maharashtra','B2B','individual',NULL,NULL,'confirmed','success','bank_transfer',NULL,0,
 TIMESTAMPTZ '2026-08-10 16:20:00+05:30','FRN-QA-5524',true,false,0);

-- ══ FRANCHISEE **WITHOUT** GSTIN - commission only, no GST on it ════════════
INSERT INTO qa_ord (ref,source,fr_slot,share_pct,student,phone,email,city,bill_state,
                    gst_class,cust_type,org_name,cust_gstin,status,pay_status,pay_mode,
                    gateway_mode,discount,ist_ts,source_no,from_wallet,make_invoice,refund_amount) VALUES
-- 01:30 IST = 20:00 UTC the previous day. Proves the report's IST day bucketing.
('QA-9016','franchisee',3,20.00,'Pooja Shinde','9876016016','pooja.shinde.qa@example.com','Kolhapur',
 'Maharashtra','B2C','individual',NULL,NULL,'confirmed','success','bank_transfer',NULL,0,
 TIMESTAMPTZ '2026-08-12 01:30:00+05:30','FRN-QA-5525',false,true,0),

('QA-9017','franchisee',3,20.00,'Yash Kulkarni','9834017017','yash.kulkarni.qa@example.com','Satara',
 'Maharashtra','B2C','individual',NULL,NULL,'confirmed','success','upi',NULL,0,
 TIMESTAMPTZ '2026-08-11 12:45:00+05:30','FRN-QA-5526',false,true,0),

('QA-9018','franchisee',4,15.00,'Ananya Gokhale','9765018018','ananya.g.qa@example.com','Sangli',
 'Maharashtra','B2C','individual',NULL,NULL,'confirmed','success','bank_transfer',NULL,0,
 TIMESTAMPTZ '2026-08-11 19:00:00+05:30','FRN-QA-5527',false,true,0),

('QA-9019','franchisee',4,25.00,'Devang Mehta','9723019019','devang.mehta.qa@example.com','Sangli',
 'Maharashtra','B2C','individual',NULL,NULL,'confirmed','success','cash',NULL,0,
 TIMESTAMPTZ '2026-08-12 09:30:00+05:30','FRN-QA-5528',false,true,0);


-- ══ LINES ═══════════════════════════════════════════════════════════════════
-- Every unit price is a multiple of 118, so the 18% extraction is exact.
INSERT INTO qa_line (ref, prod_slot, qty, unit_price) VALUES
('QA-9001',1,1,11800.00),
('QA-9002',2,2, 5900.00),
('QA-9003',1,1, 5900.00),   -- two products, one order
('QA-9003',3,1, 4720.00),
('QA-9004',4,1, 4720.00),
('QA-9005',5,1, 8850.00),
('QA-9006',2,1, 3540.00),
('QA-9007',6,1,14160.00),
('QA-9008',3,1, 7080.00),
('QA-9009',4,2, 2360.00),
('QA-9010',6,1,17700.00),
('QA-9011',5,1, 5900.00),
('QA-9012',1,1,11800.00),
('QA-9013',2,2, 5900.00),
('QA-9014',3,1, 7080.00),
('QA-9015',4,1, 9440.00),
('QA-9016',1,1,11800.00),
('QA-9017',5,1, 5900.00),
('QA-9018',3,2, 4720.00),
('QA-9019',6,1, 3540.00);


DO $seed$
DECLARE
    v_marker  text := '[QA-REPORTS-2026-08-12] synthetic order for reporting verification';
    v_admin   uuid;
    v_n       int;
    v_hjc     bigint;
    v_frn     bigint;
    v_period  text := to_char(now() AT TIME ZONE 'UTC', 'YYYYMM');  -- matches DateTime.UtcNow
BEGIN
    IF EXISTS (SELECT 1 FROM public.orders WHERE "InternalNotes" LIKE '%[QA-REPORTS-2026-08-12]%') THEN
        RAISE EXCEPTION 'QA orders already present. Run section 3 (rollback) first.';
    END IF;

    -- ── Continue the live order-number series ───────────────────────────────
    -- Mirrors the app: any order number starting with the prefix, last dash
    -- segment parsed as an integer. Soft-deleted rows are included, matching
    -- OrderAdminService's IgnoreQueryFilters.
    SELECT COALESCE(MAX(NULLIF(regexp_replace("OrderNumber", '^.*-', ''), '')::bigint), 1042)
      INTO v_hjc FROM public.orders
     WHERE "OrderNumber" LIKE 'HJC%' AND regexp_replace("OrderNumber", '^.*-', '') ~ '^[0-9]+$';

    SELECT COALESCE(MAX(NULLIF(regexp_replace("OrderNumber", '^.*-', ''), '')::bigint), 1003)
      INTO v_frn FROM public.orders
     WHERE "OrderNumber" LIKE 'FRN-%' AND regexp_replace("OrderNumber", '^.*-', '') ~ '^[0-9]+$';

    -- Website and counter share the HJC series; numbers ascend with order time.
    UPDATE qa_ord o SET order_no = 'HJC-' || (v_hjc + r.rn)
      FROM (SELECT ref, row_number() OVER (ORDER BY ist_ts, ref) AS rn
              FROM qa_ord WHERE source IN ('website','counter')) r
     WHERE o.ref = r.ref;

    UPDATE qa_ord o SET order_no = 'FRN-' || (v_frn + r.rn)
      FROM (SELECT ref, row_number() OVER (ORDER BY ist_ts, ref) AS rn
              FROM qa_ord WHERE source = 'franchisee') r
     WHERE o.ref = r.ref;

    -- ── Resolve the real rows this data hangs off ───────────────────────────
    CREATE TEMP TABLE qa_fr (slot int PRIMARY KEY, franchise_id uuid, name text,
                             code text, gstin text, state text, registered boolean) ON COMMIT DROP;

    INSERT INTO qa_fr (slot, franchise_id, name, code, gstin, state, registered)
    SELECT rn, "Id", "Name", "Code", "Gstin", "State", true FROM (
        SELECT "Id","Name","Code","Gstin","State", row_number() OVER (ORDER BY "Code") AS rn
          FROM public."Franchises"
         WHERE "IsActive" AND "Status" = 'approved'
           AND "Gstin" IS NOT NULL AND btrim("Gstin") <> '') t
     WHERE rn <= 2;

    INSERT INTO qa_fr (slot, franchise_id, name, code, gstin, state, registered)
    SELECT rn + 2, "Id", "Name", "Code", "Gstin", "State", false FROM (
        SELECT "Id","Name","Code","Gstin","State", row_number() OVER (ORDER BY "Code") AS rn
          FROM public."Franchises"
         WHERE "IsActive" AND "Status" = 'approved'
           AND ("Gstin" IS NULL OR btrim("Gstin") = '')) t
     WHERE rn <= 2;

    -- Only one of a kind? Reuse it for the second slot rather than failing.
    INSERT INTO qa_fr SELECT 2, franchise_id, name, code, gstin, state, registered
      FROM qa_fr WHERE slot = 1 ON CONFLICT (slot) DO NOTHING;
    INSERT INTO qa_fr SELECT 4, franchise_id, name, code, gstin, state, registered
      FROM qa_fr WHERE slot = 3 ON CONFLICT (slot) DO NOTHING;

    IF NOT EXISTS (SELECT 1 FROM qa_fr WHERE slot = 1) THEN
        RAISE EXCEPTION 'No active franchisee WITH a GSTIN found - refs QA-9012..9015 need one.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM qa_fr WHERE slot = 3) THEN
        RAISE EXCEPTION 'No active franchisee WITHOUT a GSTIN found - refs QA-9016..9019 need one.';
    END IF;

    CREATE TEMP TABLE qa_prod (slot int PRIMARY KEY, product_id uuid, title text) ON COMMIT DROP;
    INSERT INTO qa_prod (slot, product_id, title)
    SELECT rn, "Id", "Title" FROM (
        SELECT p."Id", p."Title",
               row_number() OVER (
                   ORDER BY (SELECT count(*) FROM public."ProductFaculty" pf
                              WHERE pf."ProductId" = p."Id") DESC, p."Title") AS rn
          FROM public.products p
         WHERE p."Status" = 'active' AND p."SubjectId" IS NOT NULL) t
     WHERE rn <= 6;

    SELECT count(*) INTO v_n FROM qa_prod;
    IF v_n < 6 THEN RAISE EXCEPTION 'Need 6 active products with a Subject; found %.', v_n; END IF;

    SELECT u."Id" INTO v_admin
      FROM public.users u JOIN public."UserRoles" ur ON ur."UserId" = u."Id"
     WHERE u."IsActive" ORDER BY u."CreatedAt" LIMIT 1;

    -- ── Derived money, computed once and reused by every insert below ───────
    CREATE TEMP TABLE qa_money ON COMMIT DROP AS
    WITH totals AS (
        SELECT o.ref, sum(l.unit_price * l.qty) AS subtotal
          FROM qa_ord o JOIN qa_line l ON l.ref = o.ref
         GROUP BY o.ref
    ), t2 AS (
        SELECT o.*, t.subtotal,
               t.subtotal - o.discount AS total,
               -- Seller is in Maharashtra: same state -> CGST+SGST, else IGST.
               (o.bill_state ILIKE 'Maharashtra') AS intra
          FROM qa_ord o JOIN totals t ON t.ref = o.ref
    ), t3 AS (
        SELECT t2.*, round(t2.total * 18.0 / 118.0, 2) AS gst FROM t2
    ), t4 AS (
        SELECT t3.*,
               CASE WHEN intra THEN round(gst / 2, 2) ELSE 0 END       AS cgst,
               CASE WHEN intra THEN gst - round(gst / 2, 2) ELSE 0 END AS sgst,
               CASE WHEN intra THEN 0 ELSE gst END                     AS igst,
               total - gst                                             AS taxable
          FROM t3
    ), t5 AS (
        SELECT t4.*,
               f.franchise_id, f.name AS fr_name, f.code AS fr_code,
               f.gstin AS fr_gstin, f.state AS fr_state, f.registered,
               -- Franchise share spec: commission on the PRE-GST base; GST on
               -- the commission only when the franchisee holds a GSTIN.
               round(t4.taxable * t4.share_pct / 100.0, 2) AS commission
          FROM t4 LEFT JOIN qa_fr f ON f.slot = t4.fr_slot
    )
    SELECT t5.*,
           CASE WHEN registered THEN round(commission * 0.18, 2) ELSE 0 END AS comm_gst,
           commission + CASE WHEN registered THEN round(commission * 0.18, 2) ELSE 0 END
                                                                            AS fr_share,
           total - (commission + CASE WHEN registered THEN round(commission * 0.18, 2)
                                      ELSE 0 END)                           AS net_payable
      FROM t5;

    -- ── Orders ──────────────────────────────────────────────────────────────
    INSERT INTO public.orders (
        "Id","OrderNumber","StudentName","StudentPhone","StudentEmail","StudentCity",
        "Source","SourceNo","FranchiseId","CreatedById","Subtotal","DiscountAmount",
        "CouponCode","GstAmount","CgstAmount","SgstAmount","IgstAmount","TotalAmount",
        "FranchiseShareAmount","FranchiseCommissionBase","FranchiseCommissionGst",
        "FranchiseNetPayable","PaidFromWallet","BillingName","BillingAddress","BillingCity",
        "BillingState","BillingPincode","GstNumber","GstClassification","Status",
        "PaymentStatus","PaymentMode","GatewayPaymentMode","CustomerType","OrgName",
        "InternalNotes","ConfirmedAt","CancelledAt","CreatedAt","UpdatedAt"
    )
    SELECT m.order_id, m.order_no, m.student, m.phone, m.email, m.city,
           m.source::public.order_source, m.source_no, m.franchise_id,
           CASE WHEN m.source = 'counter' THEN v_admin END,
           m.subtotal, m.discount,
           CASE WHEN m.discount > 0 THEN 'QA-WELCOME20' END,
           m.gst, m.cgst, m.sgst, m.igst, m.total,
           COALESCE(m.fr_share, 0), COALESCE(m.commission, 0), COALESCE(m.comm_gst, 0),
           COALESCE(m.net_payable, m.total), m.from_wallet,
           COALESCE(m.org_name, m.student),
           COALESCE(m.city, '') || ' - QA seed address', m.city, m.bill_state, '400001',
           m.cust_gstin, m.gst_class,
           m.status::public.order_status, m.pay_status::public.payment_status,
           m.pay_mode::public.payment_mode, m.gateway_mode,
           m.cust_type::public.customer_type, m.org_name,
           v_marker,
           CASE WHEN m.status IN ('confirmed','refunded') THEN m.ist_ts END,
           CASE WHEN m.status = 'cancelled' THEN m.ist_ts + interval '2 hours' END,
           m.ist_ts, now()
      FROM qa_money m;

    -- ── Order items ─────────────────────────────────────────────────────────
    INSERT INTO public."OrderItems" (
        "OrderId","ProductId","ProductTitle","Quantity","UnitPrice","Discount",
        "GstRate","GstAmount","LineTotal","IsActivated","CreatedAt","UpdatedAt"
    )
    SELECT m.order_id, p.product_id, p.title, l.qty, l.unit_price, 0,
           18.00, round(l.unit_price * l.qty * 18.0 / 118.0, 2), l.unit_price * l.qty,
           (m.status IN ('confirmed','refunded')), m.ist_ts, now()
      FROM qa_line l
      JOIN qa_money m ON m.ref  = l.ref
      JOIN qa_prod  p ON p.slot = l.prod_slot;

    -- ── Invoices - what the GST report reads, keyed on invoice date ─────────
    -- Numbers continue the real per-month series: HJC-INV-yyyyMM-NNNN, max+1
    -- within each month the seed touches. Franchise invoices are raised for the
    -- NET the franchisee is billed, so the tax is scaled to that net exactly as
    -- GstRowCalculator does. Status 0 = Active.
    INSERT INTO public.invoices (
        "InvoiceNumber","InvoiceDate","OrderId","OrderNumber","CustomerName","CustomerEmail",
        "CustomerPhone","BillingAddress","BillingCity","BillingState","BillingPincode",
        "CustomerGstin","GstClassification","Subtotal","DiscountAmount","TaxableAmount",
        "CgstAmount","SgstAmount","IgstAmount","TotalAmount","GstRate","FranchiseShareAmount",
        "Status","PaymentMode","CreatedAt","UpdatedAt"
    )
    SELECT 'HJC-INV-' || d.ym || '-' ||
           lpad((d.month_max + row_number() OVER (PARTITION BY d.ym ORDER BY d.inv_date, m.order_no))::text, 4, '0'),
           d.inv_date,
           m.order_id, m.order_no,
           COALESCE(m.org_name, m.student), m.email, m.phone,
           COALESCE(m.city, '') || ' - QA seed address', m.city, m.bill_state, '400001',
           COALESCE(m.cust_gstin, m.fr_gstin), m.gst_class,
           m.subtotal, m.discount,
           inv.inv_total - inv.inv_gst,
           CASE WHEN m.intra THEN round(inv.inv_gst / 2, 2) ELSE 0 END,
           CASE WHEN m.intra THEN inv.inv_gst - round(inv.inv_gst / 2, 2) ELSE 0 END,
           CASE WHEN m.intra THEN 0 ELSE inv.inv_gst END,
           inv.inv_total, 18.00, COALESCE(m.fr_share, 0),
           0, m.pay_mode::public.payment_mode, now(), now()
      FROM qa_money m
      CROSS JOIN LATERAL (
          SELECT (m.ist_ts AT TIME ZONE 'Asia/Kolkata')::date AS inv_date
      ) dd
      CROSS JOIN LATERAL (
          SELECT dd.inv_date, to_char(dd.inv_date, 'YYYYMM') AS ym,
                 -- Highest suffix already issued in that month, 0 if none.
                 COALESCE((SELECT MAX(NULLIF(substring(i2."InvoiceNumber" from 16), '')::int)
                             FROM public.invoices i2
                            WHERE i2."InvoiceNumber" LIKE 'HJC-INV-' || to_char(dd.inv_date,'YYYYMM') || '-%'
                              AND substring(i2."InvoiceNumber" from 16) ~ '^[0-9]+$'), 0) AS month_max
      ) d
      CROSS JOIN LATERAL (
          SELECT COALESCE(m.net_payable, m.total) AS inv_total,
                 round(m.gst * COALESCE(m.net_payable, m.total) / m.total, 2) AS inv_gst
      ) inv
     WHERE m.make_invoice;

    -- ── Refunds - one order only. Status 1 = Succeeded. ─────────────────────
    INSERT INTO public.refunds (
        "OrderId","Amount","Reason","Status","RefundType","IsOffline","InitiatedByName",
        "InitiatedById","CreatedAt","UpdatedAt"
    )
    SELECT m.order_id, m.refund_amount, 'QA seed - full refund for report testing',
           1, 'Full', true, 'QA Seed', v_admin, m.ist_ts + interval '3 days', now()
      FROM qa_money m WHERE m.refund_amount > 0;

    -- ── Franchise commission ledger ─────────────────────────────────────────
    -- The Franchisee-Wise report sums "CommissionAmount" from here, NOT from
    -- orders."FranchiseShareAmount". CommissionAmount is the BARE commission;
    -- GST on it sits in its own column.
    -- Every franchise order in this set is deliberately SINGLE-LINE, so the
    -- order's commission maps 1:1 onto its item with no apportionment. If you
    -- add a multi-line franchise order, split these three amounts pro-rata by
    -- line gross and put the rounding remainder on the last line.
    INSERT INTO public."FranchiseCommissionEntries" (
        "FranchiseId","OrderId","OrderNumber","OrderItemId","ProductId","ProductTitle",
        "BaseAmount","Type","Value","CommissionAmount","GstOnCommission","TotalPayout",
        "EarnedAt","CreatedAt","UpdatedAt"
    )
    SELECT m.franchise_id, m.order_id, m.order_no, oi."Id", oi."ProductId", oi."ProductTitle",
           m.taxable, 'percent'::public.commission_type, m.share_pct,
           m.commission, m.comm_gst, m.fr_share, m.ist_ts, now(), now()
      FROM qa_money m
      JOIN public."OrderItems" oi ON oi."OrderId" = m.order_id
     WHERE m.franchise_id IS NOT NULL AND m.status IN ('confirmed','refunded');

    -- ── Faculty share ledger ────────────────────────────────────────────────
    -- Drives the Faculty-Wise report and is what Teacher Settlement rolls up.
    -- Flat 10% of each line's taxable base, per attached faculty.
    -- WasGstRegistered is snapshotted from the faculty row exactly as
    -- FacultySharingService does: GST on the share only for registered faculty.
    INSERT INTO public."FacultyShareEntries" (
        "FacultyId","OrderId","OrderNumber","OrderItemId","ProductId","ProductTitle",
        "BaseAmount","ShareType","ShareValue","ShareAmount","GstOnShare","TotalPayout",
        "WasGstRegistered","WasCapped","EarnedAt","CreatedAt","UpdatedAt"
    )
    SELECT pf."FacultyId", m.order_id, m.order_no, oi."Id", oi."ProductId", oi."ProductTitle",
           b.base, 'percentage'::public.sharing_type, 10.00, b.share,
           CASE WHEN b.reg THEN round(b.share * 0.18, 2) ELSE 0 END,
           b.share + CASE WHEN b.reg THEN round(b.share * 0.18, 2) ELSE 0 END,
           b.reg, false, m.ist_ts, now(), now()
      FROM qa_money m
      JOIN public."OrderItems" oi     ON oi."OrderId"   = m.order_id
      JOIN public."ProductFaculty" pf ON pf."ProductId" = oi."ProductId"
      JOIN public.faculty f           ON f."Id"         = pf."FacultyId"
      CROSS JOIN LATERAL (
          SELECT round(oi."LineTotal" * 100.0 / 118.0, 2)        AS base,
                 round(oi."LineTotal" * 100.0 / 118.0 * 0.10, 2) AS share,
                 (f."GstRegistered" OR COALESCE(btrim(f."Gstin"), '') <> '') AS reg
      ) b
     WHERE m.status IN ('confirmed','refunded');

    -- ── Shipments - three statuses so the filter is testable ────────────────
    -- "CreatedAt" is the order date + 1 day, NOT now(): the shipping report
    -- windows on (DispatchedAt ?? CreatedAt), so a Pending consignment is dated
    -- by its creation. Stamping now() would park every pending row on today.
    INSERT INTO public.shipments ("OrderId","Courier","TrackingNumber","Status",
                                  "DispatchedAt","DeliveredAt","Notes","CreatedAt","UpdatedAt")
    SELECT m.order_id, s.courier,
           'QA' || replace(m.order_no, '-', '') || s.suffix, s.status,
           CASE WHEN s.status IN ('Dispatched','Delivered') THEN m.ist_ts + interval '1 day' END,
           CASE WHEN s.status = 'Delivered' THEN m.ist_ts + interval '3 days' END,
           'QA seed', m.ist_ts + interval '1 day', now()
      FROM qa_money m
      JOIN (VALUES
              ('QA-9001','BlueDart',  '01','Delivered'),
              ('QA-9003','Delhivery', '03','Delivered'),
              ('QA-9008','DTDC',      '08','Dispatched'),
              ('QA-9010','BlueDart',  '10','Dispatched'),
              ('QA-9016','India Post','16','Pending'),
              ('QA-9018','Delhivery', '18','Pending')
           ) AS s(ref, courier, suffix, status) ON s.ref = m.ref;

    INSERT INTO public.shipment_items ("ShipmentId","OrderItemId","ProductId","ProductTitle","Quantity")
    SELECT s."Id", oi."Id", oi."ProductId", oi."ProductTitle", oi."Quantity"
      FROM public.shipments s
      JOIN qa_money m             ON m.order_id = s."OrderId"
      JOIN public."OrderItems" oi ON oi."OrderId" = s."OrderId";

    -- ── Franchisee re-invoices ──────────────────────────────────────────────
    -- The ONLY report with no other source of rows: it reads franchise_reinvoices
    -- exclusively, so without these the Re-Invoices page is empty.
    --
    -- Mirrors FranchiseReInvoiceService.CreateAsync exactly:
    --   total   = the original invoice's total (already net of the commission)
    --   taxable = the original invoice's taxable
    --   gst     = its CGST+SGST+IGST, re-split on the FRANCHISEE's place of
    --             supply (the institute is billing them, not the student)
    --   rate    = gst / taxable * 100
    --   share   = the commission the franchisee retained
    -- Number continues the real HJC-RINV-{current month}-NNNN series. The month
    -- comes from UtcNow, not the re-invoice date - that is what the service does.
    -- Status 1 = Issued. Raised for four of the seven franchise invoices, so the
    -- "Raise Re-Invoice" screen still has three pending candidates to list.
    INSERT INTO public.franchise_reinvoices (
        "ReInvoiceNumber","ReInvoiceDate","OriginalInvoiceId","OriginalInvoiceNumber",
        "OriginalInvoiceDate","OrderId","OrderNumber","FranchiseId","FranchiseName",
        "FranchiseCode","FranchiseGstin","PlaceOfSupply","TaxableAmount","CgstAmount",
        "SgstAmount","IgstAmount","TotalGst","TotalAmount","GstRate","FranchiseShareAmount",
        "Status","IssuedById","IssuedByName","Notes","CreatedAt","UpdatedAt"
    )
    SELECT 'HJC-RINV-' || v_period || '-' ||
           lpad((
               COALESCE((SELECT MAX(NULLIF(substring(r2."ReInvoiceNumber" from 17), '')::int)
                           FROM public.franchise_reinvoices r2
                          WHERE r2."ReInvoiceNumber" LIKE 'HJC-RINV-' || v_period || '-%'
                            AND substring(r2."ReInvoiceNumber" from 17) ~ '^[0-9]+$'), 0)
               + row_number() OVER (ORDER BY i."InvoiceDate", m.order_no)
           )::text, 4, '0'),
           -- Day after the original, but never in the future: the report's
           -- default window ends today.
           LEAST(i."InvoiceDate" + 1, CURRENT_DATE),
           i."Id", i."InvoiceNumber", i."InvoiceDate",
           m.order_id, m.order_no, m.franchise_id, m.fr_name, m.fr_code, m.fr_gstin,
           pos.place, t.taxable,
           CASE WHEN pos.intra THEN round(t.gst / 2, 2) ELSE 0 END,
           CASE WHEN pos.intra THEN t.gst - round(t.gst / 2, 2) ELSE 0 END,
           CASE WHEN pos.intra THEN 0 ELSE t.gst END,
           t.gst, i."TotalAmount",
           CASE WHEN t.taxable > 0 THEN round(t.gst / t.taxable * 100, 2) ELSE 0 END,
           COALESCE(m.fr_share, 0),
           1, v_admin, 'QA Seed', 'QA seed - raised for report testing', now(), now()
      FROM qa_money m
      JOIN public.invoices i ON i."OrderId" = m.order_id
      CROSS JOIN LATERAL (
          SELECT COALESCE(NULLIF(btrim(m.fr_state), ''), i."BillingState") AS place
      ) pos_raw
      CROSS JOIN LATERAL (
          SELECT pos_raw.place,
                 -- IsIntraState: blank counts as intra, else compare to seller's state.
                 (pos_raw.place IS NULL OR btrim(pos_raw.place) = ''
                  OR pos_raw.place ILIKE 'Maharashtra') AS intra
      ) pos
      CROSS JOIN LATERAL (
          SELECT i."TaxableAmount" AS taxable,
                 i."CgstAmount" + i."SgstAmount" + i."IgstAmount" AS gst
      ) t
     WHERE m.ref IN ('QA-9012','QA-9014','QA-9016','QA-9018');

    -- One line per order item, carrying the subject snapshot the report filters
    -- on. Single-line orders, so each line takes the whole header amount.
    INSERT INTO public.franchise_reinvoice_items (
        "ReInvoiceId","ProductId","ProductTitle","SubjectId","SubjectName","OrderItemId",
        "Quantity","UnitPrice","TaxableAmount","GstRate","CgstAmount","SgstAmount",
        "IgstAmount","GstAmount","LineTotal","CreatedAt","UpdatedAt"
    )
    SELECT r."Id", oi."ProductId", oi."ProductTitle", p."SubjectId", s."Name", oi."Id",
           oi."Quantity", oi."UnitPrice",
           r."TaxableAmount", r."GstRate", r."CgstAmount", r."SgstAmount", r."IgstAmount",
           r."TotalGst", r."TotalAmount", now(), now()
      FROM public.franchise_reinvoices r
      JOIN qa_money m               ON m.order_id  = r."OrderId"
      JOIN public."OrderItems" oi   ON oi."OrderId" = r."OrderId"
      JOIN public.products p        ON p."Id"       = oi."ProductId"
      LEFT JOIN public."Subjects" s ON s."Id"       = p."SubjectId";

    -- ── What was assigned ───────────────────────────────────────────────────
    SELECT count(*) INTO v_n FROM qa_money;
    RAISE NOTICE 'Seeded % orders.', v_n;
    RAISE NOTICE 'HJC series: % .. %  (website + counter, 11 orders)',
        (SELECT min(order_no) FROM qa_money WHERE source IN ('website','counter')),
        (SELECT max(order_no) FROM qa_money WHERE source IN ('website','counter'));
    RAISE NOTICE 'FRN series: % .. %  (franchisee, 8 orders)',
        (SELECT min(order_no) FROM qa_money WHERE source = 'franchisee'),
        (SELECT max(order_no) FROM qa_money WHERE source = 'franchisee');
    RAISE NOTICE 'Franchisees - GSTIN: % / %  |  non-GSTIN: % / %',
        (SELECT name FROM qa_fr WHERE slot=1), (SELECT name FROM qa_fr WHERE slot=2),
        (SELECT name FROM qa_fr WHERE slot=3), (SELECT name FROM qa_fr WHERE slot=4);
END
$seed$;

-- Keep this. It is the ref -> real number map, and the temp tables are gone
-- after COMMIT. Save the output somewhere before you close the session.
SELECT "OrderNumber", "SourceNo", "Source", "Status",
       (o."CreatedAt" AT TIME ZONE 'Asia/Kolkata')::date AS ist_date, "TotalAmount"
  FROM public.orders o
 WHERE "InternalNotes" LIKE '%[QA-REPORTS-2026-08-12]%'
 ORDER BY "Source", "OrderNumber";

COMMIT;


-- ────────────────────────────────────────────────────────────────────────────
-- 1b. OPTIONAL - a generated teacher settlement. NOT run by default.
--
-- You do NOT need this for the Teacher Settlement report to show data. That
-- report already lists unsettled earnings straight from FacultyShareEntries,
-- so the seed above fills it with rows reading "- not generated". This block
-- only exercises the SETTLED half: Payable / Paid / Balance and PartiallyPaid.
--
-- WARNING. TeacherSettlementReportBuilder treats an earning as settled when ANY
--   live settlement for that faculty spans its EarnedAt. A settlement covering
--   28 Jul - 12 Aug therefore also absorbs that faculty's REAL earnings in the
--   same window and removes them from the "not yet settled" figure until this
--   row is deleted. On production that changes what the owner sees for a real
--   faculty member.
--
--   Safer alternative: leave this commented and click Manage Settlements ->
--   Generate in the admin. Same effect, but a deliberate act with an audit
--   trail behind it.
-- ────────────────────────────────────────────────────────────────────────────
/*
BEGIN;

WITH target AS (
    SELECT e."FacultyId", f."DisplayName",
           sum(e."ShareAmount") AS share, sum(e."GstOnShare") AS gst,
           sum(e."TotalPayout") AS payout, sum(e."BaseAmount") AS base
      FROM public."FacultyShareEntries" e
      JOIN public.faculty f ON f."Id" = e."FacultyId"
     WHERE e."OrderId" IN (SELECT "Id" FROM public.orders
                            WHERE "InternalNotes" LIKE '%[QA-REPORTS-2026-08-12]%')
     GROUP BY e."FacultyId", f."DisplayName"
     ORDER BY payout DESC LIMIT 1
), num AS (
    SELECT 'HJC-TS-' || to_char(now() AT TIME ZONE 'UTC','YYYYMM') || '-' ||
           lpad((COALESCE((SELECT MAX(NULLIF(substring(s2."SettlementNumber" from 15), '')::int)
                             FROM public.teacher_settlements s2
                            WHERE s2."SettlementNumber" LIKE 'HJC-TS-'
                                  || to_char(now() AT TIME ZONE 'UTC','YYYYMM') || '-%'
                              AND substring(s2."SettlementNumber" from 15) ~ '^[0-9]+$'), 0)
                 + 1)::text, 4, '0') AS n
), ins AS (
    INSERT INTO public.teacher_settlements (
        "SettlementNumber","FacultyId","FacultyName","PeriodStartUtc","PeriodEndUtc",
        "TotalSales","TotalQuantity","ShareAmount","GstOnShare","TotalPayable",
        "TdsDeduction","Adjustments","AmountPaid","BalancePayable","Status",
        "PaymentReference","PaidVia","Notes","CreatedByName"
    )
    SELECT num.n, t."FacultyId", t."DisplayName",
           TIMESTAMPTZ '2026-07-28 00:00:00+05:30', TIMESTAMPTZ '2026-08-13 00:00:00+05:30',
           t.base, 0, t.share, t.gst,
           -- Payable = share + GST - TDS (10% of the bare share, 194J).
           round(t.share + t.gst - t.share * 0.10, 2),
           round(t.share * 0.10, 2), 0,
           -- Part-paid, so the report shows a live balance not a closed row.
           round((t.share + t.gst - t.share * 0.10) / 2, 2),
           round(t.share + t.gst - t.share * 0.10, 2)
             - round((t.share + t.gst - t.share * 0.10) / 2, 2),
           1, 'QA-UTR-778001', 'bank_transfer'::public.payment_mode,
           'QA seed - part payment', 'QA Seed'
      FROM target t CROSS JOIN num
    RETURNING "Id","FacultyId","AmountPaid"
)
INSERT INTO public.teacher_settlement_items (
    "SettlementId","ProductId","ProductTitle","SubjectId","SubjectName","Quantity",
    "OrderCount","GrossSales","TaxableBase","ShareType","ShareValue","ShareAmount",
    "GstOnShare","TotalPayout"
)
SELECT ins."Id", e."ProductId", e."ProductTitle", p."SubjectId", s."Name",
       sum(oi."Quantity"), count(DISTINCT e."OrderId"),
       sum(oi."LineTotal"), sum(e."BaseAmount"),
       'percentage'::public.sharing_type, 10.00,
       sum(e."ShareAmount"), sum(e."GstOnShare"), sum(e."TotalPayout")
  FROM ins
  JOIN public."FacultyShareEntries" e ON e."FacultyId" = ins."FacultyId"
   AND e."OrderId" IN (SELECT "Id" FROM public.orders
                        WHERE "InternalNotes" LIKE '%[QA-REPORTS-2026-08-12]%')
  JOIN public."OrderItems" oi   ON oi."Id" = e."OrderItemId"
  JOIN public.products p        ON p."Id"  = e."ProductId"
  LEFT JOIN public."Subjects" s ON s."Id"  = p."SubjectId"
 GROUP BY ins."Id", e."ProductId", e."ProductTitle", p."SubjectId", s."Name";

INSERT INTO public.teacher_settlement_payments (
    "SettlementId","Amount","PaidOnUtc","PaidVia","Reference","Notes","RecordedByName")
SELECT "Id", "AmountPaid", TIMESTAMPTZ '2026-08-12 11:00:00+05:30',
       'bank_transfer'::public.payment_mode, 'QA-UTR-778001', 'QA seed', 'QA Seed'
  FROM public.teacher_settlements WHERE "Notes" = 'QA seed - part payment';

COMMIT;
*/


-- ────────────────────────────────────────────────────────────────────────────
-- 2. VERIFY - expected values to check the report screens against.
--    Every query scopes on the InternalNotes marker, since the order numbers
--    are now real and carry no QA prefix.
-- ────────────────────────────────────────────────────────────────────────────

-- 2z. All eight reports should return rows. Anything reading 0 is a bug in that
--     report, not missing data.
WITH qa AS (SELECT "Id" FROM public.orders
             WHERE "InternalNotes" LIKE '%[QA-REPORTS-2026-08-12]%')
SELECT 'Sales / Product-Subject / Franchisee-Wise' AS report,
       (SELECT count(*) FROM public.orders o JOIN public."OrderItems" oi ON oi."OrderId" = o."Id"
         WHERE o."Id" IN (SELECT "Id" FROM qa)
           AND o."Status" NOT IN ('draft','cancelled','refunded'))            AS rows_expected
UNION ALL SELECT 'GST',
       (SELECT count(*) FROM public.invoices WHERE "OrderId" IN (SELECT "Id" FROM qa))
UNION ALL SELECT 'Faculty-Wise',
       (SELECT count(*) FROM public."FacultyShareEntries" WHERE "OrderId" IN (SELECT "Id" FROM qa))
UNION ALL SELECT 'Re-Invoices',
       (SELECT count(*) FROM public.franchise_reinvoice_items ri
          JOIN public.franchise_reinvoices r ON r."Id" = ri."ReInvoiceId"
         WHERE r."OrderId" IN (SELECT "Id" FROM qa))
UNION ALL SELECT 'Shipping',
       (SELECT count(*) FROM public.shipment_items si
          JOIN public.shipments s ON s."Id" = si."ShipmentId"
         WHERE s."OrderId" IN (SELECT "Id" FROM qa))
UNION ALL SELECT 'Teacher Settlement (unsettled earnings)',
       (SELECT count(*) FROM public."FacultyShareEntries" WHERE "OrderId" IN (SELECT "Id" FROM qa));

-- 2a. Per-order figures, mirroring OrderMoney.ForOrder: gross from the lines,
--     franchisee discount from the commission ledger, GST from the order.
SELECT o."OrderNumber", o."SourceNo", o."Source", o."Status",
       (o."CreatedAt" AT TIME ZONE 'Asia/Kolkata')::date            AS ist_date,
       sum((oi."UnitPrice" + oi."Discount") * oi."Quantity")        AS gross,
       o."DiscountAmount"                                          AS discount,
       COALESCE(fc.commission, 0)                                  AS franchisee_disc,
       o."TotalAmount"                                             AS net,
       o."TotalAmount" - o."GstAmount"                             AS taxable,
       o."GstAmount"                                               AS gst
  FROM public.orders o
  JOIN public."OrderItems" oi ON oi."OrderId" = o."Id"
  LEFT JOIN (SELECT "OrderId", sum("CommissionAmount") AS commission
               FROM public."FranchiseCommissionEntries" GROUP BY "OrderId") fc
         ON fc."OrderId" = o."Id"
 WHERE o."InternalNotes" LIKE '%[QA-REPORTS-2026-08-12]%'
 GROUP BY o."Id", o."OrderNumber", o."SourceNo", o."Source", o."Status", o."CreatedAt",
          o."DiscountAmount", o."TotalAmount", o."GstAmount", fc.commission
 ORDER BY o."CreatedAt";

-- 2b. Revenue basis. The reports exclude Draft/Cancelled/Refunded by default,
--     so the default Sales total should match revenue_net, NOT all_net.
SELECT count(*)                                                                           AS all_orders,
       sum("TotalAmount")                                                                 AS all_net,
       count(*) FILTER (WHERE "Status" NOT IN ('draft','cancelled','refunded'))           AS revenue_orders,
       sum("TotalAmount") FILTER (WHERE "Status" NOT IN ('draft','cancelled','refunded')) AS revenue_net,
       sum("GstAmount")   FILTER (WHERE "Status" NOT IN ('draft','cancelled','refunded')) AS revenue_gst
  FROM public.orders WHERE "InternalNotes" LIKE '%[QA-REPORTS-2026-08-12]%';
-- Expect exactly:
--   all_orders     19        all_net      169,330.00
--   revenue_orders 17        revenue_net  151,630.00     revenue_gst  23,130.00
-- The cancelled (3,540) and refunded (14,160) orders are the two dropped.
-- Cross-check: 151,630 x 18/118 = 23,130 exactly - every price is a multiple of 118.
--
-- These are the SEEDED figures only. On the Sales report they are added to your
-- real orders in the same window, so compare the delta, not the headline.

-- 2c. GST report cross-check. Identity per row:
--       Taxable + CGST + SGST + IGST = Invoice Amt
--       Gross   - Franchisee Disc    = Invoice Amt
SELECT "InvoiceNumber","InvoiceDate","GstClassification",
       "TotalAmount" + "FranchiseShareAmount"                       AS gross,
       "FranchiseShareAmount"                                       AS franchisee_disc,
       "TotalAmount"                                                AS invoice_amt,
       "TaxableAmount","CgstAmount","SgstAmount","IgstAmount",
       "CgstAmount" + "SgstAmount" + "IgstAmount"                   AS total_tax,
       "TaxableAmount" + "CgstAmount" + "SgstAmount" + "IgstAmount" AS ties_to_invoice_amt
  FROM public.invoices
 WHERE "OrderId" IN (SELECT "Id" FROM public.orders
                      WHERE "InternalNotes" LIKE '%[QA-REPORTS-2026-08-12]%')
 ORDER BY "InvoiceNumber";
-- Expect 16 invoices (none for the pending, cancelled and wallet-paid orders),
-- and ties_to_invoice_amt = invoice_amt on every row.

-- 2d. Franchisee share - registered vs unregistered, the 2026-08-07 spec change.
SELECT o."OrderNumber", o."SourceNo", f."Name",
       f."Gstin" IS NOT NULL AND btrim(f."Gstin") <> '' AS registered,
       o."TotalAmount"              AS gross,
       o."FranchiseCommissionBase"  AS commission,
       o."FranchiseCommissionGst"   AS gst_on_commission,
       o."FranchiseShareAmount"     AS total_share,
       o."FranchiseNetPayable"      AS franchisee_pays
  FROM public.orders o JOIN public."Franchises" f ON f."Id" = o."FranchiseId"
 WHERE o."InternalNotes" LIKE '%[QA-REPORTS-2026-08-12]%'
 ORDER BY o."SourceNo";
-- Expect exactly (by SourceNo, since order numbers are assigned at run time):
--   FRN-QA-5521  reg    11,800  10,000  20%  2,000  360  2,360   9,440
--   FRN-QA-5522  reg    11,800  10,000  15%  1,500  270  1,770  10,030
--   FRN-QA-5523  reg     7,080   6,000  25%  1,500  270  1,770   5,310
--   FRN-QA-5524  reg     9,440   8,000  20%  1,600  288  1,888   7,552
--   FRN-QA-5525  unreg  11,800  10,000  20%  2,000    0  2,000   9,800
--   FRN-QA-5526  unreg   5,900   5,000  20%  1,000    0  1,000   4,900
--   FRN-QA-5527  unreg   9,440   8,000  15%  1,200    0  1,200   8,240
--   FRN-QA-5528  unreg   3,540   3,000  25%    750    0    750   2,790
--
-- 5521 and 5525 are the SAME order value at the SAME 20%, differing only by the
-- 360 GST on the commission - exactly what the spec change removed from
-- unregistered franchisees.

-- 2e. Faculty share ledger.
SELECT f."DisplayName", count(*) AS entries, sum(e."BaseAmount") AS base,
       sum(e."ShareAmount") AS share, sum(e."GstOnShare") AS gst_on_share,
       sum(e."TotalPayout") AS payout, bool_or(e."WasGstRegistered") AS registered
  FROM public."FacultyShareEntries" e JOIN public.faculty f ON f."Id" = e."FacultyId"
 WHERE e."OrderId" IN (SELECT "Id" FROM public.orders
                        WHERE "InternalNotes" LIKE '%[QA-REPORTS-2026-08-12]%')
 GROUP BY f."DisplayName" ORDER BY payout DESC;

-- 2f. Shipping.
SELECT o."OrderNumber", s."Courier", s."TrackingNumber", s."Status",
       (s."DispatchedAt" AT TIME ZONE 'Asia/Kolkata')::date AS dispatched_ist,
       count(si."Id") AS items
  FROM public.shipments s JOIN public.orders o ON o."Id" = s."OrderId"
  LEFT JOIN public.shipment_items si ON si."ShipmentId" = s."Id"
 WHERE o."InternalNotes" LIKE '%[QA-REPORTS-2026-08-12]%'
 GROUP BY o."OrderNumber", s."Id", s."Courier", s."TrackingNumber", s."Status", s."DispatchedAt"
 ORDER BY o."OrderNumber";

-- 2g. Re-invoices. Identity per row: Taxable + CGST + SGST + IGST = Total.
--     The re-invoice total equals the ORIGINAL invoice total (already net of
--     the commission) - the two documents differ by FranchiseShareAmount.
SELECT r."ReInvoiceNumber", r."ReInvoiceDate", r."OriginalInvoiceNumber", r."OrderNumber",
       r."FranchiseName", r."PlaceOfSupply", r."GstRate",
       r."TaxableAmount", r."CgstAmount", r."SgstAmount", r."IgstAmount", r."TotalGst",
       r."TotalAmount", r."FranchiseShareAmount",
       r."TaxableAmount" + r."TotalGst" AS ties_to_total,
       i."TotalAmount"                  AS original_invoice_total
  FROM public.franchise_reinvoices r
  JOIN public.invoices i ON i."Id" = r."OriginalInvoiceId"
 WHERE r."OrderId" IN (SELECT "Id" FROM public.orders
                        WHERE "InternalNotes" LIKE '%[QA-REPORTS-2026-08-12]%')
 ORDER BY r."ReInvoiceNumber";
-- Expect 4 rows, GstRate 18.00 on each, ties_to_total = TotalAmount =
-- original_invoice_total. Three franchise invoices are deliberately left
-- un-re-invoiced so "Raise Re-Invoice" still lists pending candidates.

-- 2h. The IST day-boundary case. SourceNo FRN-QA-5525 was stamped 01:30 IST on
--     12 Aug, which is 20:00 UTC on the 11th. It must report on the 12th.
SELECT "OrderNumber", "SourceNo", "CreatedAt" AS stored_utc,
       "CreatedAt" AT TIME ZONE 'Asia/Kolkata'          AS ist,
       ("CreatedAt")::date                              AS utc_date_wrong,
       ("CreatedAt" AT TIME ZONE 'Asia/Kolkata')::date  AS ist_date_correct
  FROM public.orders WHERE "SourceNo" = 'FRN-QA-5525';

-- 2i. Sequence check - the seeded numbers are contiguous and the app's next
--     generated number follows on cleanly.
SELECT 'HJC' AS series,
       max(NULLIF(regexp_replace("OrderNumber", '^.*-', ''), '')::bigint) AS now_at,
       'next will be HJC-' || (max(NULLIF(regexp_replace("OrderNumber", '^.*-', ''), '')::bigint) + 1) AS next
  FROM public.orders
 WHERE "OrderNumber" LIKE 'HJC%' AND regexp_replace("OrderNumber", '^.*-', '') ~ '^[0-9]+$'
UNION ALL
SELECT 'FRN',
       max(NULLIF(regexp_replace("OrderNumber", '^.*-', ''), '')::bigint),
       'next will be FRN-' || (max(NULLIF(regexp_replace("OrderNumber", '^.*-', ''), '')::bigint) + 1)
  FROM public.orders
 WHERE "OrderNumber" LIKE 'FRN-%' AND regexp_replace("OrderNumber", '^.*-', '') ~ '^[0-9]+$';


-- ────────────────────────────────────────────────────────────────────────────
-- 3. ROLLBACK - removes every seeded row, keyed on the InternalNotes marker.
--    Numbers are NOT returned to the sequences; you get a gap, as a cancelled
--    order would leave. Cosmetic only.
-- ────────────────────────────────────────────────────────────────────────────
/*
BEGIN;

CREATE TEMP TABLE qa_del ON COMMIT DROP AS
SELECT "Id" FROM public.orders WHERE "InternalNotes" LIKE '%[QA-REPORTS-2026-08-12]%';

DELETE FROM public.franchise_reinvoice_items WHERE "ReInvoiceId" IN (
    SELECT "Id" FROM public.franchise_reinvoices WHERE "OrderId" IN (SELECT "Id" FROM qa_del));
DELETE FROM public.franchise_reinvoices WHERE "OrderId" IN (SELECT "Id" FROM qa_del);

-- Only present if you ran optional block 1b, or generated a settlement from the
-- admin UI. Deleting it restores that faculty's real earnings to the
-- "not yet settled" figure.
DELETE FROM public.teacher_settlement_payments WHERE "SettlementId" IN (
    SELECT "Id" FROM public.teacher_settlements WHERE "Notes" = 'QA seed - part payment');
DELETE FROM public.teacher_settlement_items WHERE "SettlementId" IN (
    SELECT "Id" FROM public.teacher_settlements WHERE "Notes" = 'QA seed - part payment');
DELETE FROM public.teacher_settlements WHERE "Notes" = 'QA seed - part payment';

DELETE FROM public.shipment_items WHERE "ShipmentId" IN (
    SELECT "Id" FROM public.shipments WHERE "OrderId" IN (SELECT "Id" FROM qa_del));
DELETE FROM public.shipments WHERE "OrderId" IN (SELECT "Id" FROM qa_del);

DELETE FROM public.refunds                      WHERE "OrderId" IN (SELECT "Id" FROM qa_del);
DELETE FROM public."FacultyShareEntries"        WHERE "OrderId" IN (SELECT "Id" FROM qa_del);
DELETE FROM public."FranchiseCommissionEntries" WHERE "OrderId" IN (SELECT "Id" FROM qa_del);
DELETE FROM public.invoices                     WHERE "OrderId" IN (SELECT "Id" FROM qa_del);
DELETE FROM public."OrderItems"                 WHERE "OrderId" IN (SELECT "Id" FROM qa_del);
DELETE FROM public.orders                       WHERE "Id"      IN (SELECT "Id" FROM qa_del);

-- Should return 0 on every count.
SELECT (SELECT count(*) FROM public.orders
         WHERE "InternalNotes" LIKE '%[QA-REPORTS-2026-08-12]%')                  AS orders,
       (SELECT count(*) FROM public.invoices
         WHERE "OrderId" IN (SELECT "Id" FROM qa_del))                            AS invoices,
       (SELECT count(*) FROM public."FacultyShareEntries"
         WHERE "OrderId" IN (SELECT "Id" FROM qa_del))                            AS faculty,
       (SELECT count(*) FROM public."FranchiseCommissionEntries"
         WHERE "OrderId" IN (SELECT "Id" FROM qa_del))                            AS commissions;

COMMIT;
*/

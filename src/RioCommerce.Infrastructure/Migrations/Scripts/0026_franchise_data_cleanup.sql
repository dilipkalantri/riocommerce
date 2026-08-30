-- 0026_franchise_data_cleanup.sql
--
-- Two corrections, both prerequisites for the commission-GST split added in 0025. That change made
-- the presence of a GSTIN decide how much a franchisee is PAID (registered → commission + GST on it;
-- unregistered → bare commission), so a junk value in the Gstin column is no longer just a cosmetic
-- problem on an invoice — it silently pays the wrong amount and prints an invalid GSTIN on a legal
-- tax invoice.
--
-- Verified before writing: none of the affected franchisees has any order or invoice, so nothing
-- historical is rewritten by this script. It is purely forward-looking.

-- ── 1. Clear GSTINs that are not GSTINs ────────────────────────────────────────────────────────
--
-- Matched by FORMAT rather than by hardcoded value, so this also catches anything similar that was
-- entered in another environment: 2 digits (state) + 10-char PAN + entity code + 'Z' + checksum.
--
-- Values cleared in this database, recorded here so they are recoverable from source control:
--     BHA  SKD GROUP OF INSITUTION    '04AJYPG4002B1Z'  (14 chars — a real GSTIN missing its final
--                                                        checksum character; see the note below)
--     CHA2 SKD GROUP OF INSTITUTIONS  '50200003244283'  (14 digits — a bank account number)
--     —    asas                       'DSDS'            (test record, already inactive)
--     —    test 2 2606 Franchisee     'GSTIN010101'     (test record, already inactive)
--
-- NOTE on BHA: '04AJYPG4002B1Z' is structurally a VALID GSTIN with the last character truncated,
-- and '04' is the Chandigarh state code while the record says Punjab — it most likely belongs to
-- the Chandigarh entity (CHA2), which is the same business group. This franchisee is probably
-- genuinely GST-registered. Clearing it moves them to the unregistered commission rate. Once the
-- correct 15-character GSTIN is obtained, restore it with:
--     UPDATE public."Franchises" SET "Gstin" = '<correct 15-char GSTIN>' WHERE "Code" = 'BHA';
-- and their commission returns to the registered rate automatically on the next order.

UPDATE public."Franchises"
SET "Gstin" = NULL
WHERE "Gstin" IS NOT NULL
  AND btrim("Gstin") <> ''
  AND btrim(upper("Gstin")) !~ '^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$';

-- Normalise the survivors to the trimmed upper-case form the application always writes, so a
-- stray space can never make a valid GSTIN read as invalid on a later pass. Cannot alter a
-- correctly-formatted value.
UPDATE public."Franchises"
SET "Gstin" = btrim(upper("Gstin"))
WHERE "Gstin" IS NOT NULL
  AND "Gstin" <> btrim(upper("Gstin"));

-- ── 2. Disable the three internal HJC branch records ───────────────────────────────────────────
--
-- HJC Mumbai / Nagpur / Nashik are the institute's own branches, not third-party franchisees. They
-- carry no State, which makes them straddle a real inconsistency: order creation treats a blank
-- state as INTER-state (charges IGST) while the GST report treats blank as INTRA-state (CGST/SGST),
-- so any order they placed would be taxed one way and reported the other.
--
-- All three have zero orders, so nothing is lost. Wallet balances are NOT touched (₹28,000 /
-- ₹62,000 / ₹45,000 remain on the books) — deactivation only blocks portal login and removes them
-- from active-franchise listings and bulk assignment. Re-enable at any time with:
--     UPDATE public."Franchises" SET "IsActive" = true WHERE "Code" IN ('MB','NP','NK');

UPDATE public."Franchises"
SET "IsActive" = false
WHERE "Code" IN ('MB', 'NP', 'NK')
  AND "IsActive" = true;

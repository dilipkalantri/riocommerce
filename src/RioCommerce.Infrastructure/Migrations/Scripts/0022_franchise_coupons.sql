-- ============================================================================
-- 0022_franchise_coupons.sql
-- Lets a discount code be redeemed in the Franchise Portal as well as (or
-- instead of) the customer website.
--
-- Until now every coupon was implicitly customer-only: /franchise/order had no
-- discount field at all, and the franchisee's only reduction was their own
-- share. Two audience flags now say who may redeem a code:
--
--   "IsCustomerApplicable"  — website checkout (CheckoutService)
--   "IsFranchiseApplicable" — Franchise Portal  (FranchisePortalService)
--
-- The defaults preserve today's behaviour exactly: every existing coupon stays
-- usable on the website, and none becomes usable by franchisees until an admin
-- ticks the box in /admin/coupons.
--
-- IMPORTANT (business rule): a franchise coupon reduces the ORDER TOTAL. It
-- does NOT touch the franchisee's share, which is still calculated from product
-- pricing and commission rules. The franchisee therefore keeps their full share
-- AND gets the coupon off the balance they pay. No schema change is needed for
-- that — orders already carry "DiscountAmount", "CouponId", "CouponCode",
-- "FranchiseShareAmount" and "FranchiseNetPayable" as separate columns.
--
-- Fully additive and idempotent (per the SqlMigrationRunner contract): safe to
-- re-run.
-- ============================================================================

ALTER TABLE public."Coupons"
    ADD COLUMN IF NOT EXISTS "IsCustomerApplicable" boolean NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS "IsFranchiseApplicable" boolean NOT NULL DEFAULT false;

-- Franchise-portal coupon lookup is by code on every keystroke of "Apply", so
-- keep the franchise-eligible set cheap to scan.
CREATE INDEX IF NOT EXISTS "IX_Coupons_IsFranchiseApplicable"
    ON public."Coupons" ("IsFranchiseApplicable")
    WHERE "IsFranchiseApplicable";

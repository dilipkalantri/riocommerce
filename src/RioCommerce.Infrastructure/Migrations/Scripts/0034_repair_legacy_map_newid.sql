-- 0034 — Repair legacy_product_map / legacy_user_map rows whose NewId was written as all-zeros.
--
-- Defect this repairs
-- -------------------
-- LegacyMigrationService recorded its map rows by reading the new entity's Id immediately after
-- _db.Products.Add(p) / _db.Users.Add(u) — but every BaseEntity.Id is configured
-- HasDefaultValueSql("gen_random_uuid()"), so EF treats the key as STORE-generated and leaves the
-- CLR property at Guid.Empty until SaveChanges. The map therefore captured
-- '00000000-0000-0000-0000-000000000000' for every inserted row.
--
-- Observed: all 103 legacy_product_map rows carried an all-zero NewId. legacy_user_map was still
-- empty (customers had not been imported yet), so this script is a no-op there today — it is written
-- to cover both tables because the same defect produced both, and a user import may have run by the
-- time this reaches another environment.
--
-- Nothing was mis-displayed as a result: the importer's own idempotency falls back to matching on
-- email / SKU, so re-runs never duplicated. What was lost is the cross-system linkage, which a
-- future order migration must join through to attach an old order to the right new user and product.
--
-- The going-forward fix is in LegacyMigrationService, which now assigns Id = Guid.NewGuid() before
-- Add. This script exists only for rows written before it.
--
-- Method: re-derive the link from LegacyKey, which the importer stored alongside NewId —
--   legacy_product_map.LegacyKey  = the old nopCommerce Sku      -> products."Sku"
--   legacy_user_map.LegacyKey     = the old nopCommerce email    -> users."Email"  (citext, so the
--                                                                   comparison is case-insensitive)
--
-- Only all-zero rows are touched, and only where the key resolves to EXACTLY ONE row in the target
-- table. A key that matches nothing, or matches more than one, is left alone: guessing a linkage is
-- worse than leaving it visibly broken, and both tables index NewId without a unique constraint so
-- residual zero rows stay harmless. Re-running matches nothing once applied.

-- ── Products ────────────────────────────────────────────────────────────────────────────────────
UPDATE legacy_product_map m
SET "NewId" = p."Id",
    "UpdatedAt" = NOW()
FROM products p
WHERE m."NewId" = '00000000-0000-0000-0000-000000000000'
  AND m."LegacyKey" IS NOT NULL
  AND TRIM(m."LegacyKey") <> ''
  AND p."Sku" = m."LegacyKey"
  AND (SELECT COUNT(*) FROM products p2 WHERE p2."Sku" = m."LegacyKey") = 1;

-- ── Users ───────────────────────────────────────────────────────────────────────────────────────
UPDATE legacy_user_map m
SET "NewId" = u."Id",
    "UpdatedAt" = NOW()
FROM users u
WHERE m."NewId" = '00000000-0000-0000-0000-000000000000'
  AND m."LegacyKey" IS NOT NULL
  AND TRIM(m."LegacyKey") <> ''
  AND u."Email" = m."LegacyKey"
  AND (SELECT COUNT(*) FROM users u2 WHERE u2."Email" = m."LegacyKey") = 1;

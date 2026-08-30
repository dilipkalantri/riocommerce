-- 0013_serial_key_config_per_mode.sql
-- Adds per-lecture-mode serial-key configuration.
--
-- A product_serial_key_configs row may now be scoped to a specific ProductMode:
--   * "ProductModeId" IS NULL  → the PRODUCT-LEVEL config (fallback, unchanged existing behaviour).
--   * "ProductModeId" = <mode> → a PER-MODE override applied only when that mode is purchased.
--
-- Resolution order at key-generation time: mode-specific row → product-level row → no key.
-- Fully additive: every existing row keeps ProductModeId = NULL and behaves exactly as before.
--
-- Idempotent (per SqlMigrationRunner contract): safe to re-run.

-- 1) New nullable column.
ALTER TABLE public.product_serial_key_configs
    ADD COLUMN IF NOT EXISTS "ProductModeId" uuid NULL;

-- 2) FK to ProductModes (cascade so a per-mode override is removed with its mode).
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'FK_product_serial_key_configs_ProductModes_ProductModeId'
          AND table_name = 'product_serial_key_configs'
    ) THEN
        ALTER TABLE public.product_serial_key_configs
            ADD CONSTRAINT "FK_product_serial_key_configs_ProductModes_ProductModeId"
            FOREIGN KEY ("ProductModeId") REFERENCES public."ProductModes"("Id") ON DELETE CASCADE;
    END IF;
END $$;

-- 3) Index on the new column for the enqueue lookup.
CREATE INDEX IF NOT EXISTS "IX_product_serial_key_configs_ProductModeId"
    ON public.product_serial_key_configs USING btree ("ProductModeId");

-- 4) Replace the old (ProductId, ProviderKey, IsActive) partial index with one that includes
--    ProductModeId, so the product-level row and each per-mode override can coexist as active.
DROP INDEX IF EXISTS public."IX_product_serial_key_configs_ProductId_ProviderKey_IsActive";

CREATE INDEX IF NOT EXISTS "IX_product_serial_key_configs_ProductId_ProductModeId_ProviderKe"
    ON public.product_serial_key_configs USING btree ("ProductId", "ProductModeId", "ProviderKey", "IsActive")
    WHERE ("IsActive" = true);

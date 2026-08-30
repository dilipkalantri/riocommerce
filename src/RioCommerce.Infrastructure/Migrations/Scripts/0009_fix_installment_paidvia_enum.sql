-- ============================================================================
-- 0009_fix_installment_paidvia_enum.sql
-- Fixes a type mismatch introduced by 0008: order_installments."PaidVia" was
-- created as integer, but the application writes the native enum public.payment_mode
-- (PaymentMode is registered with Npgsql via MapEnum<PaymentMode>()), producing:
--   42804: column "PaidVia" is of type integer but expression is of type payment_mode
--
-- This converts the column to public.payment_mode (nullable). The table is new in
-- 0008 and PaidVia is only set when an installment is collected, so in practice the
-- column is all-NULL at upgrade time; the USING clause maps any stray integer value
-- by ordinal just in case.
--
-- Idempotent: only alters when the column isn't already payment_mode.
-- ============================================================================

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name   = 'order_installments'
          AND column_name  = 'PaidVia'
          AND data_type    = 'integer'
    ) THEN
        -- enum_range is 0-based by ordinal; map an integer ordinal to the matching label.
        ALTER TABLE public.order_installments
            ALTER COLUMN "PaidVia" TYPE public.payment_mode
            USING (
                CASE
                    WHEN "PaidVia" IS NULL THEN NULL
                    ELSE (enum_range(NULL::public.payment_mode))["PaidVia" + 1]
                END
            );
    END IF;
END $$;

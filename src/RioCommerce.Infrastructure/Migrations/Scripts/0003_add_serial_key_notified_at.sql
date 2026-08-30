-- ============================================================================
-- 0003_add_serial_key_notified_at.sql
-- Adds serial_key_records."NotifiedAt" — the timestamp of the one-and-only
-- "your access key is ready" customer notification. Guards against duplicate
-- emails/SMS when the retry task reprocesses a record.
--
-- Idempotent: safe to re-run.
-- ============================================================================

ALTER TABLE public.serial_key_records
    ADD COLUMN IF NOT EXISTS "NotifiedAt" timestamp with time zone;

-- Back-fill: any record that already has a key is treated as already-notified, so the fix
-- doesn't trigger a fresh blast of emails for keys generated before this column existed.
UPDATE public.serial_key_records
SET "NotifiedAt" = COALESCE("GeneratedAt", "ActivatedAt", now())
WHERE "NotifiedAt" IS NULL
  AND "SerialKey" IS NOT NULL;

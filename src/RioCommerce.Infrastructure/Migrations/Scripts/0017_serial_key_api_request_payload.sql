-- 0017_serial_key_api_request_payload.sql
-- Adds a dedicated column to capture the ACTUAL parameters sent to the provider's API
-- (Rio's wrapped { "Entity": {...} } body, or Valence's form fields with the secret masked),
-- as JSON, for per-attempt parameter inspection. Distinct from "RequestPayload", which holds
-- our internal provider-agnostic request.
--
-- Fully additive and idempotent (per SqlMigrationRunner contract): safe to re-run.

ALTER TABLE public.serial_key_records
    ADD COLUMN IF NOT EXISTS "ApiRequestPayload" jsonb NULL;

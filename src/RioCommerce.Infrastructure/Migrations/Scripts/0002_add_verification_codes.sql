-- ============================================================================
-- 0002_add_verification_codes.sql
-- Registration verification (OTP):
--   • verification_codes table — one-time 6-digit codes for customer signup and
--     franchisee applications, stored hashed.
--   • adds 'unverified' to the franchise_status enum so a franchisee application
--     can be held out of the admin queue until its contact is verified.
--
-- Idempotent: safe to re-run.
-- ============================================================================

-- New enum value. PG 12+ allows ADD VALUE inside a transaction provided the value
-- isn't used in the same transaction (we only add it here). IF NOT EXISTS keeps re-runs safe.
ALTER TYPE public.franchise_status ADD VALUE IF NOT EXISTS 'unverified';

CREATE TABLE IF NOT EXISTS public.verification_codes (
    "Id"           uuid                     NOT NULL DEFAULT gen_random_uuid(),
    "Purpose"      integer                  NOT NULL,
    "Channel"      integer                  NOT NULL,
    "Target"       character varying(256)   NOT NULL,
    "CodeHash"     character varying(256)   NOT NULL,
    "SubjectId"    uuid,
    "ExpiresAt"    timestamp with time zone NOT NULL,
    "AttemptCount" integer                  NOT NULL DEFAULT 0,
    "ConsumedAt"   timestamp with time zone,
    "CreatedAt"    timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAt"    timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT "PK_verification_codes" PRIMARY KEY ("Id")
);

-- Hot path: "latest unconsumed code for this (purpose, target)".
CREATE INDEX IF NOT EXISTS "IX_verification_codes_Purpose_Target"
    ON public.verification_codes ("Purpose", "Target");

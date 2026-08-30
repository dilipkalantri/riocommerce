-- ============================================================================
-- 0020_add_superclass_integration.sql
-- Adds the Superclass LMS provider (registration-style, keyless):
--   1. Three nullable outcome columns on serial_key_records to hold the
--      registration result — expiry, subscription status, and the assigned
--      course reference. Null for the existing key-issuing providers
--      (RioPlay / Valence), which are unaffected.
--   2. The singleton superclass_settings table holding the GLOBAL API base URL,
--      Data-Protection-encrypted API key, default class id (318), environment,
--      and logging/retry settings.
--
-- Fully additive and idempotent (per SqlMigrationRunner contract): safe to re-run.
-- ============================================================================

-- 1. Registration-style outcome columns on the shared fulfilment queue.
ALTER TABLE public.serial_key_records
    ADD COLUMN IF NOT EXISTS "ExpiresAt" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "SubscriptionStatus" character varying(64) NULL,
    ADD COLUMN IF NOT EXISTS "ProviderCourseRef" character varying(64) NULL;

-- 2. Global Superclass settings (singleton row).
CREATE TABLE IF NOT EXISTS public.superclass_settings (
    "Id"               uuid DEFAULT gen_random_uuid() NOT NULL,
    "ApiBaseUrl"       character varying(300) NOT NULL,
    "ApiKeyEncrypted"  character varying(4000) NOT NULL,
    "DefaultClassId"   integer NOT NULL DEFAULT 318,
    "Environment"      character varying(40) NOT NULL DEFAULT 'Production',
    "LoggingEnabled"   boolean NOT NULL DEFAULT TRUE,
    "MaxRetryAttempts" integer NOT NULL DEFAULT 5,
    "IsActive"         boolean NOT NULL DEFAULT TRUE,
    "CreatedAt"        timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt"        timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "PK_superclass_settings" PRIMARY KEY ("Id")
);

-- Singleton guard: at most one active settings row (mirrors rioplay_tenants' IsDefault pattern).
CREATE UNIQUE INDEX IF NOT EXISTS "IX_superclass_settings_IsActive"
    ON public.superclass_settings USING btree ("IsActive")
    WHERE ("IsActive" = true);

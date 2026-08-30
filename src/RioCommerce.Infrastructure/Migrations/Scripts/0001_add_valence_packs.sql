-- ============================================================================
-- 0001_add_valence_packs.sql
-- Adds the valence_packs table — a local cache of Valence (Edubees) packs synced
-- from the get_packs endpoint, used to populate the pack picker on the product
-- serial-key config screen when the provider is Valence.
--
-- Idempotent: safe to re-run.
-- ============================================================================

CREATE TABLE IF NOT EXISTS public.valence_packs (
    "Id"           uuid                     NOT NULL DEFAULT gen_random_uuid(),
    "ExternalId"   integer                  NOT NULL,
    "PackName"     character varying(300)   NOT NULL,
    "Tags"         character varying(1000),
    "IsActive"     boolean                  NOT NULL DEFAULT true,
    "LastSyncedAt" timestamp with time zone NOT NULL DEFAULT now(),
    "CreatedAt"    timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAt"    timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT "PK_valence_packs" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_valence_packs_ExternalId"
    ON public.valence_packs ("ExternalId");

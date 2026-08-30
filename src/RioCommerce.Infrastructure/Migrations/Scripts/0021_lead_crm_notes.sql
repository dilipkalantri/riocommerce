-- Lead CRM: follow-up notes, next-follow-up date, and the extra pipeline stages.
--
-- Website lead capture is untouched by this script: every column added here is nullable with no
-- default behaviour change, so an inbound lead still writes exactly the same row it does today.
--
-- Idempotent throughout (IF NOT EXISTS everywhere) so a half-applied script re-runs safely.

-- 1. New lifecycle stages on the existing PostgreSQL enum type.
--    Labels can only ever be APPENDED to a PG enum, never renumbered or removed, which is why the
--    C# LeadStatus enum appends these three after the original five rather than slotting them into
--    pipeline order (the admin UI orders the dropdown explicitly via LeadStageMeta.Pipeline).
--    Requires PostgreSQL 12+ to run inside the migration runner's transaction; the new labels are
--    only *added* here, never *used*, which is the restriction that applies within a transaction.
ALTER TYPE public.lead_status ADD VALUE IF NOT EXISTS 'interested';
ALTER TYPE public.lead_status ADD VALUE IF NOT EXISTS 'demo_scheduled';
ALTER TYPE public.lead_status ADD VALUE IF NOT EXISTS 'payment_pending';

-- 2. Next scheduled follow-up. Nullable = nothing scheduled (the state every existing lead starts in).
ALTER TABLE public."Leads" ADD COLUMN IF NOT EXISTS "NextFollowUpAt" timestamp with time zone;

-- 3. Append-only follow-up/conversation history.
CREATE TABLE IF NOT EXISTS public."LeadNotes" (
    "Id"              uuid DEFAULT gen_random_uuid() NOT NULL,
    "LeadId"          uuid NOT NULL,
    "Body"            text NOT NULL,
    "CreatedByUserId" uuid,
    "CreatedByName"   text NOT NULL DEFAULT 'system',
    "CreatedAt"       timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt"       timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT "PK_LeadNotes" PRIMARY KEY ("Id")
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'FK_LeadNotes_Leads_LeadId'
    ) THEN
        ALTER TABLE public."LeadNotes"
            ADD CONSTRAINT "FK_LeadNotes_Leads_LeadId"
            FOREIGN KEY ("LeadId") REFERENCES public."Leads"("Id") ON DELETE CASCADE;
    END IF;
END $$;

-- Every read is "notes for one lead, newest first".
CREATE INDEX IF NOT EXISTS "IX_LeadNotes_LeadId_CreatedAt"
    ON public."LeadNotes" ("LeadId", "CreatedAt");

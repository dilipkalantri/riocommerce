-- 0036 — optional CC addresses on a message template.
--
-- Generic by design: the column exists on every template but stays NULL unless an admin fills it in
-- on Admin → Notifications. NULL means "no CC", which is exactly the behaviour every existing
-- template already has, so nothing changes for them.
--
-- Backward-compatible and non-destructive:
--   * ADD COLUMN IF NOT EXISTS — safe to re-run, and a no-op if the column is already there.
--   * Nullable with no DEFAULT — no rewrite of existing rows, no Body/Subject touched, no
--     admin-entered value overwritten.
--   * 512 chars fits two addresses at the RFC-5321 maximum of 254 each plus the separator.

ALTER TABLE message_templates
    ADD COLUMN IF NOT EXISTS "CcEmails" character varying(512);

COMMENT ON COLUMN message_templates."CcEmails" IS
    'Optional comma-separated CC addresses for the Email channel (max 2). NULL = no CC.';

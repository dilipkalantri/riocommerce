-- 0033 — Add "CA Final" to the course_level enum.
--
-- CourseLevel is a native PostgreSQL enum (HasPostgresEnum<CourseLevel>), so a new C# member is not
-- enough on its own: the label has to exist in the database or Npgsql cannot map it.
--
-- AFTER 'ca_intermediate' sets the label's sort position inside the type, which is what ORDER BY on
-- a course_level column follows. It does NOT affect the C# ordinal — that stays at the end of the
-- enum on purpose, because the public search serialises the ordinal into its URL.
--
-- Safe to run inside the migration runner's per-script transaction: PostgreSQL 12+ permits
-- ALTER TYPE ... ADD VALUE in a transaction provided the new label is not USED in that same
-- transaction, and this script only declares it. The runner calls ReloadTypesAsync afterwards so
-- Npgsql picks the new label up without a restart.

ALTER TYPE course_level ADD VALUE IF NOT EXISTS 'ca_final' AFTER 'ca_intermediate';

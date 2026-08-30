-- 0038 — Rename course_level enum labels from CA-specific to generic names.
--
-- The C# CourseLevel enum members have been renamed:
--   CaFoundation  → Beginner
--   CaIntermediate → Intermediate
--   CaFinal        → Advanced
--
-- Npgsql's default SnakeCaseNameTranslator maps these to snake_case labels,
-- so the database labels must match:
--   ca_foundation  → beginner
--   ca_intermediate → intermediate
--   ca_final        → advanced
--
-- books and test_series are unchanged.
--
-- ALTER TYPE ... RENAME VALUE requires PostgreSQL 10+.
-- Safe inside a transaction — no new values are being added.

ALTER TYPE course_level RENAME VALUE 'ca_foundation'   TO 'beginner';
ALTER TYPE course_level RENAME VALUE 'ca_intermediate' TO 'intermediate';
ALTER TYPE course_level RENAME VALUE 'ca_final'        TO 'advanced';

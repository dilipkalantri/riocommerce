-- 0034 — Retire the Fastrack product type: move every Fastrack product to Exam Oriented.
--
-- Fastrack is being withdrawn as a product type. The nine products that carried it are reassigned
-- to Exam Oriented here so that no row is left on a type the admin can no longer select — otherwise
-- the product editor would need a permanent "legacy" fallback to stop @bind quietly rewriting those
-- products to Regular the first time anyone saved them.
--
-- The 'fastrack' LABEL stays in the course_type enum. Two reasons, both hard constraints:
--   1. PostgreSQL has no ALTER TYPE ... DROP VALUE. The label cannot be removed at all.
--   2. Removing Fastrack from the C# enum would renumber Combo 2→1, FaceToFace 3→2 and
--      ExamOriented 4→3, and the public search puts CourseType ORDINALS in its query string
--      (?CourseType=2), so every existing bookmarked or indexed filter URL would start meaning a
--      different product type.
-- The label is simply left unused. Nothing reads it once this script has run.
--
-- Idempotent: re-running matches zero rows.

UPDATE products
SET    "CourseType" = 'exam_oriented',
       "UpdatedAt"  = now()
WHERE  "CourseType" = 'fastrack';

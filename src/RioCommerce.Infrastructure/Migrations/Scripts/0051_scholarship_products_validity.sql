-- Data-only fix: the two "7th Scholarship Exam" products (English and Marathi medium, self
-- registration) have Validity = NULL, so CourseDetail.razor and the enrolment-confirmation email
-- have no course duration to show. The actual course runs 4 months — set as data, not schema,
-- since the Validity column already exists (0028-era product columns).
--
-- NOTE ON NAMING: lowercase unquoted TABLE name (products), quoted PascalCase COLUMN names.

UPDATE products
SET "Validity" = '4 Months'
WHERE "Id" IN ('ddd280c6-254a-4099-8db0-2bc2da823c60', 'e7782dfd-075b-44fe-9327-097952179695')
  AND "Validity" IS NULL;

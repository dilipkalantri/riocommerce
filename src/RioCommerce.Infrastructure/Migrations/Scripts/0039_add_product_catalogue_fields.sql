-- Add catalogue fields to products: Views, Validity, Language, CourseSchedule
ALTER TABLE products ADD COLUMN IF NOT EXISTS "Views" text;
ALTER TABLE products ADD COLUMN IF NOT EXISTS "Validity" text;
ALTER TABLE products ADD COLUMN IF NOT EXISTS "Language" text;
ALTER TABLE products ADD COLUMN IF NOT EXISTS "CourseSchedule" text;

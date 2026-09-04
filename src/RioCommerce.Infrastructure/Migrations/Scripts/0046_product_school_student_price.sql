-- Two-tier pricing on courses: students on a registered school's roll pay a lower price.
--
-- The column is nullable — a null (or 0) means "no school-student discount on this course".
-- Applied server-side in CartService / CheckoutService when the buyer resolves to an active
-- SchoolStudent row. Never set client-side.
--
-- NOTE ON NAMING: this database uses a lowercase, unquoted TABLE name (products) with quoted
-- PascalCase COLUMN names — same pattern as 0044_product_allow_customer_purchase.sql.

ALTER TABLE products
    ADD COLUMN IF NOT EXISTS "SchoolStudentPrice" numeric(18,2);

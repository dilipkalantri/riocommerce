-- School <-> Student membership.
--
-- The school module (0040) shipped Schools + SchoolUsers, but SchoolUsers models STAFF only
-- (school_user_role = Principal | Coordinator). "Enrollments" is course enrollment
-- (User <-> Product) and is unrelated. So there was no way to say "this student belongs to
-- this school" -- which is why the School Portal dashboard hard-codes a student count of 0.
--
-- This adds that missing relationship as its own table rather than overloading SchoolUsers,
-- so a student is NEVER represented as school staff and can never inherit a principal role.
--
-- Naming follows 0040: quoted PascalCase for tables created by the school module, and an
-- unquoted lowercase `users` for the legacy baseline table (see 0000_baseline.sql:2085).

CREATE TABLE IF NOT EXISTS "SchoolStudents" (
    "Id"             uuid NOT NULL DEFAULT gen_random_uuid(),
    "SchoolId"       uuid NOT NULL,
    "UserId"         uuid NOT NULL,
    "StudentClass"   character varying(20),
    "Section"        character varying(20),
    "RollNumber"     character varying(40),
    "AcademicYearId" uuid,
    "IsActive"       boolean NOT NULL DEFAULT true,
    "CreatedAt"      timestamptz NOT NULL DEFAULT NOW(),
    "UpdatedAt"      timestamptz NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_SchoolStudents" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SchoolStudents_Schools"       FOREIGN KEY ("SchoolId")       REFERENCES "Schools" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SchoolStudents_Users"         FOREIGN KEY ("UserId")         REFERENCES users     ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SchoolStudents_AcademicYears" FOREIGN KEY ("AcademicYearId") REFERENCES "AcademicYears" ("Id")
);

-- One active membership row per (school, student). A student moving schools gets a second row
-- against the new school rather than an edit, so history is preserved.
CREATE UNIQUE INDEX IF NOT EXISTS "IX_SchoolStudents_SchoolId_UserId"
    ON "SchoolStudents" ("SchoolId", "UserId");

-- Every principal query filters by SchoolId first.
CREATE INDEX IF NOT EXISTS "IX_SchoolStudents_SchoolId" ON "SchoolStudents" ("SchoolId");
CREATE INDEX IF NOT EXISTS "IX_SchoolStudents_UserId"   ON "SchoolStudents" ("UserId");

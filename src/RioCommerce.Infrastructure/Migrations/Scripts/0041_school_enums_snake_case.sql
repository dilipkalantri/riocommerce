-- 0041_school_enums_snake_case.sql
--
-- 0040 created school_type / school_user_role / gender with PascalCase labels
-- ('Primary', 'HigherSecondary', 'Male', …). Every other enum in this schema uses
-- lowercase snake_case ('draft', 'partial_refund', 'test_series'), which is what
-- Npgsql's default NpgsqlSnakeCaseNameTranslator emits when a MapEnum<T>() /
-- HasPostgresEnum<T>() pair is registered — see the note in Core/Enums/LeadStatus.cs.
--
-- With the PascalCase labels in place, EF could not write these columns at all:
--   42804: column "SchoolType" is of type school_type but expression is of type integer
--
-- Renaming is safe regardless of existing rows: ALTER TYPE … RENAME VALUE changes the
-- label, not the stored value, so rows and column defaults ('Combined'::school_type)
-- keep pointing at the same enum member and simply render under the new name.
--
-- Each rename is guarded so the script stays idempotent if it is re-run after a
-- mid-script failure.

DO $$
DECLARE
    r record;
BEGIN
    FOR r IN
        SELECT * FROM (VALUES
            ('school_type',      'Primary',         'primary'),
            ('school_type',      'Secondary',       'secondary'),
            ('school_type',      'HigherSecondary', 'higher_secondary'),
            ('school_type',      'Combined',        'combined'),
            ('school_user_role', 'Principal',       'principal'),
            ('school_user_role', 'Coordinator',     'coordinator'),
            ('gender',           'Male',            'male'),
            ('gender',           'Female',          'female'),
            ('gender',           'Other',           'other')
        ) AS v(type_name, old_label, new_label)
    LOOP
        IF EXISTS (
            SELECT 1 FROM pg_enum e
            JOIN pg_type t ON t.oid = e.enumtypid
            WHERE t.typname = r.type_name AND e.enumlabel = r.old_label
        ) THEN
            EXECUTE format('ALTER TYPE %I RENAME VALUE %L TO %L',
                           r.type_name, r.old_label, r.new_label);
        END IF;
    END LOOP;
END $$;

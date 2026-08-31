-- School module: enums, tables, indexes, and Maharashtra geography seed data

-- ── New PostgreSQL enums ──
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'school_type') THEN
    CREATE TYPE school_type AS ENUM ('Primary', 'Secondary', 'HigherSecondary', 'Combined');
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'school_user_role') THEN
    CREATE TYPE school_user_role AS ENUM ('Principal', 'Coordinator');
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'gender') THEN
    CREATE TYPE gender AS ENUM ('Male', 'Female', 'Other');
  END IF;
END $$;

-- ── Academic Years ──
CREATE TABLE IF NOT EXISTS "AcademicYears" (
    "Id"        uuid NOT NULL DEFAULT gen_random_uuid(),
    "Name"      text NOT NULL,
    "StartDate" date NOT NULL,
    "EndDate"   date NOT NULL,
    "IsCurrent" boolean NOT NULL DEFAULT false,
    "IsActive"  boolean NOT NULL DEFAULT true,
    "CreatedAt" timestamptz NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamptz NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_AcademicYears" PRIMARY KEY ("Id")
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_AcademicYears_Name" ON "AcademicYears" ("Name");

-- ── States ──
CREATE TABLE IF NOT EXISTS "States" (
    "Id"        uuid NOT NULL DEFAULT gen_random_uuid(),
    "Name"      text NOT NULL,
    "Code"      text NOT NULL,
    "IsActive"  boolean NOT NULL DEFAULT true,
    "CreatedAt" timestamptz NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamptz NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_States" PRIMARY KEY ("Id")
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_States_Code" ON "States" ("Code");

-- ── Districts ──
CREATE TABLE IF NOT EXISTS "Districts" (
    "Id"        uuid NOT NULL DEFAULT gen_random_uuid(),
    "Name"      text NOT NULL,
    "StateId"   uuid NOT NULL,
    "IsActive"  boolean NOT NULL DEFAULT true,
    "CreatedAt" timestamptz NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamptz NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_Districts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Districts_States" FOREIGN KEY ("StateId") REFERENCES "States" ("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Districts_StateId_Name" ON "Districts" ("StateId", "Name");

-- ── Talukas ──
CREATE TABLE IF NOT EXISTS "Talukas" (
    "Id"          uuid NOT NULL DEFAULT gen_random_uuid(),
    "Name"        text NOT NULL,
    "DistrictId"  uuid NOT NULL,
    "IsActive"    boolean NOT NULL DEFAULT true,
    "CreatedAt"   timestamptz NOT NULL DEFAULT NOW(),
    "UpdatedAt"   timestamptz NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_Talukas" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Talukas_Districts" FOREIGN KEY ("DistrictId") REFERENCES "Districts" ("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Talukas_DistrictId_Name" ON "Talukas" ("DistrictId", "Name");

-- ── Schools ──
CREATE TABLE IF NOT EXISTS "Schools" (
    "Id"            uuid NOT NULL DEFAULT gen_random_uuid(),
    "UdiseCode"     varchar(20) NOT NULL,
    "Name"          varchar(300) NOT NULL,
    "Address"       text,
    "SchoolType"    school_type NOT NULL DEFAULT 'Combined',
    "LowestClass"   integer NOT NULL DEFAULT 1,
    "HighestClass"  integer NOT NULL DEFAULT 10,
    "CityOrVillage" text,
    "TalukaId"      uuid,
    "DistrictId"    uuid,
    "StateId"       uuid,
    "PinCode"       text,
    "IsActive"      boolean NOT NULL DEFAULT true,
    "CreatedAt"     timestamptz NOT NULL DEFAULT NOW(),
    "UpdatedAt"     timestamptz NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_Schools" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Schools_Talukas"   FOREIGN KEY ("TalukaId")   REFERENCES "Talukas"   ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Schools_Districts" FOREIGN KEY ("DistrictId") REFERENCES "Districts" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Schools_States"    FOREIGN KEY ("StateId")    REFERENCES "States"    ("Id") ON DELETE SET NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Schools_UdiseCode" ON "Schools" ("UdiseCode");

-- ── School Users (Principal/Coordinator ↔ School junction) ──
CREATE TABLE IF NOT EXISTS "SchoolUsers" (
    "Id"        uuid NOT NULL DEFAULT gen_random_uuid(),
    "SchoolId"  uuid NOT NULL,
    "UserId"    uuid NOT NULL,
    "Role"      school_user_role NOT NULL DEFAULT 'Principal',
    "IsActive"  boolean NOT NULL DEFAULT true,
    "CreatedAt" timestamptz NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamptz NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_SchoolUsers" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SchoolUsers_Schools" FOREIGN KEY ("SchoolId") REFERENCES "Schools" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SchoolUsers_Users"   FOREIGN KEY ("UserId")   REFERENCES users     ("Id") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_SchoolUsers_SchoolId_UserId" ON "SchoolUsers" ("SchoolId", "UserId");

-- ── Seed initial academic year ──
INSERT INTO "AcademicYears" ("Name", "StartDate", "EndDate", "IsCurrent", "IsActive")
VALUES ('2025-26', '2025-06-01', '2026-05-31', true, true)
ON CONFLICT ("Name") DO NOTHING;

-- ══════════════════════════════════════════════════════════════
-- Maharashtra Geography Seed Data
-- ══════════════════════════════════════════════════════════════

-- Insert Maharashtra state
INSERT INTO "States" ("Id", "Name", "Code") VALUES
    ('a0000000-0000-0000-0000-000000000001', 'Maharashtra', 'MH')
ON CONFLICT ("Code") DO NOTHING;

-- Helper: insert district and return its id for taluka references
-- Using a DO block with variables for clean district→taluka linking.

DO $$
DECLARE
    mh_id uuid := 'a0000000-0000-0000-0000-000000000001';
    d_id uuid;
BEGIN

-- ── Ahmednagar ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Ahmednagar',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Ahmednagar' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Ahmednagar',d_id),('Akole',d_id),('Jamkhed',d_id),('Karjat',d_id),('Kopargaon',d_id),
('Nagar',d_id),('Nevasa',d_id),('Parner',d_id),('Pathardi',d_id),('Rahata',d_id),
('Rahuri',d_id),('Sangamner',d_id),('Shevgaon',d_id),('Shrigonda',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Akola ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Akola',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Akola' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Akola',d_id),('Akot',d_id),('Balapur',d_id),('Barshitakli',d_id),('Murtijapur',d_id),
('Patur',d_id),('Telhara',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Amravati ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Amravati',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Amravati' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Achalpur',d_id),('Amravati',d_id),('Anjangaon Surji',d_id),('Bhatkuli',d_id),('Chandur Bazar',d_id),
('Chandur Railway',d_id),('Chikhaldara',d_id),('Daryapur',d_id),('Dharni',d_id),('Morshi',d_id),
('Nandgaon Khandeshwar',d_id),('Teosa',d_id),('Warud',d_id),('Dhamangaon Railway',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Aurangabad (Chhatrapati Sambhajinagar) ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Chhatrapati Sambhajinagar',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Chhatrapati Sambhajinagar' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Aurangabad',d_id),('Gangapur',d_id),('Kannad',d_id),('Khuldabad',d_id),('Paithan',d_id),
('Phulambri',d_id),('Sillod',d_id),('Soegaon',d_id),('Vaijapur',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Beed ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Beed',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Beed' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Ambejogai',d_id),('Ashti',d_id),('Beed',d_id),('Dharur',d_id),('Gevrai',d_id),
('Kaij',d_id),('Majalgaon',d_id),('Parli',d_id),('Patoda',d_id),('Shirur Kasar',d_id),
('Wadwani',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Bhandara ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Bhandara',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Bhandara' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Bhandara',d_id),('Lakhandur',d_id),('Lakhani',d_id),('Mohadi',d_id),('Pauni',d_id),
('Sakoli',d_id),('Tumsar',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Buldhana ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Buldhana',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Buldhana' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Buldhana',d_id),('Chikhli',d_id),('Deulgaon Raja',d_id),('Jalgaon Jamod',d_id),('Khamgaon',d_id),
('Lonar',d_id),('Malkapur',d_id),('Mehkar',d_id),('Motala',d_id),('Nandura',d_id),
('Sangrampur',d_id),('Shegaon',d_id),('Sindkhed Raja',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Chandrapur ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Chandrapur',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Chandrapur' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Ballarpur',d_id),('Bhadravati',d_id),('Bramhapuri',d_id),('Chandrapur',d_id),('Chimur',d_id),
('Gondpipri',d_id),('Jivati',d_id),('Korpana',d_id),('Mul',d_id),('Nagbhid',d_id),
('Pombhurna',d_id),('Rajura',d_id),('Sawali',d_id),('Sindewahi',d_id),('Warora',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Dhule ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Dhule',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Dhule' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Dhule',d_id),('Sakri',d_id),('Shirpur',d_id),('Sindkheda',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Gadchiroli ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Gadchiroli',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Gadchiroli' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Aheri',d_id),('Armori',d_id),('Bhamragad',d_id),('Chamorshi',d_id),('Desaiganj',d_id),
('Dhanora',d_id),('Etapalli',d_id),('Gadchiroli',d_id),('Korchi',d_id),('Kurkheda',d_id),
('Mulchera',d_id),('Sironcha',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Gondia ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Gondia',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Gondia' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Amgaon',d_id),('Arjuni Morgaon',d_id),('Deori',d_id),('Goregaon',d_id),('Gondia',d_id),
('Sadak Arjuni',d_id),('Salekasa',d_id),('Tirora',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Hingoli ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Hingoli',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Hingoli' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Aundha Nagnath',d_id),('Basmath',d_id),('Hingoli',d_id),('Kalamnuri',d_id),('Sengaon',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Jalgaon ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Jalgaon',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Jalgaon' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Amalner',d_id),('Bhusawal',d_id),('Bodwad',d_id),('Bhadgaon',d_id),('Chalisgaon',d_id),
('Chopda',d_id),('Dharangaon',d_id),('Erandol',d_id),('Jalgaon',d_id),('Jamner',d_id),
('Muktainagar',d_id),('Pachora',d_id),('Parola',d_id),('Raver',d_id),('Yawal',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Jalna ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Jalna',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Jalna' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Ambad',d_id),('Badnapur',d_id),('Bhokardan',d_id),('Ghansawangi',d_id),('Jafrabad',d_id),
('Jalna',d_id),('Mantha',d_id),('Partur',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Kolhapur ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Kolhapur',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Kolhapur' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Ajra',d_id),('Bavda',d_id),('Bhudargad',d_id),('Chandgad',d_id),('Gadhinglaj',d_id),
('Hatkanangle',d_id),('Kagal',d_id),('Karvir',d_id),('Panhala',d_id),('Radhanagari',d_id),
('Shahuwadi',d_id),('Shirol',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Latur ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Latur',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Latur' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Ahmadpur',d_id),('Ausa',d_id),('Chakur',d_id),('Deoni',d_id),('Jalkot',d_id),
('Latur',d_id),('Nilanga',d_id),('Renapur',d_id),('Shirur Anantpal',d_id),('Udgir',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Mumbai City ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Mumbai City',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Mumbai City' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Mumbai City',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Mumbai Suburban ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Mumbai Suburban',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Mumbai Suburban' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Andheri',d_id),('Borivali',d_id),('Kurla',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Nagpur ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Nagpur',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Nagpur' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Bhiwapur',d_id),('Hingna',d_id),('Kalameshwar',d_id),('Kamptee',d_id),('Katol',d_id),
('Kuhi',d_id),('Mauda',d_id),('Nagpur City',d_id),('Nagpur Rural',d_id),('Narkhed',d_id),
('Parseoni',d_id),('Ramtek',d_id),('Savner',d_id),('Umred',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Nanded (Dharashiv) ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Nanded',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Nanded' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Ardhapur',d_id),('Bhokar',d_id),('Biloli',d_id),('Deglur',d_id),('Dharmabad',d_id),
('Hadgaon',d_id),('Himayatnagar',d_id),('Kandhar',d_id),('Kinwat',d_id),('Loha',d_id),
('Mahur',d_id),('Mudkhed',d_id),('Mukhed',d_id),('Naigaon',d_id),('Nanded',d_id),
('Umri',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Nandurbar ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Nandurbar',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Nandurbar' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Akkalkuwa',d_id),('Akrani',d_id),('Nandurbar',d_id),('Nawapur',d_id),('Shahada',d_id),
('Taloda',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Nashik ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Nashik',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Nashik' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Baglan',d_id),('Chandwad',d_id),('Deola',d_id),('Dindori',d_id),('Igatpuri',d_id),
('Kalwan',d_id),('Malegaon',d_id),('Nandgaon',d_id),('Nashik',d_id),('Niphad',d_id),
('Peint',d_id),('Sinnar',d_id),('Surgana',d_id),('Trimbakeshwar',d_id),('Yeola',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Dharashiv (Osmanabad) ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Dharashiv',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Dharashiv' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Bhoom',d_id),('Dharashiv',d_id),('Kalamb',d_id),('Lohara',d_id),('Paranda',d_id),
('Tuljapur',d_id),('Umarga',d_id),('Washi',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Palghar ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Palghar',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Palghar' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Dahanu',d_id),('Jawhar',d_id),('Mokhada',d_id),('Palghar',d_id),('Talasari',d_id),
('Vasai',d_id),('Vikramgad',d_id),('Wada',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Parbhani ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Parbhani',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Parbhani' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Gangakhed',d_id),('Jintur',d_id),('Manwath',d_id),('Palam',d_id),('Parbhani',d_id),
('Pathri',d_id),('Purna',d_id),('Selu',d_id),('Sonpeth',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Pune ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Pune',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Pune' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Ambegaon',d_id),('Baramati',d_id),('Bhor',d_id),('Daund',d_id),('Haveli',d_id),
('Indapur',d_id),('Junnar',d_id),('Khed',d_id),('Maval',d_id),('Mulshi',d_id),
('Pune City',d_id),('Purandar',d_id),('Shirur',d_id),('Velhe',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Raigad ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Raigad',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Raigad' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Alibag',d_id),('Karjat',d_id),('Khalapur',d_id),('Mahad',d_id),('Mangaon',d_id),
('Mhasla',d_id),('Murud',d_id),('Panvel',d_id),('Pen',d_id),('Poladpur',d_id),
('Roha',d_id),('Shrivardhan',d_id),('Sudhagad',d_id),('Tala',d_id),('Uran',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Ratnagiri ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Ratnagiri',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Ratnagiri' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Chiplun',d_id),('Dapoli',d_id),('Guhagar',d_id),('Khed',d_id),('Lanja',d_id),
('Mandangad',d_id),('Rajapur',d_id),('Ratnagiri',d_id),('Sangameshwar',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Sangli ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Sangli',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Sangli' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Atpadi',d_id),('Jat',d_id),('Kadegaon',d_id),('Kavathemahankal',d_id),('Khanapur',d_id),
('Miraj',d_id),('Palus',d_id),('Shirala',d_id),('Tasgaon',d_id),('Walwa',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Satara ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Satara',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Satara' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Jaoli',d_id),('Karad',d_id),('Khandala',d_id),('Khatav',d_id),('Koregaon',d_id),
('Mahabaleshwar',d_id),('Man',d_id),('Patan',d_id),('Phaltan',d_id),('Satara',d_id),
('Wai',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Sindhudurg ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Sindhudurg',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Sindhudurg' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Devgad',d_id),('Dodamarg',d_id),('Kankavli',d_id),('Kudal',d_id),('Malwan',d_id),
('Sawantwadi',d_id),('Vaibhavwadi',d_id),('Vengurla',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Solapur ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Solapur',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Solapur' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Akkalkot',d_id),('Barshi',d_id),('Karmala',d_id),('Madha',d_id),('Malshiras',d_id),
('Mangalvedhe',d_id),('Mohol',d_id),('Pandharpur',d_id),('Sangola',d_id),('Solapur North',d_id),
('Solapur South',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Thane ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Thane',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Thane' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Ambarnath',d_id),('Bhiwandi',d_id),('Kalyan',d_id),('Murbad',d_id),('Shahapur',d_id),
('Thane',d_id),('Ulhasnagar',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Wardha ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Wardha',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Wardha' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Arvi',d_id),('Ashti',d_id),('Deoli',d_id),('Hinganghat',d_id),('Karanja',d_id),
('Samudrapur',d_id),('Seloo',d_id),('Wardha',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Washim ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Washim',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Washim' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Karanja',d_id),('Malegaon',d_id),('Mangrulpir',d_id),('Manora',d_id),('Risod',d_id),
('Washim',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

-- ── Yavatmal ──
d_id := gen_random_uuid();
INSERT INTO "Districts" ("Id","Name","StateId") VALUES (d_id,'Yavatmal',mh_id) ON CONFLICT ("StateId","Name") DO NOTHING;
SELECT "Id" INTO d_id FROM "Districts" WHERE "Name"='Yavatmal' AND "StateId"=mh_id;
INSERT INTO "Talukas" ("Name","DistrictId") VALUES
('Arni',d_id),('Babulgaon',d_id),('Darwha',d_id),('Digras',d_id),('Ghatanji',d_id),
('Kalamb',d_id),('Kelapur',d_id),('Mahagaon',d_id),('Maregaon',d_id),('Ner',d_id),
('Pusad',d_id),('Ralegaon',d_id),('Umarkhed',d_id),('Wani',d_id),('Yavatmal',d_id),
('Zari Jamni',d_id)
ON CONFLICT ("DistrictId","Name") DO NOTHING;

END $$;

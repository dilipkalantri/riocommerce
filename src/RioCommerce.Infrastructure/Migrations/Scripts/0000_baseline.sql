-- ============================================================================
-- 0000_baseline.sql
-- RioCommerce — baseline schema (script-based migrations, v0)
--
-- Generated from a pg_dump --schema-only of the live production database on
-- 2026-06-24, then sanitised for execution over Npgsql/ADO.NET (psql
-- meta-commands and session SETs removed). This single file reproduces the
-- complete schema: extensions, enum types, 90 tables, primary keys, indexes,
-- and foreign keys.
--
-- The SqlMigrationRunner executes this ONLY against a database that has no
-- 'schema_migrations' record for it. Existing databases are marked baselined
-- (see the one-time bootstrap in the migration notes) so this never re-runs
-- and never collides with objects that already exist.
-- ============================================================================

SELECT pg_catalog.set_config('search_path', 'public', false);

--
-- TOC entry 2 (class 3079 OID 106265)
-- Name: citext; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS citext WITH SCHEMA public;

--
-- TOC entry 6127 (class 0 OID 0)
-- Dependencies: 2
-- Name: EXTENSION citext; Type: COMMENT; Schema: -; Owner: -
--


--
-- TOC entry 3 (class 3079 OID 106370)
-- Name: uuid-ossp; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS "uuid-ossp" WITH SCHEMA public;

--
-- TOC entry 6128 (class 0 OID 0)
-- Dependencies: 3
-- Name: EXTENSION "uuid-ossp"; Type: COMMENT; Schema: -; Owner: -
--


--
-- TOC entry 1157 (class 1247 OID 107369)
-- Name: attribute_control_type; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.attribute_control_type AS ENUM (
    'dropdown_list',
    'radio_list',
    'checkboxes',
    'text_box',
    'multiline_text_box',
    'datepicker'
);

--
-- TOC entry 1247 (class 1247 OID 108046)
-- Name: commission_type; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.commission_type AS ENUM (
    'percent',
    'fixed'
);

--
-- TOC entry 1002 (class 1247 OID 106133)
-- Name: content_status; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.content_status AS ENUM (
    'published',
    'draft',
    'scheduled',
    'archived'
);

--
-- TOC entry 1005 (class 1247 OID 106142)
-- Name: course_level; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.course_level AS ENUM (
    'ca_foundation',
    'ca_intermediate',
    'books',
    'test_series'
);

--
-- TOC entry 1008 (class 1247 OID 106152)
-- Name: course_type; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.course_type AS ENUM (
    'regular',
    'fastrack',
    'combo',
    'face_to_face',
    'exam_oriented'
);

--
-- TOC entry 1259 (class 1247 OID 108138)
-- Name: customer_type; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.customer_type AS ENUM (
    'individual',
    'organization'
);

--
-- TOC entry 1244 (class 1247 OID 108035)
-- Name: franchise_status; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.franchise_status AS ENUM (
    'approved',
    'pending',
    'rejected'
);

--
-- TOC entry 1011 (class 1247 OID 106164)
-- Name: lead_status; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.lead_status AS ENUM (
    'new',
    'contacted',
    'follow_up',
    'converted',
    'lost'
);

--
-- TOC entry 1014 (class 1247 OID 106176)
-- Name: lecture_mode; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.lecture_mode AS ENUM (
    'live_streaming',
    'recorded',
    'live_plus_recorded',
    'pendrive',
    'face_to_face'
);

--
-- TOC entry 1154 (class 1247 OID 107347)
-- Name: order_source; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.order_source AS ENUM (
    'website',
    'counter',
    'franchisee',
    'other'
);

--
-- TOC entry 1017 (class 1247 OID 106202)
-- Name: order_status; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.order_status AS ENUM (
    'draft',
    'pending',
    'confirmed',
    'processing',
    'activated',
    'delivered',
    'cancelled',
    'refunded'
);

--
-- TOC entry 1020 (class 1247 OID 106220)
-- Name: payment_mode; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.payment_mode AS ENUM (
    'razorpay',
    'easebuzz',
    'ccavenue',
    'upi',
    'bank_transfer',
    'cash',
    'cheque',
    'razorpay_link',
    'emi'
);

--
-- TOC entry 1023 (class 1247 OID 106240)
-- Name: payment_status; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.payment_status AS ENUM (
    'pending',
    'success',
    'failed',
    'refunded',
    'partial_refund'
);

--
-- TOC entry 1026 (class 1247 OID 106252)
-- Name: product_status; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.product_status AS ENUM (
    'active',
    'draft',
    'archived'
);

--
-- TOC entry 1109 (class 1247 OID 107019)
-- Name: review_status; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.review_status AS ENUM (
    'pending',
    'approved',
    'rejected'
);

--
-- TOC entry 1029 (class 1247 OID 106260)
-- Name: sharing_type; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.sharing_type AS ENUM (
    'percentage',
    'fixed_amount'
);

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- TOC entry 223 (class 1259 OID 106393)
-- Name: Banners; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Banners" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Title" text,
    "ImageUrl" text NOT NULL,
    "LinkUrl" text,
    "Placement" text NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 229 (class 1259 OID 106503)
-- Name: BlogPosts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."BlogPosts" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Title" text NOT NULL,
    "Slug" text NOT NULL,
    "Excerpt" text,
    "Body" text NOT NULL,
    "FeaturedImage" text,
    "Category" text,
    "AuthorId" uuid,
    "Status" public.content_status NOT NULL,
    "PublishedAt" timestamp with time zone,
    "SeoTitle" text,
    "SeoDescription" text,
    "Views" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 301 (class 1259 OID 108350)
-- Name: BookPreviewPages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."BookPreviewPages" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "BookPreviewId" uuid NOT NULL,
    "PageNumber" integer NOT NULL,
    "ImageRelativePath" text NOT NULL,
    "Width" integer NOT NULL,
    "Height" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 245 (class 1259 OID 106913)
-- Name: CartItems; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."CartItems" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "UserId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "ProductModeId" uuid,
    "Quantity" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "AttributePriceAdjustment" numeric(10,2) DEFAULT 0.0 NOT NULL,
    "SelectedAttributesJson" text
);

--
-- TOC entry 224 (class 1259 OID 106409)
-- Name: Categories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Categories" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Name" text NOT NULL,
    "Slug" text NOT NULL,
    "ParentId" uuid,
    "Description" text,
    "ImageUrl" text,
    "DisplayOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "IncludeInTopMenu" boolean DEFAULT false NOT NULL,
    "SeoDescription" text,
    "SeoKeywords" text,
    "SeoTitle" text,
    "ShowOnHomePage" boolean DEFAULT false NOT NULL
);

--
-- TOC entry 225 (class 1259 OID 106430)
-- Name: Coupons; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Coupons" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Code" text NOT NULL,
    "Name" text,
    "CouponType" public.sharing_type NOT NULL,
    "Value" numeric NOT NULL,
    "MaxDiscount" numeric,
    "MinOrder" numeric NOT NULL,
    "TotalLimit" integer,
    "PerUserLimit" integer NOT NULL,
    "TotalUsed" integer NOT NULL,
    "StartsAt" timestamp with time zone,
    "ExpiresAt" timestamp with time zone,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "AffiliateId" uuid
);

--
-- TOC entry 261 (class 1259 OID 107338)
-- Name: DataProtectionKeys; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."DataProtectionKeys" (
    "Id" integer NOT NULL,
    "FriendlyName" text,
    "Xml" text
);

--
-- TOC entry 260 (class 1259 OID 107337)
-- Name: DataProtectionKeys_Id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public."DataProtectionKeys" ALTER COLUMN "Id" ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public."DataProtectionKeys_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);

--
-- TOC entry 236 (class 1259 OID 106706)
-- Name: Enrollments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Enrollments" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "UserId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "OrderItemId" uuid,
    "Mode" public.lecture_mode,
    "IsActive" boolean NOT NULL,
    "ProgressPct" numeric NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 237 (class 1259 OID 106732)
-- Name: FacultySharingRules; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."FacultySharingRules" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ProductId" uuid NOT NULL,
    "FacultyId" uuid NOT NULL,
    "ShareType" public.sharing_type NOT NULL,
    "ShareValue" numeric NOT NULL,
    "EffectiveAmount" numeric,
    "IsActive" boolean NOT NULL,
    "Notes" text,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "EffectiveFrom" timestamp with time zone
);

--
-- TOC entry 290 (class 1259 OID 108051)
-- Name: FranchiseCommissionEntries; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."FranchiseCommissionEntries" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "FranchiseId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "OrderNumber" text NOT NULL,
    "OrderItemId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "ProductTitle" text NOT NULL,
    "BaseAmount" numeric NOT NULL,
    "Type" public.commission_type NOT NULL,
    "Value" numeric NOT NULL,
    "CommissionAmount" numeric NOT NULL,
    "EarnedAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 291 (class 1259 OID 108080)
-- Name: FranchiseCommissions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."FranchiseCommissions" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "FranchiseId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "Type" public.commission_type NOT NULL,
    "Value" numeric NOT NULL,
    "EffectiveFrom" timestamp with time zone NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 231 (class 1259 OID 106547)
-- Name: Franchises; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Franchises" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Name" text NOT NULL,
    "Code" text NOT NULL,
    "City" text NOT NULL,
    "ContactPerson" text,
    "ContactPhone" text,
    "WalletBalance" numeric NOT NULL,
    "AdminUserId" uuid,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "CreditLimit" numeric DEFAULT 0.0 NOT NULL,
    "AddressLine" text,
    "ApprovalRemarks" text,
    "ApprovedAt" timestamp with time zone,
    "ApprovedById" uuid,
    "BusinessName" text,
    "ContactEmail" text,
    "DocumentUrls" text,
    "Gstin" text,
    "Pan" text,
    "PinCode" text,
    "RegisteredAt" timestamp with time zone DEFAULT now() NOT NULL,
    "RejectionRemarks" text,
    "State" text,
    "Status" public.franchise_status DEFAULT 'approved'::public.franchise_status NOT NULL
);

--
-- TOC entry 243 (class 1259 OID 106866)
-- Name: Invoices; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Invoices" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "OrderId" uuid NOT NULL,
    "InvoiceNumber" text NOT NULL,
    "InvoiceDate" date NOT NULL,
    "TaxableAmount" numeric NOT NULL,
    "CgstAmount" numeric NOT NULL,
    "SgstAmount" numeric NOT NULL,
    "IgstAmount" numeric NOT NULL,
    "TotalAmount" numeric NOT NULL,
    "PdfUrl" text,
    "IsCancelled" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 232 (class 1259 OID 106569)
-- Name: Leads; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Leads" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "FullName" text NOT NULL,
    "Phone" text NOT NULL,
    "City" text,
    "CourseInterest" text,
    "Source" text,
    "Status" public.lead_status NOT NULL,
    "AssignedToId" uuid,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "Message" text
);

--
-- TOC entry 246 (class 1259 OID 106941)
-- Name: OrderItems; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."OrderItems" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "OrderId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "ProductModeId" uuid,
    "ProductTitle" text NOT NULL,
    "ModeName" text,
    "Quantity" integer NOT NULL,
    "UnitPrice" numeric NOT NULL,
    "Discount" numeric NOT NULL,
    "GstRate" numeric,
    "GstAmount" numeric NOT NULL,
    "LineTotal" numeric NOT NULL,
    "Attempt" text,
    "IsActivated" boolean NOT NULL,
    "ActivatedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "SelectedAttributesJson" text
);

--
-- TOC entry 244 (class 1259 OID 106892)
-- Name: Payments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Payments" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "OrderId" uuid NOT NULL,
    "Amount" numeric NOT NULL,
    "PaymentMode" public.payment_mode NOT NULL,
    "Status" public.payment_status NOT NULL,
    "GatewayName" text,
    "GatewayOrderId" text,
    "GatewayPaymentId" text,
    "GatewaySignature" text,
    "BankRef" text,
    "PaidAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 300 (class 1259 OID 108326)
-- Name: ProductBookPreviews; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ProductBookPreviews" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ProductId" uuid NOT NULL,
    "Title" text NOT NULL,
    "Description" text,
    "PdfRelativePath" text NOT NULL,
    "CoverImageUrl" text,
    "IsEnabled" boolean NOT NULL,
    "MaxPagesAllowed" integer,
    "DisplayOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 238 (class 1259 OID 106759)
-- Name: ProductFaculty; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ProductFaculty" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ProductId" uuid NOT NULL,
    "FacultyId" uuid NOT NULL,
    "IsPrimary" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 239 (class 1259 OID 106782)
-- Name: ProductImages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ProductImages" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ProductId" uuid NOT NULL,
    "ImageUrl" text NOT NULL,
    "AltText" text,
    "DisplayOrder" integer NOT NULL,
    "IsPrimary" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "Title" text
);

--
-- TOC entry 240 (class 1259 OID 106803)
-- Name: ProductInclusions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ProductInclusions" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ProductId" uuid NOT NULL,
    "Icon" text,
    "Title" text NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 241 (class 1259 OID 106823)
-- Name: ProductModes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ProductModes" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ProductId" uuid NOT NULL,
    "ModeName" text NOT NULL,
    "ModeType" public.lecture_mode NOT NULL,
    "Price" numeric NOT NULL,
    "IsEnabled" boolean NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 302 (class 1259 OID 108374)
-- Name: ProductTestimonials; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ProductTestimonials" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ProductId" uuid NOT NULL,
    "StudentName" text NOT NULL,
    "CourseName" text,
    "YoutubeUrl" text NOT NULL,
    "ThumbnailUrl" text,
    "DisplayOrder" integer NOT NULL,
    "IsFeatured" boolean NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 226 (class 1259 OID 106449)
-- Name: Roles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Roles" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Name" text NOT NULL,
    "DisplayName" text NOT NULL,
    "Description" text,
    "IsSystem" boolean NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 299 (class 1259 OID 108298)
-- Name: ScheduledTaskRuns; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ScheduledTaskRuns" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ScheduledTaskId" uuid NOT NULL,
    "Trigger" character varying(20) NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "CompletedAt" timestamp with time zone,
    "DurationMs" integer NOT NULL,
    "Status" integer NOT NULL,
    "ItemsChecked" integer NOT NULL,
    "ItemsUpdated" integer NOT NULL,
    "Output" character varying(800),
    "Error" character varying(800),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 298 (class 1259 OID 108275)
-- Name: ScheduledTasks; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."ScheduledTasks" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TaskKey" character varying(80) NOT NULL,
    "Name" character varying(160) NOT NULL,
    "Description" character varying(500),
    "IntervalSeconds" integer NOT NULL,
    "Enabled" boolean NOT NULL,
    "StopOnError" boolean NOT NULL,
    "TimeoutSeconds" integer NOT NULL,
    "LastRunAt" timestamp with time zone,
    "LastSuccessAt" timestamp with time zone,
    "NextRunAt" timestamp with time zone,
    "LastStatus" integer NOT NULL,
    "LastError" character varying(800),
    "LastDurationMs" integer NOT NULL,
    "LastItemsChecked" integer NOT NULL,
    "LastItemsUpdated" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 227 (class 1259 OID 106465)
-- Name: Subjects; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Subjects" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Name" text NOT NULL,
    "Slug" text NOT NULL,
    "Level" public.course_level,
    "DisplayOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 242 (class 1259 OID 106846)
-- Name: Testimonials; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Testimonials" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "StudentName" text NOT NULL,
    "Location" text,
    "ScoreText" text,
    "TextContent" text,
    "Rating" integer,
    "ProductId" uuid,
    "DisplayOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 235 (class 1259 OID 106678)
-- Name: UserRoles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."UserRoles" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "UserId" uuid NOT NULL,
    "RoleId" uuid NOT NULL,
    "FranchiseId" uuid,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 292 (class 1259 OID 108112)
-- Name: WalletRecharges; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."WalletRecharges" (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "FranchiseId" uuid NOT NULL,
    "Amount" numeric NOT NULL,
    "Status" integer NOT NULL,
    "GatewayName" text NOT NULL,
    "GatewayOrderId" text NOT NULL,
    "GatewayPaymentId" text,
    "GatewaySignature" text,
    "CompletedAt" timestamp with time zone,
    "FailureReason" text,
    "LedgerEntryId" uuid,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 221 (class 1259 OID 106125)
-- Name: __EFMigrationsHistory; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL
);

--
-- TOC entry 273 (class 1259 OID 107625)
-- Name: admin_notifications; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.admin_notifications (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Type" integer NOT NULL,
    "Severity" integer NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Message" character varying(1000) NOT NULL,
    "LinkUrl" character varying(400),
    "EntityId" text,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 250 (class 1259 OID 107133)
-- Name: affiliate_referrals; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.affiliate_referrals (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "AffiliateId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "OrderNumber" character varying(20) NOT NULL,
    "OrderAmount" numeric(12,2) NOT NULL,
    "Commission" numeric(12,2) NOT NULL,
    "IsPaid" boolean NOT NULL,
    "PaidAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 249 (class 1259 OID 107107)
-- Name: affiliates; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.affiliates (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "UserId" uuid,
    "Name" character varying(200) NOT NULL,
    "Email" text,
    "Phone" text,
    "Code" character varying(40) NOT NULL,
    "CommissionType" public.sharing_type NOT NULL,
    "CommissionValue" numeric(12,2) NOT NULL,
    "IsActive" boolean NOT NULL,
    "TotalReferrals" integer NOT NULL,
    "TotalEarned" numeric(12,2) NOT NULL,
    "TotalPaid" numeric(12,2) NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 308 (class 1259 OID 108543)
-- Name: app_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.app_logs (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Level" integer NOT NULL,
    "Category" character varying(120) NOT NULL,
    "EventCode" character varying(80),
    "Message" character varying(2000) NOT NULL,
    "Exception" text,
    "Properties" jsonb,
    "UserId" uuid,
    "OrderId" uuid,
    "EntityType" character varying(60),
    "EntityId" character varying(80),
    "RequestPath" character varying(500),
    "Source" character varying(60),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 222 (class 1259 OID 106381)
-- Name: app_settings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.app_settings (
    "Key" character varying(100) NOT NULL,
    "Value" text NOT NULL,
    "ValueType" text NOT NULL,
    "Category" text,
    "Description" text,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 280 (class 1259 OID 107787)
-- Name: approval_comments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.approval_comments (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ApprovalRequestId" uuid NOT NULL,
    "AuthorId" uuid,
    "AuthorName" character varying(200) NOT NULL,
    "Body" character varying(2000) NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 279 (class 1259 OID 107767)
-- Name: approval_requests; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.approval_requests (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Type" integer NOT NULL,
    "Status" integer NOT NULL,
    "Title" character varying(300) NOT NULL,
    "Description" text,
    "Amount" numeric(12,2),
    "RelatedEntityType" character varying(60) NOT NULL,
    "RelatedEntityId" uuid,
    "CurrentStep" integer NOT NULL,
    "TotalSteps" integer NOT NULL,
    "RequestedById" uuid,
    "RequestedByName" character varying(200) NOT NULL,
    "DecidedById" uuid,
    "DecidedByName" character varying(200),
    "DecidedAt" timestamp with time zone,
    "DecisionNotes" text,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 281 (class 1259 OID 107808)
-- Name: approval_steps; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.approval_steps (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ApprovalRequestId" uuid NOT NULL,
    "StepOrder" integer NOT NULL,
    "ApproverRole" character varying(60),
    "Status" integer NOT NULL,
    "DecidedByName" text,
    "DecidedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 259 (class 1259 OID 107320)
-- Name: audit_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.audit_logs (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ActorUserId" uuid,
    "ActorName" character varying(200) NOT NULL,
    "Action" character varying(80) NOT NULL,
    "EntityType" character varying(60) NOT NULL,
    "EntityId" character varying(60),
    "Details" text,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "Browser" character varying(40),
    "Device" character varying(40),
    "EntityName" character varying(200),
    "IpAddress" character varying(64),
    "Module" character varying(40),
    "NewValue" character varying(1000),
    "OldValue" character varying(1000),
    "Status" character varying(20) DEFAULT ''::character varying NOT NULL,
    "UserAgent" character varying(400)
);

--
-- TOC entry 251 (class 1259 OID 107164)
-- Name: campaigns; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.campaigns (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Name" character varying(160) NOT NULL,
    "Subject" character varying(200) NOT NULL,
    "Body" character varying(8000) NOT NULL,
    "IsSent" boolean NOT NULL,
    "RecipientCount" integer NOT NULL,
    "SentAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 265 (class 1259 OID 107426)
-- Name: checkout_attribute_values; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.checkout_attribute_values (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "CheckoutAttributeId" uuid NOT NULL,
    "Name" character varying(400) NOT NULL,
    "PriceAdjustment" numeric(10,2) NOT NULL,
    "PriceAdjustmentUsePercentage" boolean NOT NULL,
    "IsPreSelected" boolean NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 262 (class 1259 OID 107381)
-- Name: checkout_attributes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.checkout_attributes (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Name" character varying(400) NOT NULL,
    "TextPrompt" character varying(400),
    "IsRequired" boolean NOT NULL,
    "ControlType" public.attribute_control_type NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 253 (class 1259 OID 107196)
-- Name: cms_pages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cms_pages (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Title" character varying(200) NOT NULL,
    "Slug" character varying(160) NOT NULL,
    "Body" character varying(20000) NOT NULL,
    "IsPublished" boolean NOT NULL,
    "SeoTitle" text,
    "SeoDescription" text,
    "DisplayOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 288 (class 1259 OID 107989)
-- Name: customer_addresses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customer_addresses (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "UserId" uuid NOT NULL,
    "FirstName" character varying(100),
    "LastName" character varying(100),
    "Email" character varying(200),
    "Phone" character varying(30),
    "FaxNumber" character varying(30),
    "Address1" character varying(300),
    "City" character varying(100),
    "State" character varying(100),
    "Pincode" character varying(20),
    "Country" character varying(100) NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 230 (class 1259 OID 106525)
-- Name: faculty; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.faculty (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "UserId" uuid,
    "DisplayName" character varying(200) NOT NULL,
    "ShortCode" character varying(10) NOT NULL,
    "Designation" text,
    "Qualifications" text,
    "Subjects" text[],
    "Bio" text,
    "PhotoUrl" text,
    "YoutubeUrl" text,
    "WhatsappNumber" text,
    "BankName" text,
    "BankAccount" text,
    "BankIfsc" text,
    "PanNumber" text,
    "Gstin" text,
    "GstRegistered" boolean NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "AirHoldersNote" text,
    "CallNumber" text,
    "HoursOfTeaching" text,
    "StudentSatisfaction" text,
    "StudentsTaught" text,
    "YearsOfExperience" text,
    "ShowOnHomePage" boolean DEFAULT false NOT NULL,
    "ShortDescription" text
);

--
-- TOC entry 256 (class 1259 OID 107254)
-- Name: franchise_ledger; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.franchise_ledger (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "FranchiseId" uuid NOT NULL,
    "IsCredit" boolean NOT NULL,
    "Amount" numeric(12,2) NOT NULL,
    "BalanceAfter" numeric(12,2) NOT NULL,
    "Description" character varying(300),
    "OrderId" uuid,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 310 (class 1259 OID 108616)
-- Name: invoice_line_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.invoice_line_items (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "InvoiceId" uuid NOT NULL,
    "OrderItemId" uuid,
    "ProductId" uuid,
    "LineNumber" integer NOT NULL,
    "Description" character varying(500) NOT NULL,
    "ModeName" character varying(120),
    "HsnCode" character varying(20),
    "Quantity" integer DEFAULT 1 NOT NULL,
    "UnitPrice" numeric(18,2) DEFAULT 0 NOT NULL,
    "Discount" numeric(18,2) DEFAULT 0 NOT NULL,
    "GstRate" numeric(5,2),
    "GstAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "LineTotal" numeric(18,2) DEFAULT 0 NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 309 (class 1259 OID 108568)
-- Name: invoices; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.invoices (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "InvoiceNumber" character varying(40) NOT NULL,
    "InvoiceDate" date DEFAULT now() NOT NULL,
    "OrderId" uuid NOT NULL,
    "OrderNumber" character varying(40),
    "CustomerName" character varying(200),
    "CustomerEmail" character varying(200),
    "CustomerPhone" character varying(40),
    "BillingAddress" character varying(500),
    "BillingCity" character varying(120),
    "BillingState" character varying(120),
    "BillingPincode" character varying(20),
    "CustomerGstin" character varying(20),
    "GstClassification" character varying(10) DEFAULT 'B2C'::character varying NOT NULL,
    "Subtotal" numeric(18,2) DEFAULT 0 NOT NULL,
    "DiscountAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "CgstAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "SgstAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "IgstAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "ShippingCharges" numeric(18,2) DEFAULT 0 NOT NULL,
    "TotalAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "Currency" character varying(8) DEFAULT 'INR'::character varying NOT NULL,
    "PaymentReference" character varying(120),
    "PaidAt" timestamp with time zone,
    "Status" integer DEFAULT 0 NOT NULL,
    "CancelledAt" timestamp with time zone,
    "CancelledReason" character varying(500),
    "GeneratedByUserId" uuid,
    "GeneratedByName" character varying(200),
    "Notes" character varying(2000),
    "CompanyName" character varying(200),
    "CompanyGstin" character varying(20),
    "CompanyAddress" character varying(500),
    "CompanyPhone" character varying(40),
    "CompanyEmail" character varying(200),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "TaxableAmount" numeric(18,2) DEFAULT 0 NOT NULL,
    "PdfUrl" character varying(500),
    "PaymentMode" public.payment_mode
);

--
-- TOC entry 289 (class 1259 OID 108010)
-- Name: menu_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.menu_items (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ParentId" uuid,
    "Title" character varying(200) NOT NULL,
    "Url" character varying(500) NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "IsEnabled" boolean NOT NULL,
    "OpenInNewTab" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 254 (class 1259 OID 107215)
-- Name: message_templates; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.message_templates (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Key" character varying(60) NOT NULL,
    "Name" character varying(160) NOT NULL,
    "Channel" character varying(20) NOT NULL,
    "Subject" character varying(300) NOT NULL,
    "Body" character varying(8000) NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 252 (class 1259 OID 107182)
-- Name: newsletter_subscribers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.newsletter_subscribers (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Email" character varying(200) NOT NULL,
    "Name" character varying(200),
    "Source" character varying(60),
    "IsActive" boolean NOT NULL,
    "UnsubscribedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 278 (class 1259 OID 107737)
-- Name: note_attachments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.note_attachments (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "OrderNoteId" uuid NOT NULL,
    "OrderId" uuid NOT NULL,
    "FileName" character varying(120) NOT NULL,
    "OriginalFileName" character varying(300) NOT NULL,
    "ContentType" character varying(150) NOT NULL,
    "FileSize" bigint NOT NULL,
    "StoragePath" character varying(500) NOT NULL,
    "HashChecksum" character varying(80),
    "VirusScanStatus" character varying(30) NOT NULL,
    "IsDeleted" boolean NOT NULL,
    "UploadedByName" text NOT NULL,
    "UploadedById" uuid,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 255 (class 1259 OID 107234)
-- Name: notification_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.notification_logs (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "TemplateKey" character varying(60),
    "Channel" character varying(20) NOT NULL,
    "Recipient" character varying(200) NOT NULL,
    "Subject" character varying(300),
    "Body" character varying(8000),
    "Status" character varying(20) NOT NULL,
    "Error" character varying(500),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "IsHtml" boolean DEFAULT false NOT NULL,
    "Provider" text,
    "Response" text,
    "DurationMs" bigint,
    "TriggeredBy" text,
    "RequestPayload" text,
    "RetryCount" integer DEFAULT 0 NOT NULL,
    "OriginalLogId" uuid
);

--
-- TOC entry 272 (class 1259 OID 107600)
-- Name: order_notes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.order_notes (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "OrderId" uuid NOT NULL,
    "Body" character varying(4000) NOT NULL,
    "IsCustomerVisible" boolean NOT NULL,
    "IsPinned" boolean NOT NULL,
    "CreatedByUserId" uuid,
    "CreatedByName" character varying(200) NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 234 (class 1259 OID 106632)
-- Name: orders; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.orders (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "OrderNumber" character varying(20) NOT NULL,
    "UserId" uuid,
    "StudentName" character varying(200) NOT NULL,
    "StudentPhone" character varying(15) NOT NULL,
    "StudentEmail" text,
    "StudentCity" text,
    "Source" public.order_source NOT NULL,
    "FranchiseId" uuid,
    "CreatedById" uuid,
    "Subtotal" numeric(12,2) NOT NULL,
    "DiscountAmount" numeric(12,2) NOT NULL,
    "CouponId" uuid,
    "CouponCode" text,
    "GstAmount" numeric(12,2) NOT NULL,
    "CgstAmount" numeric(10,2) NOT NULL,
    "SgstAmount" numeric(10,2) NOT NULL,
    "IgstAmount" numeric(10,2) NOT NULL,
    "TotalAmount" numeric(12,2) NOT NULL,
    "BillingName" text,
    "BillingAddress" text,
    "BillingCity" text,
    "BillingState" text,
    "BillingPincode" text,
    "GstNumber" text,
    "GstClassification" text NOT NULL,
    "Status" public.order_status NOT NULL,
    "PaymentStatus" public.payment_status NOT NULL,
    "PaymentMode" public.payment_mode,
    "InternalNotes" text,
    "ConfirmedAt" timestamp with time zone,
    "ActivatedAt" timestamp with time zone,
    "CancelledAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "AffiliateId" uuid,
    "CheckoutAttributesAmount" numeric(10,2) DEFAULT 0.0 NOT NULL,
    "CheckoutAttributesJson" text,
    "DeletedAt" timestamp with time zone,
    "IsDeleted" boolean DEFAULT false NOT NULL,
    "CustomerNotes" text,
    "CustomerType" public.customer_type DEFAULT 'individual'::public.customer_type NOT NULL,
    "OrgName" text,
    "ShippingAddress" text,
    "ShippingCharges" numeric DEFAULT 0.0 NOT NULL,
    "ShippingCity" text,
    "ShippingPincode" text,
    "ShippingState" text,
    "ReferralCustomText" text,
    "ReferralSourceId" uuid,
    "ReferralSourceName" text,
    "ReferralType" integer
);

--
-- TOC entry 275 (class 1259 OID 107660)
-- Name: payment_transactions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payment_transactions (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "OrderId" uuid NOT NULL,
    "Gateway" character varying(60) NOT NULL,
    "GatewayTransactionId" character varying(200),
    "Status" public.payment_status NOT NULL,
    "Amount" numeric(12,2) NOT NULL,
    "Currency" character varying(8) NOT NULL,
    "ResponseCode" text,
    "ResponseMessage" text,
    "RawResponseJson" text,
    "PaymentMethod" public.payment_mode NOT NULL,
    "IsRefund" boolean NOT NULL,
    "ParentTransactionId" uuid,
    "PaidOnUtc" timestamp with time zone,
    "Reference" character varying(200),
    "CreatedByName" text NOT NULL,
    "CreatedById" uuid,
    "Notes" text,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 285 (class 1259 OID 107910)
-- Name: payout_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payout_items (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "PayoutId" uuid NOT NULL,
    "Source" integer NOT NULL,
    "OrderId" uuid,
    "OrderNumber" character varying(40),
    "OrderItemId" uuid,
    "RefundId" uuid,
    "SharingRuleId" uuid,
    "Description" character varying(300) NOT NULL,
    "Amount" numeric(12,2) NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 283 (class 1259 OID 107858)
-- Name: payouts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payouts (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "BeneficiaryType" integer NOT NULL,
    "BeneficiaryId" uuid NOT NULL,
    "BeneficiaryName" character varying(200) NOT NULL,
    "GrossAmount" numeric(12,2) NOT NULL,
    "Adjustments" numeric(12,2) NOT NULL,
    "RefundAdjustments" numeric(12,2) NOT NULL,
    "TaxDeduction" numeric(12,2) NOT NULL,
    "NetAmount" numeric(12,2) NOT NULL,
    "Status" integer NOT NULL,
    "SettlementBatchId" uuid,
    "PeriodStartUtc" timestamp with time zone NOT NULL,
    "PeriodEndUtc" timestamp with time zone NOT NULL,
    "ProcessedOnUtc" timestamp with time zone,
    "PaidOnUtc" timestamp with time zone,
    "PaymentReference" character varying(120),
    "Provider" character varying(40),
    "Notes" text,
    "ApprovedById" uuid,
    "ApprovedByName" text,
    "ApprovedOnUtc" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 297 (class 1259 OID 108247)
-- Name: permission_audit_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.permission_audit_logs (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "UserId" uuid NOT NULL,
    "PermissionKey" character varying(120) NOT NULL,
    "OldValue" boolean,
    "NewValue" boolean,
    "ChangedById" uuid,
    "ChangedByName" character varying(150) NOT NULL,
    "Action" character varying(40) NOT NULL,
    "IpAddress" character varying(64),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 266 (class 1259 OID 107448)
-- Name: predefined_product_attribute_values; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.predefined_product_attribute_values (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ProductAttributeId" uuid NOT NULL,
    "Name" character varying(400) NOT NULL,
    "PriceAdjustment" numeric(10,2) NOT NULL,
    "PriceAdjustmentUsePercentage" boolean CONSTRAINT "predefined_product_attribut_PriceAdjustmentUsePercenta_not_null" NOT NULL,
    "IsPreSelected" boolean NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 267 (class 1259 OID 107470)
-- Name: product_attribute_mappings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.product_attribute_mappings (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ProductId" uuid NOT NULL,
    "ProductAttributeId" uuid NOT NULL,
    "TextPrompt" character varying(400),
    "IsRequired" boolean NOT NULL,
    "ControlType" public.attribute_control_type NOT NULL,
    "DefaultValue" character varying(1000),
    "DisplayOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 269 (class 1259 OID 107516)
-- Name: product_attribute_values; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.product_attribute_values (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ProductAttributeMappingId" uuid NOT NULL,
    "Name" character varying(400) NOT NULL,
    "PriceAdjustment" numeric(10,2) NOT NULL,
    "PriceAdjustmentUsePercentage" boolean NOT NULL,
    "IsPreSelected" boolean NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 263 (class 1259 OID 107399)
-- Name: product_attributes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.product_attributes (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Name" character varying(400) NOT NULL,
    "Description" character varying(2000),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 303 (class 1259 OID 108399)
-- Name: product_categories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.product_categories (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ProductId" uuid NOT NULL,
    "CategoryId" uuid NOT NULL,
    "IsPrimary" boolean NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 294 (class 1259 OID 108171)
-- Name: product_recommendations; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.product_recommendations (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ProductId" uuid NOT NULL,
    "RecommendedProductId" uuid NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "CustomTitle" character varying(120),
    "Type" integer NOT NULL,
    "BadgeText" character varying(40),
    "BadgeColor" character varying(20),
    "Priority" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedById" uuid,
    "UpdatedById" uuid,
    "IsDeleted" boolean NOT NULL,
    "DeletedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 305 (class 1259 OID 108448)
-- Name: product_serial_key_configs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.product_serial_key_configs (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ProductId" uuid NOT NULL,
    "ProviderKey" character varying(40) NOT NULL,
    "TenantId" uuid,
    "ProviderProductCode" character varying(64),
    "ConfigJson" jsonb NOT NULL,
    "AutoActivate" boolean NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 271 (class 1259 OID 107557)
-- Name: product_specification_attributes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.product_specification_attributes (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ProductId" uuid NOT NULL,
    "SpecificationAttributeOptionId" uuid CONSTRAINT "product_specification_attri_SpecificationAttributeOpti_not_null" NOT NULL,
    "AllowFiltering" boolean NOT NULL,
    "ShowOnProductPage" boolean NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 287 (class 1259 OID 107963)
-- Name: product_videos; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.product_videos (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ProductId" uuid NOT NULL,
    "YoutubeUrl" character varying(500) NOT NULL,
    "Title" character varying(200),
    "DisplayOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 233 (class 1259 OID 106589)
-- Name: products; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.products (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Title" character varying(500) NOT NULL,
    "Slug" character varying(300) NOT NULL,
    "ShortDesc" text,
    "FullDesc" text,
    "Level" public.course_level NOT NULL,
    "CourseType" public.course_type NOT NULL,
    "CategoryId" uuid,
    "SubjectId" uuid,
    "PrimaryFacultyId" uuid,
    "Mrp" numeric(10,2) NOT NULL,
    "SellingPrice" numeric(10,2) NOT NULL,
    "FranchisePrice" numeric(10,2),
    "GstRate" numeric(4,2) NOT NULL,
    "GstInclusive" boolean NOT NULL,
    "SacCode" text NOT NULL,
    "BatchStartDate" date,
    "ApplicableAttempts" text[],
    "TotalLectures" text,
    "TotalHours" text,
    "BooksInfo" text,
    "ExamOrientedInfo" text,
    "AdditionalDetails" text,
    "SeoTitle" text,
    "SeoDescription" text,
    "Badge" text,
    "IsFeatured" boolean NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "Status" public.product_status NOT NULL,
    "TotalOrders" integer NOT NULL,
    "TotalViews" integer NOT NULL,
    "AvgRating" numeric(2,1) NOT NULL,
    "RatingCount" integer NOT NULL,
    "PublishedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "Sku" text,
    "AdminComment" text,
    "AllowReviews" boolean DEFAULT true NOT NULL,
    "AvailableEndUtc" timestamp with time zone,
    "AvailableStartUtc" timestamp with time zone,
    "Gtin" text,
    "MarkAsNew" boolean DEFAULT false NOT NULL,
    "ProductCost" numeric DEFAULT 0.0 NOT NULL,
    "Tags" text,
    "BookPreviewPdfUrl" text,
    "FaqsJson" text,
    "LecturesVideoUrl" text,
    "RelatedProductSlugs" text,
    "TestimonialVideoUrls" text,
    "HomeCardImageUrl" text,
    "OfferImageUrl" text,
    "BatchStatus" integer DEFAULT 0 NOT NULL,
    "EstimatedDeliveryMessage" text,
    "LectureAccessTiming" text DEFAULT 'Within 24 Hours'::text NOT NULL,
    "NotesDispatchTimeline" text DEFAULT 'Within 48 Hours'::text NOT NULL,
    "SpecialPrice" numeric(10,2),
    "SpecialPriceEndDateUtc" timestamp with time zone,
    "SpecialPriceStartDateUtc" timestamp with time zone
);

--
-- TOC entry 293 (class 1259 OID 108150)
-- Name: referral_sources; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.referral_sources (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Name" character varying(120) NOT NULL,
    "Type" integer NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL,
    "IsDefault" boolean NOT NULL,
    "ColorBadge" character varying(20),
    "Icon" character varying(40),
    "Description" character varying(500),
    "CreatedById" uuid,
    "UpdatedById" uuid,
    "IsDeleted" boolean NOT NULL,
    "DeletedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 277 (class 1259 OID 107710)
-- Name: refund_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.refund_items (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "RefundId" uuid NOT NULL,
    "OrderItemId" uuid NOT NULL,
    "ProductTitle" character varying(500) NOT NULL,
    "Quantity" integer NOT NULL,
    "Amount" numeric(12,2) NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 276 (class 1259 OID 107686)
-- Name: refunds; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.refunds (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "OrderId" uuid NOT NULL,
    "Amount" numeric(12,2) NOT NULL,
    "Reason" character varying(1000),
    "Status" integer NOT NULL,
    "RefundType" character varying(20) NOT NULL,
    "IsOffline" boolean NOT NULL,
    "Gateway" text,
    "GatewayRefundId" text,
    "ResponseMessage" text,
    "FailureReason" text,
    "PaymentTransactionId" uuid,
    "InitiatedByName" text NOT NULL,
    "InitiatedById" uuid,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 257 (class 1259 OID 107275)
-- Name: return_requests; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.return_requests (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "OrderId" uuid NOT NULL,
    "OrderNumber" character varying(20) NOT NULL,
    "Reason" character varying(500) NOT NULL,
    "RefundAmount" numeric(12,2) NOT NULL,
    "Status" character varying(20) NOT NULL,
    "ResolvedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 247 (class 1259 OID 107049)
-- Name: reviews; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.reviews (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ProductId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Rating" integer NOT NULL,
    "Title" character varying(160),
    "Comment" character varying(2000) NOT NULL,
    "AuthorName" character varying(200) NOT NULL,
    "IsVerifiedPurchase" boolean NOT NULL,
    "Status" public.review_status NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 306 (class 1259 OID 108471)
-- Name: rioplay_tenants; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.rioplay_tenants (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Name" character varying(120) NOT NULL,
    "TenantId" integer NOT NULL,
    "SecretEncrypted" character varying(4000) NOT NULL,
    "BaseUrl" character varying(300) NOT NULL,
    "IsActive" boolean NOT NULL,
    "IsDefault" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 295 (class 1259 OID 108208)
-- Name: role_permissions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.role_permissions (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "RoleId" uuid NOT NULL,
    "PermissionKey" character varying(120) NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 307 (class 1259 OID 108490)
-- Name: serial_key_records; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.serial_key_records (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "OrderId" uuid NOT NULL,
    "OrderItemId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "UserId" uuid,
    "ProviderKey" character varying(40) NOT NULL,
    "TenantRef" character varying(64),
    "SerialKey" character varying(256),
    "ExternalReference" character varying(128),
    "Status" integer NOT NULL,
    "RequestPayload" jsonb NOT NULL,
    "ResponsePayload" jsonb,
    "ErrorCode" character varying(64),
    "ErrorMessage" character varying(2000),
    "AttemptCount" integer NOT NULL,
    "LastAttemptAt" timestamp with time zone,
    "NextRetryAt" timestamp with time zone NOT NULL,
    "GeneratedAt" timestamp with time zone,
    "ActivatedAt" timestamp with time zone,
    "RevokedAt" timestamp with time zone,
    "RevokedReason" character varying(500),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 286 (class 1259 OID 107939)
-- Name: setting_history; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.setting_history (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Key" character varying(100) NOT NULL,
    "Category" character varying(60),
    "OldValue" text,
    "NewValue" text,
    "WasSecret" boolean NOT NULL,
    "ChangedById" uuid,
    "ChangedByName" character varying(200) NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 284 (class 1259 OID 107887)
-- Name: settlement_adjustments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.settlement_adjustments (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "SettlementBatchId" uuid NOT NULL,
    "PayoutId" uuid,
    "Type" integer NOT NULL,
    "Amount" numeric(12,2) NOT NULL,
    "Reason" character varying(300) NOT NULL,
    "CreatedById" uuid,
    "CreatedByName" character varying(200) NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 282 (class 1259 OID 107833)
-- Name: settlement_batches; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.settlement_batches (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "BatchNumber" character varying(40) NOT NULL,
    "BeneficiaryType" integer NOT NULL,
    "Status" integer NOT NULL,
    "PeriodStartUtc" timestamp with time zone NOT NULL,
    "PeriodEndUtc" timestamp with time zone NOT NULL,
    "BeneficiaryCount" integer NOT NULL,
    "TotalGross" numeric(12,2) NOT NULL,
    "TotalAdjustments" numeric(12,2) NOT NULL,
    "TotalRefundAdjustments" numeric(12,2) NOT NULL,
    "TotalTax" numeric(12,2) NOT NULL,
    "TotalNet" numeric(12,2) NOT NULL,
    "ApprovalRequestId" uuid,
    "ApprovedById" uuid,
    "ApprovedByName" text,
    "ApprovedOnUtc" timestamp with time zone,
    "ProcessedOnUtc" timestamp with time zone,
    "PaidOnUtc" timestamp with time zone,
    "Provider" character varying(40),
    "PaymentReference" character varying(120),
    "Notes" text,
    "CreatedById" uuid,
    "CreatedByName" character varying(200) NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 258 (class 1259 OID 107298)
-- Name: shipments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shipments (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "OrderId" uuid NOT NULL,
    "Courier" character varying(80),
    "TrackingNumber" character varying(80),
    "Status" character varying(20) NOT NULL,
    "DispatchedAt" timestamp with time zone,
    "DeliveredAt" timestamp with time zone,
    "Notes" character varying(500),
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 304 (class 1259 OID 108424)
-- Name: special_price_audits; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.special_price_audits (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "ProductId" uuid NOT NULL,
    "ProductName" character varying(500) NOT NULL,
    "OldPrice" numeric(10,2),
    "NewPrice" numeric(10,2),
    "OldStartDate" timestamp with time zone,
    "NewStartDate" timestamp with time zone,
    "OldEndDate" timestamp with time zone,
    "NewEndDate" timestamp with time zone,
    "ModifiedByUserId" uuid,
    "ModifiedByName" character varying(200) NOT NULL,
    "ModifiedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "Remarks" character varying(500)
);

--
-- TOC entry 264 (class 1259 OID 107413)
-- Name: specification_attribute_groups; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.specification_attribute_groups (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Name" character varying(400) NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 270 (class 1259 OID 107538)
-- Name: specification_attribute_options; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.specification_attribute_options (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "SpecificationAttributeId" uuid CONSTRAINT "specification_attribute_optio_SpecificationAttributeId_not_null" NOT NULL,
    "Name" character varying(400) NOT NULL,
    "ColorSquaresRgb" character varying(7),
    "DisplayOrder" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 268 (class 1259 OID 107498)
-- Name: specification_attributes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.specification_attributes (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Name" character varying(400) NOT NULL,
    "DisplayOrder" integer NOT NULL,
    "SpecificationAttributeGroupId" uuid,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 311 (class 1259 OID 108805)
-- Name: url_redirects; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.url_redirects (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "OldUrl" character varying(500) NOT NULL,
    "NewUrl" character varying(500) NOT NULL,
    "RedirectType" integer DEFAULT 301 NOT NULL,
    "IsActive" boolean DEFAULT true NOT NULL,
    "HitCount" integer DEFAULT 0 NOT NULL,
    "LastHitAt" timestamp with time zone,
    "Notes" text,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 296 (class 1259 OID 108226)
-- Name: user_permission_overrides; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_permission_overrides (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "UserId" uuid NOT NULL,
    "PermissionKey" character varying(120) NOT NULL,
    "Granted" boolean NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 274 (class 1259 OID 107642)
-- Name: user_preferences; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_preferences (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "UserId" uuid NOT NULL,
    "Key" character varying(100) NOT NULL,
    "ValueJson" text NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 228 (class 1259 OID 106481)
-- Name: users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.users (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "Email" public.citext,
    "Phone" character varying(15),
    "PhoneCountry" text NOT NULL,
    "PasswordHash" text,
    "FullName" character varying(200) NOT NULL,
    "FirstName" text,
    "LastName" text,
    "AvatarUrl" text,
    "IsActive" boolean NOT NULL,
    "IsVerified" boolean NOT NULL,
    "GoogleId" text,
    "City" text,
    "State" text,
    "CourseInterest" public.course_level,
    "ReferralCode" text,
    "ReferredById" uuid,
    "LastLoginAt" timestamp with time zone,
    "LoginCount" integer NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "AdminComment" text,
    "Attempt" text,
    "Gender" text
);

--
-- TOC entry 248 (class 1259 OID 107082)
-- Name: wishlist_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.wishlist_items (
    "Id" uuid DEFAULT gen_random_uuid() NOT NULL,
    "UserId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "CreatedAt" timestamp with time zone DEFAULT now() NOT NULL,
    "UpdatedAt" timestamp with time zone DEFAULT now() NOT NULL
);

--
-- TOC entry 5566 (class 2606 OID 106408)
-- Name: Banners PK_Banners; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Banners"
    ADD CONSTRAINT "PK_Banners" PRIMARY KEY ("Id");

--
-- TOC entry 5583 (class 2606 OID 106519)
-- Name: BlogPosts PK_BlogPosts; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."BlogPosts"
    ADD CONSTRAINT "PK_BlogPosts" PRIMARY KEY ("Id");

--
-- TOC entry 5840 (class 2606 OID 108367)
-- Name: BookPreviewPages PK_BookPreviewPages; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."BookPreviewPages"
    ADD CONSTRAINT "PK_BookPreviewPages" PRIMARY KEY ("Id");

--
-- TOC entry 5649 (class 2606 OID 106925)
-- Name: CartItems PK_CartItems; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CartItems"
    ADD CONSTRAINT "PK_CartItems" PRIMARY KEY ("Id");

--
-- TOC entry 5569 (class 2606 OID 106424)
-- Name: Categories PK_Categories; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Categories"
    ADD CONSTRAINT "PK_Categories" PRIMARY KEY ("Id");

--
-- TOC entry 5571 (class 2606 OID 106448)
-- Name: Coupons PK_Coupons; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Coupons"
    ADD CONSTRAINT "PK_Coupons" PRIMARY KEY ("Id");

--
-- TOC entry 5705 (class 2606 OID 107345)
-- Name: DataProtectionKeys PK_DataProtectionKeys; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."DataProtectionKeys"
    ADD CONSTRAINT "PK_DataProtectionKeys" PRIMARY KEY ("Id");

--
-- TOC entry 5618 (class 2606 OID 106721)
-- Name: Enrollments PK_Enrollments; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Enrollments"
    ADD CONSTRAINT "PK_Enrollments" PRIMARY KEY ("Id");

--
-- TOC entry 5622 (class 2606 OID 106748)
-- Name: FacultySharingRules PK_FacultySharingRules; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."FacultySharingRules"
    ADD CONSTRAINT "PK_FacultySharingRules" PRIMARY KEY ("Id");

--
-- TOC entry 5800 (class 2606 OID 108074)
-- Name: FranchiseCommissionEntries PK_FranchiseCommissionEntries; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."FranchiseCommissionEntries"
    ADD CONSTRAINT "PK_FranchiseCommissionEntries" PRIMARY KEY ("Id");

--
-- TOC entry 5804 (class 2606 OID 108098)
-- Name: FranchiseCommissions PK_FranchiseCommissions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."FranchiseCommissions"
    ADD CONSTRAINT "PK_FranchiseCommissions" PRIMARY KEY ("Id");

--
-- TOC entry 5590 (class 2606 OID 106563)
-- Name: Franchises PK_Franchises; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Franchises"
    ADD CONSTRAINT "PK_Franchises" PRIMARY KEY ("Id");

--
-- TOC entry 5641 (class 2606 OID 106886)
-- Name: Invoices PK_Invoices; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Invoices"
    ADD CONSTRAINT "PK_Invoices" PRIMARY KEY ("Id");

--
-- TOC entry 5593 (class 2606 OID 106583)
-- Name: Leads PK_Leads; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Leads"
    ADD CONSTRAINT "PK_Leads" PRIMARY KEY ("Id");

--
-- TOC entry 5654 (class 2606 OID 106961)
-- Name: OrderItems PK_OrderItems; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."OrderItems"
    ADD CONSTRAINT "PK_OrderItems" PRIMARY KEY ("Id");

--
-- TOC entry 5644 (class 2606 OID 106907)
-- Name: Payments PK_Payments; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Payments"
    ADD CONSTRAINT "PK_Payments" PRIMARY KEY ("Id");

--
-- TOC entry 5837 (class 2606 OID 108343)
-- Name: ProductBookPreviews PK_ProductBookPreviews; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ProductBookPreviews"
    ADD CONSTRAINT "PK_ProductBookPreviews" PRIMARY KEY ("Id");

--
-- TOC entry 5626 (class 2606 OID 106771)
-- Name: ProductFaculty PK_ProductFaculty; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ProductFaculty"
    ADD CONSTRAINT "PK_ProductFaculty" PRIMARY KEY ("Id");

--
-- TOC entry 5629 (class 2606 OID 106797)
-- Name: ProductImages PK_ProductImages; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ProductImages"
    ADD CONSTRAINT "PK_ProductImages" PRIMARY KEY ("Id");

--
-- TOC entry 5632 (class 2606 OID 106817)
-- Name: ProductInclusions PK_ProductInclusions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ProductInclusions"
    ADD CONSTRAINT "PK_ProductInclusions" PRIMARY KEY ("Id");

--
-- TOC entry 5635 (class 2606 OID 106840)
-- Name: ProductModes PK_ProductModes; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ProductModes"
    ADD CONSTRAINT "PK_ProductModes" PRIMARY KEY ("Id");

--
-- TOC entry 5843 (class 2606 OID 108392)
-- Name: ProductTestimonials PK_ProductTestimonials; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ProductTestimonials"
    ADD CONSTRAINT "PK_ProductTestimonials" PRIMARY KEY ("Id");

--
-- TOC entry 5573 (class 2606 OID 106464)
-- Name: Roles PK_Roles; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Roles"
    ADD CONSTRAINT "PK_Roles" PRIMARY KEY ("Id");

--
-- TOC entry 5834 (class 2606 OID 108317)
-- Name: ScheduledTaskRuns PK_ScheduledTaskRuns; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ScheduledTaskRuns"
    ADD CONSTRAINT "PK_ScheduledTaskRuns" PRIMARY KEY ("Id");

--
-- TOC entry 5830 (class 2606 OID 108297)
-- Name: ScheduledTasks PK_ScheduledTasks; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ScheduledTasks"
    ADD CONSTRAINT "PK_ScheduledTasks" PRIMARY KEY ("Id");

--
-- TOC entry 5575 (class 2606 OID 106480)
-- Name: Subjects PK_Subjects; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Subjects"
    ADD CONSTRAINT "PK_Subjects" PRIMARY KEY ("Id");

--
-- TOC entry 5638 (class 2606 OID 106860)
-- Name: Testimonials PK_Testimonials; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Testimonials"
    ADD CONSTRAINT "PK_Testimonials" PRIMARY KEY ("Id");

--
-- TOC entry 5614 (class 2606 OID 106690)
-- Name: UserRoles PK_UserRoles; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserRoles"
    ADD CONSTRAINT "PK_UserRoles" PRIMARY KEY ("Id");

--
-- TOC entry 5808 (class 2606 OID 108129)
-- Name: WalletRecharges PK_WalletRecharges; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."WalletRecharges"
    ADD CONSTRAINT "PK_WalletRecharges" PRIMARY KEY ("Id");

--
-- TOC entry 5562 (class 2606 OID 106131)
-- Name: __EFMigrationsHistory PK___EFMigrationsHistory; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."__EFMigrationsHistory"
    ADD CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId");

--
-- TOC entry 5740 (class 2606 OID 107641)
-- Name: admin_notifications PK_admin_notifications; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.admin_notifications
    ADD CONSTRAINT "PK_admin_notifications" PRIMARY KEY ("Id");

--
-- TOC entry 5671 (class 2606 OID 107149)
-- Name: affiliate_referrals PK_affiliate_referrals; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.affiliate_referrals
    ADD CONSTRAINT "PK_affiliate_referrals" PRIMARY KEY ("Id");

--
-- TOC entry 5667 (class 2606 OID 107127)
-- Name: affiliates PK_affiliates; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.affiliates
    ADD CONSTRAINT "PK_affiliates" PRIMARY KEY ("Id");

--
-- TOC entry 5564 (class 2606 OID 106392)
-- Name: app_settings PK_app_settings; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_settings
    ADD CONSTRAINT "PK_app_settings" PRIMARY KEY ("Key");

--
-- TOC entry 5764 (class 2606 OID 107802)
-- Name: approval_comments PK_approval_comments; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.approval_comments
    ADD CONSTRAINT "PK_approval_comments" PRIMARY KEY ("Id");

--
-- TOC entry 5761 (class 2606 OID 107786)
-- Name: approval_requests PK_approval_requests; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.approval_requests
    ADD CONSTRAINT "PK_approval_requests" PRIMARY KEY ("Id");

--
-- TOC entry 5767 (class 2606 OID 107823)
-- Name: approval_steps PK_approval_steps; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.approval_steps
    ADD CONSTRAINT "PK_approval_steps" PRIMARY KEY ("Id");

--
-- TOC entry 5703 (class 2606 OID 107335)
-- Name: audit_logs PK_audit_logs; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.audit_logs
    ADD CONSTRAINT "PK_audit_logs" PRIMARY KEY ("Id");

--
-- TOC entry 5673 (class 2606 OID 107181)
-- Name: campaigns PK_campaigns; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.campaigns
    ADD CONSTRAINT "PK_campaigns" PRIMARY KEY ("Id");

--
-- TOC entry 5714 (class 2606 OID 107442)
-- Name: checkout_attribute_values PK_checkout_attribute_values; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkout_attribute_values
    ADD CONSTRAINT "PK_checkout_attribute_values" PRIMARY KEY ("Id");

--
-- TOC entry 5707 (class 2606 OID 107398)
-- Name: checkout_attributes PK_checkout_attributes; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkout_attributes
    ADD CONSTRAINT "PK_checkout_attributes" PRIMARY KEY ("Id");

--
-- TOC entry 5679 (class 2606 OID 107213)
-- Name: cms_pages PK_cms_pages; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cms_pages
    ADD CONSTRAINT "PK_cms_pages" PRIMARY KEY ("Id");

--
-- TOC entry 5794 (class 2606 OID 108003)
-- Name: customer_addresses PK_customer_addresses; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_addresses
    ADD CONSTRAINT "PK_customer_addresses" PRIMARY KEY ("Id");

--
-- TOC entry 5587 (class 2606 OID 106541)
-- Name: faculty PK_faculty; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.faculty
    ADD CONSTRAINT "PK_faculty" PRIMARY KEY ("Id");

--
-- TOC entry 5691 (class 2606 OID 107268)
-- Name: franchise_ledger PK_franchise_ledger; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.franchise_ledger
    ADD CONSTRAINT "PK_franchise_ledger" PRIMARY KEY ("Id");

--
-- TOC entry 5797 (class 2606 OID 108027)
-- Name: menu_items PK_menu_items; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.menu_items
    ADD CONSTRAINT "PK_menu_items" PRIMARY KEY ("Id");

--
-- TOC entry 5682 (class 2606 OID 107233)
-- Name: message_templates PK_message_templates; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.message_templates
    ADD CONSTRAINT "PK_message_templates" PRIMARY KEY ("Id");

--
-- TOC entry 5676 (class 2606 OID 107194)
-- Name: newsletter_subscribers PK_newsletter_subscribers; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.newsletter_subscribers
    ADD CONSTRAINT "PK_newsletter_subscribers" PRIMARY KEY ("Id");

--
-- TOC entry 5757 (class 2606 OID 107759)
-- Name: note_attachments PK_note_attachments; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.note_attachments
    ADD CONSTRAINT "PK_note_attachments" PRIMARY KEY ("Id");

--
-- TOC entry 5688 (class 2606 OID 107249)
-- Name: notification_logs PK_notification_logs; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notification_logs
    ADD CONSTRAINT "PK_notification_logs" PRIMARY KEY ("Id");

--
-- TOC entry 5737 (class 2606 OID 107617)
-- Name: order_notes PK_order_notes; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.order_notes
    ADD CONSTRAINT "PK_order_notes" PRIMARY KEY ("Id");

--
-- TOC entry 5609 (class 2606 OID 106657)
-- Name: orders PK_orders; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT "PK_orders" PRIMARY KEY ("Id");

--
-- TOC entry 5746 (class 2606 OID 107680)
-- Name: payment_transactions PK_payment_transactions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_transactions
    ADD CONSTRAINT "PK_payment_transactions" PRIMARY KEY ("Id");

--
-- TOC entry 5784 (class 2606 OID 107924)
-- Name: payout_items PK_payout_items; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payout_items
    ADD CONSTRAINT "PK_payout_items" PRIMARY KEY ("Id");

--
-- TOC entry 5776 (class 2606 OID 107881)
-- Name: payouts PK_payouts; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payouts
    ADD CONSTRAINT "PK_payouts" PRIMARY KEY ("Id");

--
-- TOC entry 5827 (class 2606 OID 108261)
-- Name: permission_audit_logs PK_permission_audit_logs; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.permission_audit_logs
    ADD CONSTRAINT "PK_permission_audit_logs" PRIMARY KEY ("Id");

--
-- TOC entry 5717 (class 2606 OID 107464)
-- Name: predefined_product_attribute_values PK_predefined_product_attribute_values; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.predefined_product_attribute_values
    ADD CONSTRAINT "PK_predefined_product_attribute_values" PRIMARY KEY ("Id");

--
-- TOC entry 5721 (class 2606 OID 107487)
-- Name: product_attribute_mappings PK_product_attribute_mappings; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_attribute_mappings
    ADD CONSTRAINT "PK_product_attribute_mappings" PRIMARY KEY ("Id");

--
-- TOC entry 5727 (class 2606 OID 107532)
-- Name: product_attribute_values PK_product_attribute_values; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_attribute_values
    ADD CONSTRAINT "PK_product_attribute_values" PRIMARY KEY ("Id");

--
-- TOC entry 5709 (class 2606 OID 107412)
-- Name: product_attributes PK_product_attributes; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_attributes
    ADD CONSTRAINT "PK_product_attributes" PRIMARY KEY ("Id");

--
-- TOC entry 5847 (class 2606 OID 108413)
-- Name: product_categories PK_product_categories; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_categories
    ADD CONSTRAINT "PK_product_categories" PRIMARY KEY ("Id");

--
-- TOC entry 5817 (class 2606 OID 108188)
-- Name: product_recommendations PK_product_recommendations; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_recommendations
    ADD CONSTRAINT "PK_product_recommendations" PRIMARY KEY ("Id");

--
-- TOC entry 5856 (class 2606 OID 108465)
-- Name: product_serial_key_configs PK_product_serial_key_configs; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_serial_key_configs
    ADD CONSTRAINT "PK_product_serial_key_configs" PRIMARY KEY ("Id");

--
-- TOC entry 5734 (class 2606 OID 107572)
-- Name: product_specification_attributes PK_product_specification_attributes; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_specification_attributes
    ADD CONSTRAINT "PK_product_specification_attributes" PRIMARY KEY ("Id");

--
-- TOC entry 5791 (class 2606 OID 107978)
-- Name: product_videos PK_product_videos; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_videos
    ADD CONSTRAINT "PK_product_videos" PRIMARY KEY ("Id");

--
-- TOC entry 5600 (class 2606 OID 106616)
-- Name: products PK_products; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT "PK_products" PRIMARY KEY ("Id");

--
-- TOC entry 5812 (class 2606 OID 108168)
-- Name: referral_sources PK_referral_sources; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.referral_sources
    ADD CONSTRAINT "PK_referral_sources" PRIMARY KEY ("Id");

--
-- TOC entry 5753 (class 2606 OID 107727)
-- Name: refund_items PK_refund_items; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_items
    ADD CONSTRAINT "PK_refund_items" PRIMARY KEY ("Id");

--
-- TOC entry 5749 (class 2606 OID 107704)
-- Name: refunds PK_refunds; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refunds
    ADD CONSTRAINT "PK_refunds" PRIMARY KEY ("Id");

--
-- TOC entry 5694 (class 2606 OID 107292)
-- Name: return_requests PK_return_requests; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.return_requests
    ADD CONSTRAINT "PK_return_requests" PRIMARY KEY ("Id");

--
-- TOC entry 5659 (class 2606 OID 107068)
-- Name: reviews PK_reviews; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reviews
    ADD CONSTRAINT "PK_reviews" PRIMARY KEY ("Id");

--
-- TOC entry 5860 (class 2606 OID 108489)
-- Name: rioplay_tenants PK_rioplay_tenants; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.rioplay_tenants
    ADD CONSTRAINT "PK_rioplay_tenants" PRIMARY KEY ("Id");

--
-- TOC entry 5820 (class 2606 OID 108220)
-- Name: role_permissions PK_role_permissions; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT "PK_role_permissions" PRIMARY KEY ("Id");

--
-- TOC entry 5868 (class 2606 OID 108510)
-- Name: serial_key_records PK_serial_key_records; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.serial_key_records
    ADD CONSTRAINT "PK_serial_key_records" PRIMARY KEY ("Id");

--
-- TOC entry 5788 (class 2606 OID 107954)
-- Name: setting_history PK_setting_history; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.setting_history
    ADD CONSTRAINT "PK_setting_history" PRIMARY KEY ("Id");

--
-- TOC entry 5779 (class 2606 OID 107904)
-- Name: settlement_adjustments PK_settlement_adjustments; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.settlement_adjustments
    ADD CONSTRAINT "PK_settlement_adjustments" PRIMARY KEY ("Id");

--
-- TOC entry 5771 (class 2606 OID 107857)
-- Name: settlement_batches PK_settlement_batches; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.settlement_batches
    ADD CONSTRAINT "PK_settlement_batches" PRIMARY KEY ("Id");

--
-- TOC entry 5697 (class 2606 OID 107312)
-- Name: shipments PK_shipments; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shipments
    ADD CONSTRAINT "PK_shipments" PRIMARY KEY ("Id");

--
-- TOC entry 5851 (class 2606 OID 108437)
-- Name: special_price_audits PK_special_price_audits; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.special_price_audits
    ADD CONSTRAINT "PK_special_price_audits" PRIMARY KEY ("Id");

--
-- TOC entry 5711 (class 2606 OID 107425)
-- Name: specification_attribute_groups PK_specification_attribute_groups; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.specification_attribute_groups
    ADD CONSTRAINT "PK_specification_attribute_groups" PRIMARY KEY ("Id");

--
-- TOC entry 5730 (class 2606 OID 107551)
-- Name: specification_attribute_options PK_specification_attribute_options; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.specification_attribute_options
    ADD CONSTRAINT "PK_specification_attribute_options" PRIMARY KEY ("Id");

--
-- TOC entry 5724 (class 2606 OID 107510)
-- Name: specification_attributes PK_specification_attributes; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.specification_attributes
    ADD CONSTRAINT "PK_specification_attributes" PRIMARY KEY ("Id");

--
-- TOC entry 5885 (class 2606 OID 108825)
-- Name: url_redirects PK_url_redirects; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.url_redirects
    ADD CONSTRAINT "PK_url_redirects" PRIMARY KEY ("Id");

--
-- TOC entry 5823 (class 2606 OID 108239)
-- Name: user_permission_overrides PK_user_permission_overrides; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_permission_overrides
    ADD CONSTRAINT "PK_user_permission_overrides" PRIMARY KEY ("Id");

--
-- TOC entry 5743 (class 2606 OID 107657)
-- Name: user_preferences PK_user_preferences; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_preferences
    ADD CONSTRAINT "PK_user_preferences" PRIMARY KEY ("Id");

--
-- TOC entry 5580 (class 2606 OID 106497)
-- Name: users PK_users; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT "PK_users" PRIMARY KEY ("Id");

--
-- TOC entry 5663 (class 2606 OID 107094)
-- Name: wishlist_items PK_wishlist_items; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.wishlist_items
    ADD CONSTRAINT "PK_wishlist_items" PRIMARY KEY ("Id");

--
-- TOC entry 5874 (class 2606 OID 108558)
-- Name: app_logs app_logs_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_logs
    ADD CONSTRAINT app_logs_pkey PRIMARY KEY ("Id");

--
-- TOC entry 5882 (class 2606 OID 108641)
-- Name: invoice_line_items invoice_line_items_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invoice_line_items
    ADD CONSTRAINT invoice_line_items_pkey PRIMARY KEY ("Id");

--
-- TOC entry 5879 (class 2606 OID 108607)
-- Name: invoices invoices_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invoices
    ADD CONSTRAINT invoices_pkey PRIMARY KEY ("Id");

--
-- TOC entry 5581 (class 1259 OID 106977)
-- Name: IX_BlogPosts_AuthorId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_BlogPosts_AuthorId" ON public."BlogPosts" USING btree ("AuthorId");

--
-- TOC entry 5838 (class 1259 OID 108531)
-- Name: IX_BookPreviewPages_BookPreviewId_PageNumber; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_BookPreviewPages_BookPreviewId_PageNumber" ON public."BookPreviewPages" USING btree ("BookPreviewId", "PageNumber");

--
-- TOC entry 5645 (class 1259 OID 106978)
-- Name: IX_CartItems_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CartItems_ProductId" ON public."CartItems" USING btree ("ProductId");

--
-- TOC entry 5646 (class 1259 OID 106979)
-- Name: IX_CartItems_ProductModeId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CartItems_ProductModeId" ON public."CartItems" USING btree ("ProductModeId");

--
-- TOC entry 5647 (class 1259 OID 106980)
-- Name: IX_CartItems_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_CartItems_UserId" ON public."CartItems" USING btree ("UserId");

--
-- TOC entry 5567 (class 1259 OID 106981)
-- Name: IX_Categories_ParentId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Categories_ParentId" ON public."Categories" USING btree ("ParentId");

--
-- TOC entry 5615 (class 1259 OID 106982)
-- Name: IX_Enrollments_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Enrollments_ProductId" ON public."Enrollments" USING btree ("ProductId");

--
-- TOC entry 5616 (class 1259 OID 106983)
-- Name: IX_Enrollments_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Enrollments_UserId" ON public."Enrollments" USING btree ("UserId");

--
-- TOC entry 5619 (class 1259 OID 106986)
-- Name: IX_FacultySharingRules_FacultyId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_FacultySharingRules_FacultyId" ON public."FacultySharingRules" USING btree ("FacultyId");

--
-- TOC entry 5620 (class 1259 OID 106987)
-- Name: IX_FacultySharingRules_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_FacultySharingRules_ProductId" ON public."FacultySharingRules" USING btree ("ProductId");

--
-- TOC entry 5798 (class 1259 OID 108109)
-- Name: IX_FranchiseCommissionEntries_FranchiseId_EarnedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_FranchiseCommissionEntries_FranchiseId_EarnedAt" ON public."FranchiseCommissionEntries" USING btree ("FranchiseId", "EarnedAt");

--
-- TOC entry 5801 (class 1259 OID 108110)
-- Name: IX_FranchiseCommissions_FranchiseId_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_FranchiseCommissions_FranchiseId_ProductId" ON public."FranchiseCommissions" USING btree ("FranchiseId", "ProductId");

--
-- TOC entry 5802 (class 1259 OID 108111)
-- Name: IX_FranchiseCommissions_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_FranchiseCommissions_ProductId" ON public."FranchiseCommissions" USING btree ("ProductId");

--
-- TOC entry 5588 (class 1259 OID 106988)
-- Name: IX_Franchises_AdminUserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Franchises_AdminUserId" ON public."Franchises" USING btree ("AdminUserId");

--
-- TOC entry 5639 (class 1259 OID 106989)
-- Name: IX_Invoices_OrderId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Invoices_OrderId" ON public."Invoices" USING btree ("OrderId");

--
-- TOC entry 5591 (class 1259 OID 106990)
-- Name: IX_Leads_AssignedToId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Leads_AssignedToId" ON public."Leads" USING btree ("AssignedToId");

--
-- TOC entry 5650 (class 1259 OID 106991)
-- Name: IX_OrderItems_OrderId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_OrderItems_OrderId" ON public."OrderItems" USING btree ("OrderId");

--
-- TOC entry 5651 (class 1259 OID 106992)
-- Name: IX_OrderItems_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_OrderItems_ProductId" ON public."OrderItems" USING btree ("ProductId");

--
-- TOC entry 5652 (class 1259 OID 106993)
-- Name: IX_OrderItems_ProductModeId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_OrderItems_ProductModeId" ON public."OrderItems" USING btree ("ProductModeId");

--
-- TOC entry 5642 (class 1259 OID 107000)
-- Name: IX_Payments_OrderId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Payments_OrderId" ON public."Payments" USING btree ("OrderId");

--
-- TOC entry 5835 (class 1259 OID 108349)
-- Name: IX_ProductBookPreviews_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ProductBookPreviews_ProductId" ON public."ProductBookPreviews" USING btree ("ProductId");

--
-- TOC entry 5623 (class 1259 OID 107001)
-- Name: IX_ProductFaculty_FacultyId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ProductFaculty_FacultyId" ON public."ProductFaculty" USING btree ("FacultyId");

--
-- TOC entry 5624 (class 1259 OID 107002)
-- Name: IX_ProductFaculty_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ProductFaculty_ProductId" ON public."ProductFaculty" USING btree ("ProductId");

--
-- TOC entry 5627 (class 1259 OID 107003)
-- Name: IX_ProductImages_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ProductImages_ProductId" ON public."ProductImages" USING btree ("ProductId");

--
-- TOC entry 5630 (class 1259 OID 107004)
-- Name: IX_ProductInclusions_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ProductInclusions_ProductId" ON public."ProductInclusions" USING btree ("ProductId");

--
-- TOC entry 5633 (class 1259 OID 107005)
-- Name: IX_ProductModes_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ProductModes_ProductId" ON public."ProductModes" USING btree ("ProductId");

--
-- TOC entry 5841 (class 1259 OID 108398)
-- Name: IX_ProductTestimonials_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ProductTestimonials_ProductId" ON public."ProductTestimonials" USING btree ("ProductId");

--
-- TOC entry 5831 (class 1259 OID 108323)
-- Name: IX_ScheduledTaskRuns_ScheduledTaskId_StartedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ScheduledTaskRuns_ScheduledTaskId_StartedAt" ON public."ScheduledTaskRuns" USING btree ("ScheduledTaskId", "StartedAt");

--
-- TOC entry 5832 (class 1259 OID 108324)
-- Name: IX_ScheduledTaskRuns_StartedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_ScheduledTaskRuns_StartedAt" ON public."ScheduledTaskRuns" USING btree ("StartedAt");

--
-- TOC entry 5828 (class 1259 OID 108325)
-- Name: IX_ScheduledTasks_TaskKey; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_ScheduledTasks_TaskKey" ON public."ScheduledTasks" USING btree ("TaskKey");

--
-- TOC entry 5636 (class 1259 OID 107011)
-- Name: IX_Testimonials_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_Testimonials_ProductId" ON public."Testimonials" USING btree ("ProductId");

--
-- TOC entry 5610 (class 1259 OID 107012)
-- Name: IX_UserRoles_FranchiseId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_UserRoles_FranchiseId" ON public."UserRoles" USING btree ("FranchiseId");

--
-- TOC entry 5611 (class 1259 OID 107013)
-- Name: IX_UserRoles_RoleId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_UserRoles_RoleId" ON public."UserRoles" USING btree ("RoleId");

--
-- TOC entry 5612 (class 1259 OID 107014)
-- Name: IX_UserRoles_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_UserRoles_UserId" ON public."UserRoles" USING btree ("UserId");

--
-- TOC entry 5805 (class 1259 OID 108135)
-- Name: IX_WalletRecharges_FranchiseId_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_WalletRecharges_FranchiseId_CreatedAt" ON public."WalletRecharges" USING btree ("FranchiseId", "CreatedAt");

--
-- TOC entry 5806 (class 1259 OID 108136)
-- Name: IX_WalletRecharges_GatewayOrderId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_WalletRecharges_GatewayOrderId" ON public."WalletRecharges" USING btree ("GatewayOrderId");

--
-- TOC entry 5738 (class 1259 OID 107658)
-- Name: IX_admin_notifications_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_admin_notifications_CreatedAt" ON public.admin_notifications USING btree ("CreatedAt");

--
-- TOC entry 5668 (class 1259 OID 107160)
-- Name: IX_affiliate_referrals_AffiliateId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_affiliate_referrals_AffiliateId" ON public.affiliate_referrals USING btree ("AffiliateId");

--
-- TOC entry 5669 (class 1259 OID 107161)
-- Name: IX_affiliate_referrals_OrderId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_affiliate_referrals_OrderId" ON public.affiliate_referrals USING btree ("OrderId");

--
-- TOC entry 5664 (class 1259 OID 107162)
-- Name: IX_affiliates_Code; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_affiliates_Code" ON public.affiliates USING btree ("Code");

--
-- TOC entry 5665 (class 1259 OID 107163)
-- Name: IX_affiliates_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_affiliates_UserId" ON public.affiliates USING btree ("UserId");

--
-- TOC entry 5869 (class 1259 OID 108560)
-- Name: IX_app_logs_Category_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_app_logs_Category_CreatedAt" ON public.app_logs USING btree ("Category", "CreatedAt" DESC);

--
-- TOC entry 5870 (class 1259 OID 108559)
-- Name: IX_app_logs_Level_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_app_logs_Level_CreatedAt" ON public.app_logs USING btree ("Level", "CreatedAt" DESC);

--
-- TOC entry 5871 (class 1259 OID 108561)
-- Name: IX_app_logs_OrderId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_app_logs_OrderId" ON public.app_logs USING btree ("OrderId") WHERE ("OrderId" IS NOT NULL);

--
-- TOC entry 5872 (class 1259 OID 108562)
-- Name: IX_app_logs_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_app_logs_UserId" ON public.app_logs USING btree ("UserId") WHERE ("UserId" IS NOT NULL);

--
-- TOC entry 5762 (class 1259 OID 107829)
-- Name: IX_approval_comments_ApprovalRequestId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_approval_comments_ApprovalRequestId" ON public.approval_comments USING btree ("ApprovalRequestId");

--
-- TOC entry 5758 (class 1259 OID 107830)
-- Name: IX_approval_requests_RelatedEntityType_RelatedEntityId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_approval_requests_RelatedEntityType_RelatedEntityId" ON public.approval_requests USING btree ("RelatedEntityType", "RelatedEntityId");

--
-- TOC entry 5759 (class 1259 OID 107831)
-- Name: IX_approval_requests_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_approval_requests_Status" ON public.approval_requests USING btree ("Status");

--
-- TOC entry 5765 (class 1259 OID 107832)
-- Name: IX_approval_steps_ApprovalRequestId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_approval_steps_ApprovalRequestId" ON public.approval_steps USING btree ("ApprovalRequestId");

--
-- TOC entry 5698 (class 1259 OID 108272)
-- Name: IX_audit_logs_Action; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_audit_logs_Action" ON public.audit_logs USING btree ("Action");

--
-- TOC entry 5699 (class 1259 OID 108273)
-- Name: IX_audit_logs_ActorUserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_audit_logs_ActorUserId" ON public.audit_logs USING btree ("ActorUserId");

--
-- TOC entry 5700 (class 1259 OID 107336)
-- Name: IX_audit_logs_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_audit_logs_CreatedAt" ON public.audit_logs USING btree ("CreatedAt");

--
-- TOC entry 5701 (class 1259 OID 108274)
-- Name: IX_audit_logs_Module; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_audit_logs_Module" ON public.audit_logs USING btree ("Module");

--
-- TOC entry 5712 (class 1259 OID 107583)
-- Name: IX_checkout_attribute_values_CheckoutAttributeId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_checkout_attribute_values_CheckoutAttributeId" ON public.checkout_attribute_values USING btree ("CheckoutAttributeId");

--
-- TOC entry 5677 (class 1259 OID 107214)
-- Name: IX_cms_pages_Slug; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_cms_pages_Slug" ON public.cms_pages USING btree ("Slug");

--
-- TOC entry 5792 (class 1259 OID 108009)
-- Name: IX_customer_addresses_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_customer_addresses_UserId" ON public.customer_addresses USING btree ("UserId");

--
-- TOC entry 5584 (class 1259 OID 106984)
-- Name: IX_faculty_ShortCode; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_faculty_ShortCode" ON public.faculty USING btree ("ShortCode");

--
-- TOC entry 5585 (class 1259 OID 106985)
-- Name: IX_faculty_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_faculty_UserId" ON public.faculty USING btree ("UserId");

--
-- TOC entry 5689 (class 1259 OID 107274)
-- Name: IX_franchise_ledger_FranchiseId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_franchise_ledger_FranchiseId" ON public.franchise_ledger USING btree ("FranchiseId");

--
-- TOC entry 5880 (class 1259 OID 108647)
-- Name: IX_invoice_line_items_InvoiceId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_invoice_line_items_InvoiceId" ON public.invoice_line_items USING btree ("InvoiceId");

--
-- TOC entry 5875 (class 1259 OID 108658)
-- Name: IX_invoices_InvoiceDate; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_invoices_InvoiceDate" ON public.invoices USING btree ("InvoiceDate" DESC);

--
-- TOC entry 5876 (class 1259 OID 108613)
-- Name: IX_invoices_InvoiceNumber; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_invoices_InvoiceNumber" ON public.invoices USING btree ("InvoiceNumber");

--
-- TOC entry 5877 (class 1259 OID 108614)
-- Name: IX_invoices_OrderId_Active; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_invoices_OrderId_Active" ON public.invoices USING btree ("OrderId") WHERE ("Status" = 0);

--
-- TOC entry 5795 (class 1259 OID 108033)
-- Name: IX_menu_items_ParentId_DisplayOrder; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_menu_items_ParentId_DisplayOrder" ON public.menu_items USING btree ("ParentId", "DisplayOrder");

--
-- TOC entry 5680 (class 1259 OID 108147)
-- Name: IX_message_templates_Key_Channel; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_message_templates_Key_Channel" ON public.message_templates USING btree ("Key", "Channel");

--
-- TOC entry 5674 (class 1259 OID 107195)
-- Name: IX_newsletter_subscribers_Email; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_newsletter_subscribers_Email" ON public.newsletter_subscribers USING btree ("Email");

--
-- TOC entry 5754 (class 1259 OID 107765)
-- Name: IX_note_attachments_OrderId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_note_attachments_OrderId" ON public.note_attachments USING btree ("OrderId");

--
-- TOC entry 5755 (class 1259 OID 107766)
-- Name: IX_note_attachments_OrderNoteId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_note_attachments_OrderNoteId" ON public.note_attachments USING btree ("OrderNoteId");

--
-- TOC entry 5683 (class 1259 OID 107251)
-- Name: IX_notification_logs_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_notification_logs_CreatedAt" ON public.notification_logs USING btree ("CreatedAt");

--
-- TOC entry 5684 (class 1259 OID 108832)
-- Name: IX_notification_logs_Provider; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_notification_logs_Provider" ON public.notification_logs USING btree ("Provider");

--
-- TOC entry 5685 (class 1259 OID 108833)
-- Name: IX_notification_logs_Recipient; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_notification_logs_Recipient" ON public.notification_logs USING btree ("Recipient");

--
-- TOC entry 5686 (class 1259 OID 108831)
-- Name: IX_notification_logs_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_notification_logs_Status" ON public.notification_logs USING btree ("Status");

--
-- TOC entry 5735 (class 1259 OID 107624)
-- Name: IX_order_notes_OrderId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_order_notes_OrderId" ON public.order_notes USING btree ("OrderId");

--
-- TOC entry 5601 (class 1259 OID 106994)
-- Name: IX_orders_CouponId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_orders_CouponId" ON public.orders USING btree ("CouponId");

--
-- TOC entry 5602 (class 1259 OID 106995)
-- Name: IX_orders_CreatedById; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_orders_CreatedById" ON public.orders USING btree ("CreatedById");

--
-- TOC entry 5603 (class 1259 OID 106996)
-- Name: IX_orders_FranchiseId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_orders_FranchiseId" ON public.orders USING btree ("FranchiseId");

--
-- TOC entry 5604 (class 1259 OID 107623)
-- Name: IX_orders_IsDeleted; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_orders_IsDeleted" ON public.orders USING btree ("IsDeleted");

--
-- TOC entry 5605 (class 1259 OID 106997)
-- Name: IX_orders_OrderNumber; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_orders_OrderNumber" ON public.orders USING btree ("OrderNumber");

--
-- TOC entry 5606 (class 1259 OID 106998)
-- Name: IX_orders_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_orders_Status" ON public.orders USING btree ("Status");

--
-- TOC entry 5607 (class 1259 OID 106999)
-- Name: IX_orders_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_orders_UserId" ON public.orders USING btree ("UserId");

--
-- TOC entry 5744 (class 1259 OID 107733)
-- Name: IX_payment_transactions_OrderId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_payment_transactions_OrderId" ON public.payment_transactions USING btree ("OrderId");

--
-- TOC entry 5780 (class 1259 OID 107930)
-- Name: IX_payout_items_OrderItemId_Source; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_payout_items_OrderItemId_Source" ON public.payout_items USING btree ("OrderItemId", "Source");

--
-- TOC entry 5781 (class 1259 OID 107931)
-- Name: IX_payout_items_PayoutId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_payout_items_PayoutId" ON public.payout_items USING btree ("PayoutId");

--
-- TOC entry 5782 (class 1259 OID 107932)
-- Name: IX_payout_items_RefundId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_payout_items_RefundId" ON public.payout_items USING btree ("RefundId");

--
-- TOC entry 5772 (class 1259 OID 107933)
-- Name: IX_payouts_BeneficiaryType_BeneficiaryId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_payouts_BeneficiaryType_BeneficiaryId" ON public.payouts USING btree ("BeneficiaryType", "BeneficiaryId");

--
-- TOC entry 5773 (class 1259 OID 107934)
-- Name: IX_payouts_SettlementBatchId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_payouts_SettlementBatchId" ON public.payouts USING btree ("SettlementBatchId");

--
-- TOC entry 5774 (class 1259 OID 107935)
-- Name: IX_payouts_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_payouts_Status" ON public.payouts USING btree ("Status");

--
-- TOC entry 5824 (class 1259 OID 108267)
-- Name: IX_permission_audit_logs_PermissionKey; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_permission_audit_logs_PermissionKey" ON public.permission_audit_logs USING btree ("PermissionKey");

--
-- TOC entry 5825 (class 1259 OID 108268)
-- Name: IX_permission_audit_logs_UserId_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_permission_audit_logs_UserId_CreatedAt" ON public.permission_audit_logs USING btree ("UserId", "CreatedAt");

--
-- TOC entry 5715 (class 1259 OID 107584)
-- Name: IX_predefined_product_attribute_values_ProductAttributeId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_predefined_product_attribute_values_ProductAttributeId" ON public.predefined_product_attribute_values USING btree ("ProductAttributeId");

--
-- TOC entry 5718 (class 1259 OID 107585)
-- Name: IX_product_attribute_mappings_ProductAttributeId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_product_attribute_mappings_ProductAttributeId" ON public.product_attribute_mappings USING btree ("ProductAttributeId");

--
-- TOC entry 5719 (class 1259 OID 107586)
-- Name: IX_product_attribute_mappings_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_product_attribute_mappings_ProductId" ON public.product_attribute_mappings USING btree ("ProductId");

--
-- TOC entry 5725 (class 1259 OID 107587)
-- Name: IX_product_attribute_values_ProductAttributeMappingId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_product_attribute_values_ProductAttributeMappingId" ON public.product_attribute_values USING btree ("ProductAttributeMappingId");

--
-- TOC entry 5844 (class 1259 OID 108444)
-- Name: IX_product_categories_CategoryId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_product_categories_CategoryId" ON public.product_categories USING btree ("CategoryId");

--
-- TOC entry 5845 (class 1259 OID 108445)
-- Name: IX_product_categories_ProductId_CategoryId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_product_categories_ProductId_CategoryId" ON public.product_categories USING btree ("ProductId", "CategoryId");

--
-- TOC entry 5813 (class 1259 OID 108199)
-- Name: IX_product_recommendations_ProductId_IsActive_Priority_Display~; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_product_recommendations_ProductId_IsActive_Priority_Display~" ON public.product_recommendations USING btree ("ProductId", "IsActive", "Priority", "DisplayOrder");

--
-- TOC entry 5814 (class 1259 OID 108200)
-- Name: IX_product_recommendations_ProductId_RecommendedProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_product_recommendations_ProductId_RecommendedProductId" ON public.product_recommendations USING btree ("ProductId", "RecommendedProductId") WHERE ("IsDeleted" = false);

--
-- TOC entry 5815 (class 1259 OID 108201)
-- Name: IX_product_recommendations_RecommendedProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_product_recommendations_RecommendedProductId" ON public.product_recommendations USING btree ("RecommendedProductId");

--
-- TOC entry 5852 (class 1259 OID 108532)
-- Name: IX_product_serial_key_configs_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_product_serial_key_configs_ProductId" ON public.product_serial_key_configs USING btree ("ProductId");

--
-- TOC entry 5853 (class 1259 OID 108533)
-- Name: IX_product_serial_key_configs_ProductId_ProviderKey_IsActive; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_product_serial_key_configs_ProductId_ProviderKey_IsActive" ON public.product_serial_key_configs USING btree ("ProductId", "ProviderKey", "IsActive") WHERE ("IsActive" = true);

--
-- TOC entry 5854 (class 1259 OID 108764)
-- Name: IX_product_serial_key_configs_TenantId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_product_serial_key_configs_TenantId" ON public.product_serial_key_configs USING btree ("TenantId");

--
-- TOC entry 5731 (class 1259 OID 107588)
-- Name: IX_product_specification_attributes_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_product_specification_attributes_ProductId" ON public.product_specification_attributes USING btree ("ProductId");

--
-- TOC entry 5732 (class 1259 OID 107589)
-- Name: IX_product_specification_attributes_SpecificationAttributeOpti~; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_product_specification_attributes_SpecificationAttributeOpti~" ON public.product_specification_attributes USING btree ("SpecificationAttributeOptionId");

--
-- TOC entry 5789 (class 1259 OID 107984)
-- Name: IX_product_videos_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_product_videos_ProductId" ON public.product_videos USING btree ("ProductId");

--
-- TOC entry 5594 (class 1259 OID 107006)
-- Name: IX_products_CategoryId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_products_CategoryId" ON public.products USING btree ("CategoryId");

--
-- TOC entry 5595 (class 1259 OID 107007)
-- Name: IX_products_PrimaryFacultyId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_products_PrimaryFacultyId" ON public.products USING btree ("PrimaryFacultyId");

--
-- TOC entry 5596 (class 1259 OID 107008)
-- Name: IX_products_Slug; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_products_Slug" ON public.products USING btree ("Slug");

--
-- TOC entry 5597 (class 1259 OID 107009)
-- Name: IX_products_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_products_Status" ON public.products USING btree ("Status");

--
-- TOC entry 5598 (class 1259 OID 107010)
-- Name: IX_products_SubjectId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_products_SubjectId" ON public.products USING btree ("SubjectId");

--
-- TOC entry 5809 (class 1259 OID 108169)
-- Name: IX_referral_sources_IsActive_DisplayOrder; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_referral_sources_IsActive_DisplayOrder" ON public.referral_sources USING btree ("IsActive", "DisplayOrder");

--
-- TOC entry 5810 (class 1259 OID 108170)
-- Name: IX_referral_sources_Name; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_referral_sources_Name" ON public.referral_sources USING btree ("Name") WHERE ("IsDeleted" = false);

--
-- TOC entry 5750 (class 1259 OID 107734)
-- Name: IX_refund_items_OrderItemId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_refund_items_OrderItemId" ON public.refund_items USING btree ("OrderItemId");

--
-- TOC entry 5751 (class 1259 OID 107735)
-- Name: IX_refund_items_RefundId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_refund_items_RefundId" ON public.refund_items USING btree ("RefundId");

--
-- TOC entry 5747 (class 1259 OID 107736)
-- Name: IX_refunds_OrderId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_refunds_OrderId" ON public.refunds USING btree ("OrderId");

--
-- TOC entry 5692 (class 1259 OID 107318)
-- Name: IX_return_requests_OrderId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_return_requests_OrderId" ON public.return_requests USING btree ("OrderId");

--
-- TOC entry 5655 (class 1259 OID 107079)
-- Name: IX_reviews_ProductId_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_reviews_ProductId_UserId" ON public.reviews USING btree ("ProductId", "UserId");

--
-- TOC entry 5656 (class 1259 OID 107080)
-- Name: IX_reviews_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_reviews_Status" ON public.reviews USING btree ("Status");

--
-- TOC entry 5657 (class 1259 OID 107081)
-- Name: IX_reviews_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_reviews_UserId" ON public.reviews USING btree ("UserId");

--
-- TOC entry 5857 (class 1259 OID 108534)
-- Name: IX_rioplay_tenants_IsDefault; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_rioplay_tenants_IsDefault" ON public.rioplay_tenants USING btree ("IsDefault") WHERE ("IsDefault" = true);

--
-- TOC entry 5858 (class 1259 OID 108535)
-- Name: IX_rioplay_tenants_TenantId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_rioplay_tenants_TenantId" ON public.rioplay_tenants USING btree ("TenantId");

--
-- TOC entry 5818 (class 1259 OID 108245)
-- Name: IX_role_permissions_RoleId_PermissionKey; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_role_permissions_RoleId_PermissionKey" ON public.role_permissions USING btree ("RoleId", "PermissionKey");

--
-- TOC entry 5861 (class 1259 OID 108536)
-- Name: IX_serial_key_records_OrderId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_serial_key_records_OrderId" ON public.serial_key_records USING btree ("OrderId");

--
-- TOC entry 5862 (class 1259 OID 108537)
-- Name: IX_serial_key_records_OrderItemId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_serial_key_records_OrderItemId" ON public.serial_key_records USING btree ("OrderItemId");

--
-- TOC entry 5863 (class 1259 OID 108538)
-- Name: IX_serial_key_records_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_serial_key_records_ProductId" ON public.serial_key_records USING btree ("ProductId");

--
-- TOC entry 5864 (class 1259 OID 108539)
-- Name: IX_serial_key_records_SerialKey; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_serial_key_records_SerialKey" ON public.serial_key_records USING btree ("SerialKey") WHERE ("SerialKey" IS NOT NULL);

--
-- TOC entry 5865 (class 1259 OID 108540)
-- Name: IX_serial_key_records_Status_NextRetryAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_serial_key_records_Status_NextRetryAt" ON public.serial_key_records USING btree ("Status", "NextRetryAt");

--
-- TOC entry 5866 (class 1259 OID 108541)
-- Name: IX_serial_key_records_UserId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_serial_key_records_UserId" ON public.serial_key_records USING btree ("UserId");

--
-- TOC entry 5785 (class 1259 OID 107955)
-- Name: IX_setting_history_CreatedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_setting_history_CreatedAt" ON public.setting_history USING btree ("CreatedAt");

--
-- TOC entry 5786 (class 1259 OID 107956)
-- Name: IX_setting_history_Key; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_setting_history_Key" ON public.setting_history USING btree ("Key");

--
-- TOC entry 5777 (class 1259 OID 107936)
-- Name: IX_settlement_adjustments_SettlementBatchId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_settlement_adjustments_SettlementBatchId" ON public.settlement_adjustments USING btree ("SettlementBatchId");

--
-- TOC entry 5768 (class 1259 OID 107937)
-- Name: IX_settlement_batches_BatchNumber; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_settlement_batches_BatchNumber" ON public.settlement_batches USING btree ("BatchNumber");

--
-- TOC entry 5769 (class 1259 OID 107938)
-- Name: IX_settlement_batches_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_settlement_batches_Status" ON public.settlement_batches USING btree ("Status");

--
-- TOC entry 5695 (class 1259 OID 107319)
-- Name: IX_shipments_OrderId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_shipments_OrderId" ON public.shipments USING btree ("OrderId");

--
-- TOC entry 5848 (class 1259 OID 108446)
-- Name: IX_special_price_audits_ModifiedAt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_special_price_audits_ModifiedAt" ON public.special_price_audits USING btree ("ModifiedAt");

--
-- TOC entry 5849 (class 1259 OID 108447)
-- Name: IX_special_price_audits_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_special_price_audits_ProductId" ON public.special_price_audits USING btree ("ProductId");

--
-- TOC entry 5728 (class 1259 OID 107590)
-- Name: IX_specification_attribute_options_SpecificationAttributeId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_specification_attribute_options_SpecificationAttributeId" ON public.specification_attribute_options USING btree ("SpecificationAttributeId");

--
-- TOC entry 5722 (class 1259 OID 107591)
-- Name: IX_specification_attributes_SpecificationAttributeGroupId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_specification_attributes_SpecificationAttributeGroupId" ON public.specification_attributes USING btree ("SpecificationAttributeGroupId");

--
-- TOC entry 5883 (class 1259 OID 108826)
-- Name: IX_url_redirects_OldUrl; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_url_redirects_OldUrl" ON public.url_redirects USING btree ("OldUrl");

--
-- TOC entry 5821 (class 1259 OID 108246)
-- Name: IX_user_permission_overrides_UserId_PermissionKey; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_user_permission_overrides_UserId_PermissionKey" ON public.user_permission_overrides USING btree ("UserId", "PermissionKey");

--
-- TOC entry 5741 (class 1259 OID 107659)
-- Name: IX_user_preferences_UserId_Key; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_user_preferences_UserId_Key" ON public.user_preferences USING btree ("UserId", "Key");

--
-- TOC entry 5576 (class 1259 OID 107015)
-- Name: IX_users_Email; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_users_Email" ON public.users USING btree ("Email") WHERE ("Email" IS NOT NULL);

--
-- TOC entry 5577 (class 1259 OID 107016)
-- Name: IX_users_Phone; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_users_Phone" ON public.users USING btree ("Phone") WHERE ("Phone" IS NOT NULL);

--
-- TOC entry 5578 (class 1259 OID 107017)
-- Name: IX_users_ReferredById; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_users_ReferredById" ON public.users USING btree ("ReferredById");

--
-- TOC entry 5660 (class 1259 OID 107105)
-- Name: IX_wishlist_items_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_wishlist_items_ProductId" ON public.wishlist_items USING btree ("ProductId");

--
-- TOC entry 5661 (class 1259 OID 107106)
-- Name: IX_wishlist_items_UserId_ProductId; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_wishlist_items_UserId_ProductId" ON public.wishlist_items USING btree ("UserId", "ProductId");

--
-- TOC entry 5888 (class 2606 OID 106520)
-- Name: BlogPosts FK_BlogPosts_users_AuthorId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."BlogPosts"
    ADD CONSTRAINT "FK_BlogPosts_users_AuthorId" FOREIGN KEY ("AuthorId") REFERENCES public.users("Id");

--
-- TOC entry 5963 (class 2606 OID 108368)
-- Name: BookPreviewPages FK_BookPreviewPages_ProductBookPreviews_BookPreviewId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."BookPreviewPages"
    ADD CONSTRAINT "FK_BookPreviewPages_ProductBookPreviews_BookPreviewId" FOREIGN KEY ("BookPreviewId") REFERENCES public."ProductBookPreviews"("Id") ON DELETE CASCADE;

--
-- TOC entry 5914 (class 2606 OID 106926)
-- Name: CartItems FK_CartItems_ProductModes_ProductModeId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CartItems"
    ADD CONSTRAINT "FK_CartItems_ProductModes_ProductModeId" FOREIGN KEY ("ProductModeId") REFERENCES public."ProductModes"("Id");

--
-- TOC entry 5915 (class 2606 OID 106931)
-- Name: CartItems FK_CartItems_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CartItems"
    ADD CONSTRAINT "FK_CartItems_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5916 (class 2606 OID 106936)
-- Name: CartItems FK_CartItems_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."CartItems"
    ADD CONSTRAINT "FK_CartItems_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;

--
-- TOC entry 5886 (class 2606 OID 106425)
-- Name: Categories FK_Categories_Categories_ParentId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Categories"
    ADD CONSTRAINT "FK_Categories_Categories_ParentId" FOREIGN KEY ("ParentId") REFERENCES public."Categories"("Id");

--
-- TOC entry 5902 (class 2606 OID 106722)
-- Name: Enrollments FK_Enrollments_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Enrollments"
    ADD CONSTRAINT "FK_Enrollments_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5903 (class 2606 OID 106727)
-- Name: Enrollments FK_Enrollments_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Enrollments"
    ADD CONSTRAINT "FK_Enrollments_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;

--
-- TOC entry 5904 (class 2606 OID 106749)
-- Name: FacultySharingRules FK_FacultySharingRules_faculty_FacultyId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."FacultySharingRules"
    ADD CONSTRAINT "FK_FacultySharingRules_faculty_FacultyId" FOREIGN KEY ("FacultyId") REFERENCES public.faculty("Id") ON DELETE CASCADE;

--
-- TOC entry 5905 (class 2606 OID 106754)
-- Name: FacultySharingRules FK_FacultySharingRules_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."FacultySharingRules"
    ADD CONSTRAINT "FK_FacultySharingRules_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5952 (class 2606 OID 108075)
-- Name: FranchiseCommissionEntries FK_FranchiseCommissionEntries_Franchises_FranchiseId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."FranchiseCommissionEntries"
    ADD CONSTRAINT "FK_FranchiseCommissionEntries_Franchises_FranchiseId" FOREIGN KEY ("FranchiseId") REFERENCES public."Franchises"("Id") ON DELETE CASCADE;

--
-- TOC entry 5953 (class 2606 OID 108099)
-- Name: FranchiseCommissions FK_FranchiseCommissions_Franchises_FranchiseId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."FranchiseCommissions"
    ADD CONSTRAINT "FK_FranchiseCommissions_Franchises_FranchiseId" FOREIGN KEY ("FranchiseId") REFERENCES public."Franchises"("Id") ON DELETE CASCADE;

--
-- TOC entry 5954 (class 2606 OID 108104)
-- Name: FranchiseCommissions FK_FranchiseCommissions_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."FranchiseCommissions"
    ADD CONSTRAINT "FK_FranchiseCommissions_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5890 (class 2606 OID 106564)
-- Name: Franchises FK_Franchises_users_AdminUserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Franchises"
    ADD CONSTRAINT "FK_Franchises_users_AdminUserId" FOREIGN KEY ("AdminUserId") REFERENCES public.users("Id");

--
-- TOC entry 5912 (class 2606 OID 106887)
-- Name: Invoices FK_Invoices_orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Invoices"
    ADD CONSTRAINT "FK_Invoices_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public.orders("Id") ON DELETE CASCADE;

--
-- TOC entry 5891 (class 2606 OID 106584)
-- Name: Leads FK_Leads_users_AssignedToId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Leads"
    ADD CONSTRAINT "FK_Leads_users_AssignedToId" FOREIGN KEY ("AssignedToId") REFERENCES public.users("Id");

--
-- TOC entry 5917 (class 2606 OID 106962)
-- Name: OrderItems FK_OrderItems_ProductModes_ProductModeId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."OrderItems"
    ADD CONSTRAINT "FK_OrderItems_ProductModes_ProductModeId" FOREIGN KEY ("ProductModeId") REFERENCES public."ProductModes"("Id");

--
-- TOC entry 5918 (class 2606 OID 106967)
-- Name: OrderItems FK_OrderItems_orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."OrderItems"
    ADD CONSTRAINT "FK_OrderItems_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public.orders("Id") ON DELETE CASCADE;

--
-- TOC entry 5919 (class 2606 OID 106972)
-- Name: OrderItems FK_OrderItems_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."OrderItems"
    ADD CONSTRAINT "FK_OrderItems_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5913 (class 2606 OID 106908)
-- Name: Payments FK_Payments_orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Payments"
    ADD CONSTRAINT "FK_Payments_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public.orders("Id") ON DELETE CASCADE;

--
-- TOC entry 5962 (class 2606 OID 108344)
-- Name: ProductBookPreviews FK_ProductBookPreviews_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ProductBookPreviews"
    ADD CONSTRAINT "FK_ProductBookPreviews_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5906 (class 2606 OID 106772)
-- Name: ProductFaculty FK_ProductFaculty_faculty_FacultyId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ProductFaculty"
    ADD CONSTRAINT "FK_ProductFaculty_faculty_FacultyId" FOREIGN KEY ("FacultyId") REFERENCES public.faculty("Id") ON DELETE CASCADE;

--
-- TOC entry 5907 (class 2606 OID 106777)
-- Name: ProductFaculty FK_ProductFaculty_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ProductFaculty"
    ADD CONSTRAINT "FK_ProductFaculty_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5908 (class 2606 OID 106798)
-- Name: ProductImages FK_ProductImages_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ProductImages"
    ADD CONSTRAINT "FK_ProductImages_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5909 (class 2606 OID 106818)
-- Name: ProductInclusions FK_ProductInclusions_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ProductInclusions"
    ADD CONSTRAINT "FK_ProductInclusions_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5910 (class 2606 OID 106841)
-- Name: ProductModes FK_ProductModes_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ProductModes"
    ADD CONSTRAINT "FK_ProductModes_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5964 (class 2606 OID 108393)
-- Name: ProductTestimonials FK_ProductTestimonials_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ProductTestimonials"
    ADD CONSTRAINT "FK_ProductTestimonials_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5961 (class 2606 OID 108318)
-- Name: ScheduledTaskRuns FK_ScheduledTaskRuns_ScheduledTasks_ScheduledTaskId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."ScheduledTaskRuns"
    ADD CONSTRAINT "FK_ScheduledTaskRuns_ScheduledTasks_ScheduledTaskId" FOREIGN KEY ("ScheduledTaskId") REFERENCES public."ScheduledTasks"("Id") ON DELETE CASCADE;

--
-- TOC entry 5911 (class 2606 OID 106861)
-- Name: Testimonials FK_Testimonials_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Testimonials"
    ADD CONSTRAINT "FK_Testimonials_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id");

--
-- TOC entry 5899 (class 2606 OID 106691)
-- Name: UserRoles FK_UserRoles_Franchises_FranchiseId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserRoles"
    ADD CONSTRAINT "FK_UserRoles_Franchises_FranchiseId" FOREIGN KEY ("FranchiseId") REFERENCES public."Franchises"("Id");

--
-- TOC entry 5900 (class 2606 OID 106696)
-- Name: UserRoles FK_UserRoles_Roles_RoleId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserRoles"
    ADD CONSTRAINT "FK_UserRoles_Roles_RoleId" FOREIGN KEY ("RoleId") REFERENCES public."Roles"("Id") ON DELETE CASCADE;

--
-- TOC entry 5901 (class 2606 OID 106701)
-- Name: UserRoles FK_UserRoles_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."UserRoles"
    ADD CONSTRAINT "FK_UserRoles_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;

--
-- TOC entry 5955 (class 2606 OID 108130)
-- Name: WalletRecharges FK_WalletRecharges_Franchises_FranchiseId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."WalletRecharges"
    ADD CONSTRAINT "FK_WalletRecharges_Franchises_FranchiseId" FOREIGN KEY ("FranchiseId") REFERENCES public."Franchises"("Id") ON DELETE CASCADE;

--
-- TOC entry 5925 (class 2606 OID 107150)
-- Name: affiliate_referrals FK_affiliate_referrals_affiliates_AffiliateId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.affiliate_referrals
    ADD CONSTRAINT "FK_affiliate_referrals_affiliates_AffiliateId" FOREIGN KEY ("AffiliateId") REFERENCES public.affiliates("Id") ON DELETE CASCADE;

--
-- TOC entry 5926 (class 2606 OID 107155)
-- Name: affiliate_referrals FK_affiliate_referrals_orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.affiliate_referrals
    ADD CONSTRAINT "FK_affiliate_referrals_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public.orders("Id") ON DELETE CASCADE;

--
-- TOC entry 5924 (class 2606 OID 107128)
-- Name: affiliates FK_affiliates_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.affiliates
    ADD CONSTRAINT "FK_affiliates_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id");

--
-- TOC entry 5944 (class 2606 OID 107803)
-- Name: approval_comments FK_approval_comments_approval_requests_ApprovalRequestId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.approval_comments
    ADD CONSTRAINT "FK_approval_comments_approval_requests_ApprovalRequestId" FOREIGN KEY ("ApprovalRequestId") REFERENCES public.approval_requests("Id") ON DELETE CASCADE;

--
-- TOC entry 5945 (class 2606 OID 107824)
-- Name: approval_steps FK_approval_steps_approval_requests_ApprovalRequestId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.approval_steps
    ADD CONSTRAINT "FK_approval_steps_approval_requests_ApprovalRequestId" FOREIGN KEY ("ApprovalRequestId") REFERENCES public.approval_requests("Id") ON DELETE CASCADE;

--
-- TOC entry 5930 (class 2606 OID 107443)
-- Name: checkout_attribute_values FK_checkout_attribute_values_checkout_attributes_CheckoutAttri~; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checkout_attribute_values
    ADD CONSTRAINT "FK_checkout_attribute_values_checkout_attributes_CheckoutAttri~" FOREIGN KEY ("CheckoutAttributeId") REFERENCES public.checkout_attributes("Id") ON DELETE CASCADE;

--
-- TOC entry 5950 (class 2606 OID 108004)
-- Name: customer_addresses FK_customer_addresses_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customer_addresses
    ADD CONSTRAINT "FK_customer_addresses_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;

--
-- TOC entry 5889 (class 2606 OID 106542)
-- Name: faculty FK_faculty_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.faculty
    ADD CONSTRAINT "FK_faculty_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id");

--
-- TOC entry 5927 (class 2606 OID 107269)
-- Name: franchise_ledger FK_franchise_ledger_Franchises_FranchiseId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.franchise_ledger
    ADD CONSTRAINT "FK_franchise_ledger_Franchises_FranchiseId" FOREIGN KEY ("FranchiseId") REFERENCES public."Franchises"("Id") ON DELETE CASCADE;

--
-- TOC entry 5974 (class 2606 OID 108642)
-- Name: invoice_line_items FK_invoice_line_items_invoices_InvoiceId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invoice_line_items
    ADD CONSTRAINT "FK_invoice_line_items_invoices_InvoiceId" FOREIGN KEY ("InvoiceId") REFERENCES public.invoices("Id") ON DELETE CASCADE;

--
-- TOC entry 5973 (class 2606 OID 108608)
-- Name: invoices FK_invoices_orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.invoices
    ADD CONSTRAINT "FK_invoices_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public.orders("Id") ON DELETE RESTRICT;

--
-- TOC entry 5951 (class 2606 OID 108028)
-- Name: menu_items FK_menu_items_menu_items_ParentId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.menu_items
    ADD CONSTRAINT "FK_menu_items_menu_items_ParentId" FOREIGN KEY ("ParentId") REFERENCES public.menu_items("Id") ON DELETE RESTRICT;

--
-- TOC entry 5943 (class 2606 OID 107760)
-- Name: note_attachments FK_note_attachments_order_notes_OrderNoteId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.note_attachments
    ADD CONSTRAINT "FK_note_attachments_order_notes_OrderNoteId" FOREIGN KEY ("OrderNoteId") REFERENCES public.order_notes("Id") ON DELETE CASCADE;

--
-- TOC entry 5939 (class 2606 OID 107618)
-- Name: order_notes FK_order_notes_orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.order_notes
    ADD CONSTRAINT "FK_order_notes_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public.orders("Id") ON DELETE CASCADE;

--
-- TOC entry 5895 (class 2606 OID 106658)
-- Name: orders FK_orders_Coupons_CouponId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT "FK_orders_Coupons_CouponId" FOREIGN KEY ("CouponId") REFERENCES public."Coupons"("Id");

--
-- TOC entry 5896 (class 2606 OID 106663)
-- Name: orders FK_orders_Franchises_FranchiseId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT "FK_orders_Franchises_FranchiseId" FOREIGN KEY ("FranchiseId") REFERENCES public."Franchises"("Id");

--
-- TOC entry 5897 (class 2606 OID 106668)
-- Name: orders FK_orders_users_CreatedById; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT "FK_orders_users_CreatedById" FOREIGN KEY ("CreatedById") REFERENCES public.users("Id");

--
-- TOC entry 5898 (class 2606 OID 106673)
-- Name: orders FK_orders_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT "FK_orders_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id");

--
-- TOC entry 5940 (class 2606 OID 107681)
-- Name: payment_transactions FK_payment_transactions_orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payment_transactions
    ADD CONSTRAINT "FK_payment_transactions_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public.orders("Id") ON DELETE CASCADE;

--
-- TOC entry 5948 (class 2606 OID 107925)
-- Name: payout_items FK_payout_items_payouts_PayoutId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payout_items
    ADD CONSTRAINT "FK_payout_items_payouts_PayoutId" FOREIGN KEY ("PayoutId") REFERENCES public.payouts("Id") ON DELETE CASCADE;

--
-- TOC entry 5946 (class 2606 OID 107882)
-- Name: payouts FK_payouts_settlement_batches_SettlementBatchId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payouts
    ADD CONSTRAINT "FK_payouts_settlement_batches_SettlementBatchId" FOREIGN KEY ("SettlementBatchId") REFERENCES public.settlement_batches("Id") ON DELETE SET NULL;

--
-- TOC entry 5960 (class 2606 OID 108262)
-- Name: permission_audit_logs FK_permission_audit_logs_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.permission_audit_logs
    ADD CONSTRAINT "FK_permission_audit_logs_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;

--
-- TOC entry 5931 (class 2606 OID 107465)
-- Name: predefined_product_attribute_values FK_predefined_product_attribute_values_product_attributes_Prod~; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.predefined_product_attribute_values
    ADD CONSTRAINT "FK_predefined_product_attribute_values_product_attributes_Prod~" FOREIGN KEY ("ProductAttributeId") REFERENCES public.product_attributes("Id") ON DELETE CASCADE;

--
-- TOC entry 5932 (class 2606 OID 107488)
-- Name: product_attribute_mappings FK_product_attribute_mappings_product_attributes_ProductAttrib~; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_attribute_mappings
    ADD CONSTRAINT "FK_product_attribute_mappings_product_attributes_ProductAttrib~" FOREIGN KEY ("ProductAttributeId") REFERENCES public.product_attributes("Id") ON DELETE RESTRICT;

--
-- TOC entry 5933 (class 2606 OID 107493)
-- Name: product_attribute_mappings FK_product_attribute_mappings_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_attribute_mappings
    ADD CONSTRAINT "FK_product_attribute_mappings_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5935 (class 2606 OID 107533)
-- Name: product_attribute_values FK_product_attribute_values_product_attribute_mappings_Product~; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_attribute_values
    ADD CONSTRAINT "FK_product_attribute_values_product_attribute_mappings_Product~" FOREIGN KEY ("ProductAttributeMappingId") REFERENCES public.product_attribute_mappings("Id") ON DELETE CASCADE;

--
-- TOC entry 5965 (class 2606 OID 108414)
-- Name: product_categories FK_product_categories_Categories_CategoryId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_categories
    ADD CONSTRAINT "FK_product_categories_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES public."Categories"("Id") ON DELETE CASCADE;

--
-- TOC entry 5966 (class 2606 OID 108419)
-- Name: product_categories FK_product_categories_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_categories
    ADD CONSTRAINT "FK_product_categories_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5956 (class 2606 OID 108189)
-- Name: product_recommendations FK_product_recommendations_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_recommendations
    ADD CONSTRAINT "FK_product_recommendations_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE RESTRICT;

--
-- TOC entry 5957 (class 2606 OID 108194)
-- Name: product_recommendations FK_product_recommendations_products_RecommendedProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_recommendations
    ADD CONSTRAINT "FK_product_recommendations_products_RecommendedProductId" FOREIGN KEY ("RecommendedProductId") REFERENCES public.products("Id") ON DELETE RESTRICT;

--
-- TOC entry 5968 (class 2606 OID 108466)
-- Name: product_serial_key_configs FK_product_serial_key_configs_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_serial_key_configs
    ADD CONSTRAINT "FK_product_serial_key_configs_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5937 (class 2606 OID 107573)
-- Name: product_specification_attributes FK_product_specification_attributes_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_specification_attributes
    ADD CONSTRAINT "FK_product_specification_attributes_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5938 (class 2606 OID 107578)
-- Name: product_specification_attributes FK_product_specification_attributes_specification_attribute_op~; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_specification_attributes
    ADD CONSTRAINT "FK_product_specification_attributes_specification_attribute_op~" FOREIGN KEY ("SpecificationAttributeOptionId") REFERENCES public.specification_attribute_options("Id") ON DELETE RESTRICT;

--
-- TOC entry 5949 (class 2606 OID 107979)
-- Name: product_videos FK_product_videos_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_videos
    ADD CONSTRAINT "FK_product_videos_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5892 (class 2606 OID 106617)
-- Name: products FK_products_Categories_CategoryId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT "FK_products_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES public."Categories"("Id");

--
-- TOC entry 5893 (class 2606 OID 106622)
-- Name: products FK_products_Subjects_SubjectId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT "FK_products_Subjects_SubjectId" FOREIGN KEY ("SubjectId") REFERENCES public."Subjects"("Id");

--
-- TOC entry 5894 (class 2606 OID 106627)
-- Name: products FK_products_faculty_PrimaryFacultyId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT "FK_products_faculty_PrimaryFacultyId" FOREIGN KEY ("PrimaryFacultyId") REFERENCES public.faculty("Id");

--
-- TOC entry 5942 (class 2606 OID 107728)
-- Name: refund_items FK_refund_items_refunds_RefundId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refund_items
    ADD CONSTRAINT "FK_refund_items_refunds_RefundId" FOREIGN KEY ("RefundId") REFERENCES public.refunds("Id") ON DELETE CASCADE;

--
-- TOC entry 5941 (class 2606 OID 107705)
-- Name: refunds FK_refunds_orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refunds
    ADD CONSTRAINT "FK_refunds_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public.orders("Id") ON DELETE CASCADE;

--
-- TOC entry 5928 (class 2606 OID 107293)
-- Name: return_requests FK_return_requests_orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.return_requests
    ADD CONSTRAINT "FK_return_requests_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public.orders("Id") ON DELETE CASCADE;

--
-- TOC entry 5920 (class 2606 OID 107069)
-- Name: reviews FK_reviews_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reviews
    ADD CONSTRAINT "FK_reviews_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5921 (class 2606 OID 107074)
-- Name: reviews FK_reviews_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reviews
    ADD CONSTRAINT "FK_reviews_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;

--
-- TOC entry 5958 (class 2606 OID 108221)
-- Name: role_permissions FK_role_permissions_Roles_RoleId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.role_permissions
    ADD CONSTRAINT "FK_role_permissions_Roles_RoleId" FOREIGN KEY ("RoleId") REFERENCES public."Roles"("Id") ON DELETE CASCADE;

--
-- TOC entry 5969 (class 2606 OID 108511)
-- Name: serial_key_records FK_serial_key_records_OrderItems_OrderItemId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.serial_key_records
    ADD CONSTRAINT "FK_serial_key_records_OrderItems_OrderItemId" FOREIGN KEY ("OrderItemId") REFERENCES public."OrderItems"("Id") ON DELETE RESTRICT;

--
-- TOC entry 5970 (class 2606 OID 108516)
-- Name: serial_key_records FK_serial_key_records_orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.serial_key_records
    ADD CONSTRAINT "FK_serial_key_records_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public.orders("Id") ON DELETE RESTRICT;

--
-- TOC entry 5971 (class 2606 OID 108521)
-- Name: serial_key_records FK_serial_key_records_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.serial_key_records
    ADD CONSTRAINT "FK_serial_key_records_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE RESTRICT;

--
-- TOC entry 5972 (class 2606 OID 108526)
-- Name: serial_key_records FK_serial_key_records_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.serial_key_records
    ADD CONSTRAINT "FK_serial_key_records_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE SET NULL;

--
-- TOC entry 5947 (class 2606 OID 107905)
-- Name: settlement_adjustments FK_settlement_adjustments_settlement_batches_SettlementBatchId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.settlement_adjustments
    ADD CONSTRAINT "FK_settlement_adjustments_settlement_batches_SettlementBatchId" FOREIGN KEY ("SettlementBatchId") REFERENCES public.settlement_batches("Id") ON DELETE CASCADE;

--
-- TOC entry 5929 (class 2606 OID 107313)
-- Name: shipments FK_shipments_orders_OrderId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shipments
    ADD CONSTRAINT "FK_shipments_orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES public.orders("Id") ON DELETE CASCADE;

--
-- TOC entry 5967 (class 2606 OID 108438)
-- Name: special_price_audits FK_special_price_audits_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.special_price_audits
    ADD CONSTRAINT "FK_special_price_audits_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5936 (class 2606 OID 107552)
-- Name: specification_attribute_options FK_specification_attribute_options_specification_attributes_Sp~; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.specification_attribute_options
    ADD CONSTRAINT "FK_specification_attribute_options_specification_attributes_Sp~" FOREIGN KEY ("SpecificationAttributeId") REFERENCES public.specification_attributes("Id") ON DELETE CASCADE;

--
-- TOC entry 5934 (class 2606 OID 107511)
-- Name: specification_attributes FK_specification_attributes_specification_attribute_groups_Spe~; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.specification_attributes
    ADD CONSTRAINT "FK_specification_attributes_specification_attribute_groups_Spe~" FOREIGN KEY ("SpecificationAttributeGroupId") REFERENCES public.specification_attribute_groups("Id") ON DELETE SET NULL;

--
-- TOC entry 5959 (class 2606 OID 108240)
-- Name: user_permission_overrides FK_user_permission_overrides_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_permission_overrides
    ADD CONSTRAINT "FK_user_permission_overrides_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;

--
-- TOC entry 5887 (class 2606 OID 106498)
-- Name: users FK_users_users_ReferredById; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT "FK_users_users_ReferredById" FOREIGN KEY ("ReferredById") REFERENCES public.users("Id");

--
-- TOC entry 5922 (class 2606 OID 107095)
-- Name: wishlist_items FK_wishlist_items_products_ProductId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.wishlist_items
    ADD CONSTRAINT "FK_wishlist_items_products_ProductId" FOREIGN KEY ("ProductId") REFERENCES public.products("Id") ON DELETE CASCADE;

--
-- TOC entry 5923 (class 2606 OID 107100)
-- Name: wishlist_items FK_wishlist_items_users_UserId; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.wishlist_items
    ADD CONSTRAINT "FK_wishlist_items_users_UserId" FOREIGN KEY ("UserId") REFERENCES public.users("Id") ON DELETE CASCADE;

-- Completed on 2026-06-24 13:05:26

--
-- PostgreSQL database dump complete
--


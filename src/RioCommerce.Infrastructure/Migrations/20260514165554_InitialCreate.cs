using System;
using RioCommerce.Core.Enums;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace RioCommerce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:content_status", "published,draft,scheduled,archived")
                .Annotation("Npgsql:Enum:course_level", "ca_foundation,ca_intermediate,books,test_series")
                .Annotation("Npgsql:Enum:course_type", "regular,fastrack,combo,face_to_face,exam_oriented")
                .Annotation("Npgsql:Enum:lead_status", "new,contacted,follow_up,converted,lost")
                .Annotation("Npgsql:Enum:lecture_mode", "live_streaming,recorded,live_plus_recorded,pendrive,face_to_face")
                .Annotation("Npgsql:Enum:order_source", "website,backend_counter,backend_phone,backend_whats_app,franchise,franchise_referral")
                .Annotation("Npgsql:Enum:order_status", "draft,pending,confirmed,processing,activated,delivered,cancelled,refunded")
                .Annotation("Npgsql:Enum:payment_mode", "razorpay,easebuzz,ccavenue,upi,bank_transfer,cash,cheque,razorpay_link,emi")
                .Annotation("Npgsql:Enum:payment_status", "pending,success,failed,refunded,partial_refund")
                .Annotation("Npgsql:Enum:product_status", "active,draft,archived")
                .Annotation("Npgsql:Enum:sharing_type", "percentage,fixed_amount")
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:uuid-ossp", ",,");

            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    ValueType = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Banners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: false),
                    LinkUrl = table.Column<string>(type: "text", nullable: true),
                    Placement = table.Column<string>(type: "text", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Banners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categories_Categories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Categories",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Coupons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    CouponType = table.Column<SharingType>(type: "sharing_type", nullable: false),
                    Value = table.Column<decimal>(type: "numeric", nullable: false),
                    MaxDiscount = table.Column<decimal>(type: "numeric", nullable: true),
                    MinOrder = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalLimit = table.Column<int>(type: "integer", nullable: true),
                    PerUserLimit = table.Column<int>(type: "integer", nullable: false),
                    TotalUsed = table.Column<int>(type: "integer", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coupons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Level = table.Column<CourseLevel>(type: "course_level", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "citext", nullable: true),
                    Phone = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    PhoneCountry = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FirstName = table.Column<string>(type: "text", nullable: true),
                    LastName = table.Column<string>(type: "text", nullable: true),
                    AvatarUrl = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    GoogleId = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "text", nullable: true),
                    CourseInterest = table.Column<CourseLevel>(type: "course_level", nullable: true),
                    ReferralCode = table.Column<string>(type: "text", nullable: true),
                    ReferredById = table.Column<Guid>(type: "uuid", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LoginCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_users_users_ReferredById",
                        column: x => x.ReferredById,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BlogPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Excerpt = table.Column<string>(type: "text", nullable: true),
                    Body = table.Column<string>(type: "text", nullable: false),
                    FeaturedImage = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: true),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<ContentStatus>(type: "content_status", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SeoTitle = table.Column<string>(type: "text", nullable: true),
                    SeoDescription = table.Column<string>(type: "text", nullable: true),
                    Views = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlogPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlogPosts_users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "faculty",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ShortCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Designation = table.Column<string>(type: "text", nullable: true),
                    Qualifications = table.Column<string>(type: "text", nullable: true),
                    Subjects = table.Column<string[]>(type: "text[]", nullable: true),
                    Bio = table.Column<string>(type: "text", nullable: true),
                    PhotoUrl = table.Column<string>(type: "text", nullable: true),
                    YoutubeUrl = table.Column<string>(type: "text", nullable: true),
                    WhatsappNumber = table.Column<string>(type: "text", nullable: true),
                    BankName = table.Column<string>(type: "text", nullable: true),
                    BankAccount = table.Column<string>(type: "text", nullable: true),
                    BankIfsc = table.Column<string>(type: "text", nullable: true),
                    PanNumber = table.Column<string>(type: "text", nullable: true),
                    Gstin = table.Column<string>(type: "text", nullable: true),
                    GstRegistered = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faculty", x => x.Id);
                    table.ForeignKey(
                        name: "FK_faculty_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Franchises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    City = table.Column<string>(type: "text", nullable: false),
                    ContactPerson = table.Column<string>(type: "text", nullable: true),
                    ContactPhone = table.Column<string>(type: "text", nullable: true),
                    WalletBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    AdminUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Franchises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Franchises_users_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Leads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    City = table.Column<string>(type: "text", nullable: true),
                    CourseInterest = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<LeadStatus>(type: "lead_status", nullable: false),
                    AssignedToId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Leads_users_AssignedToId",
                        column: x => x.AssignedToId,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Slug = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ShortDesc = table.Column<string>(type: "text", nullable: true),
                    FullDesc = table.Column<string>(type: "text", nullable: true),
                    Level = table.Column<CourseLevel>(type: "course_level", nullable: false),
                    CourseType = table.Column<CourseType>(type: "course_type", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    PrimaryFacultyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Mrp = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    SellingPrice = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    FranchisePrice = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    GstRate = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    GstInclusive = table.Column<bool>(type: "boolean", nullable: false),
                    SacCode = table.Column<string>(type: "text", nullable: false),
                    BatchStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ApplicableAttempts = table.Column<string[]>(type: "text[]", nullable: true),
                    TotalLectures = table.Column<string>(type: "text", nullable: true),
                    TotalHours = table.Column<string>(type: "text", nullable: true),
                    BooksInfo = table.Column<string>(type: "text", nullable: true),
                    ExamOrientedInfo = table.Column<string>(type: "text", nullable: true),
                    AdditionalDetails = table.Column<string>(type: "text", nullable: true),
                    SeoTitle = table.Column<string>(type: "text", nullable: true),
                    SeoDescription = table.Column<string>(type: "text", nullable: true),
                    Badge = table.Column<string>(type: "text", nullable: true),
                    IsFeatured = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<ProductStatus>(type: "product_status", nullable: false),
                    TotalOrders = table.Column<int>(type: "integer", nullable: false),
                    TotalViews = table.Column<int>(type: "integer", nullable: false),
                    AvgRating = table.Column<decimal>(type: "numeric(2,1)", precision: 2, scale: 1, nullable: false),
                    RatingCount = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_products_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_products_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_products_faculty_PrimaryFacultyId",
                        column: x => x.PrimaryFacultyId,
                        principalTable: "faculty",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    StudentName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StudentPhone = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    StudentEmail = table.Column<string>(type: "text", nullable: true),
                    StudentCity = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<OrderSource>(type: "order_source", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CouponId = table.Column<Guid>(type: "uuid", nullable: true),
                    CouponCode = table.Column<string>(type: "text", nullable: true),
                    GstAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    CgstAmount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    SgstAmount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    IgstAmount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    BillingName = table.Column<string>(type: "text", nullable: true),
                    BillingAddress = table.Column<string>(type: "text", nullable: true),
                    BillingCity = table.Column<string>(type: "text", nullable: true),
                    BillingState = table.Column<string>(type: "text", nullable: true),
                    BillingPincode = table.Column<string>(type: "text", nullable: true),
                    GstNumber = table.Column<string>(type: "text", nullable: true),
                    GstClassification = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<OrderStatus>(type: "order_status", nullable: false),
                    PaymentStatus = table.Column<PaymentStatus>(type: "payment_status", nullable: false),
                    PaymentMode = table.Column<PaymentMode>(type: "payment_mode", nullable: true),
                    InternalNotes = table.Column<string>(type: "text", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_orders_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_orders_Franchises_FranchiseId",
                        column: x => x.FranchiseId,
                        principalTable: "Franchises",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_orders_users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_orders_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    FranchiseId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRoles_Franchises_FranchiseId",
                        column: x => x.FranchiseId,
                        principalTable: "Franchises",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Enrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Mode = table.Column<LectureMode>(type: "lecture_mode", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ProgressPct = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Enrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Enrollments_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Enrollments_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FacultySharingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacultyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShareType = table.Column<SharingType>(type: "sharing_type", nullable: false),
                    ShareValue = table.Column<decimal>(type: "numeric", nullable: false),
                    EffectiveAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacultySharingRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FacultySharingRules_faculty_FacultyId",
                        column: x => x.FacultyId,
                        principalTable: "faculty",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FacultySharingRules_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductFaculty",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacultyId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductFaculty", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductFaculty_faculty_FacultyId",
                        column: x => x.FacultyId,
                        principalTable: "faculty",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductFaculty_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: false),
                    AltText = table.Column<string>(type: "text", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductImages_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductInclusions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductInclusions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductInclusions_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductModes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModeName = table.Column<string>(type: "text", nullable: false),
                    ModeType = table.Column<LectureMode>(type: "lecture_mode", nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductModes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductModes_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Testimonials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentName = table.Column<string>(type: "text", nullable: false),
                    Location = table.Column<string>(type: "text", nullable: true),
                    ScoreText = table.Column<string>(type: "text", nullable: true),
                    TextContent = table.Column<string>(type: "text", nullable: true),
                    Rating = table.Column<int>(type: "integer", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Testimonials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Testimonials_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "text", nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    CgstAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    SgstAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    IgstAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    PdfUrl = table.Column<string>(type: "text", nullable: true),
                    IsCancelled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    PaymentMode = table.Column<PaymentMode>(type: "payment_mode", nullable: false),
                    Status = table.Column<PaymentStatus>(type: "payment_status", nullable: false),
                    GatewayName = table.Column<string>(type: "text", nullable: true),
                    GatewayOrderId = table.Column<string>(type: "text", nullable: true),
                    GatewayPaymentId = table.Column<string>(type: "text", nullable: true),
                    GatewaySignature = table.Column<string>(type: "text", nullable: true),
                    BankRef = table.Column<string>(type: "text", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CartItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductModeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CartItems_ProductModes_ProductModeId",
                        column: x => x.ProductModeId,
                        principalTable: "ProductModes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CartItems_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CartItems_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductModeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductTitle = table.Column<string>(type: "text", nullable: false),
                    ModeName = table.Column<string>(type: "text", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    Discount = table.Column<decimal>(type: "numeric", nullable: false),
                    GstRate = table.Column<decimal>(type: "numeric", nullable: true),
                    GstAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric", nullable: false),
                    Attempt = table.Column<string>(type: "text", nullable: true),
                    IsActivated = table.Column<bool>(type: "boolean", nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderItems_ProductModes_ProductModeId",
                        column: x => x.ProductModeId,
                        principalTable: "ProductModes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderItems_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderItems_products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "Description", "DisplayOrder", "ImageUrl", "IsActive", "Name", "ParentId", "Slug" },
                values: new object[,]
                {
                    { new Guid("22222222-2222-2222-2222-222222222201"), null, 1, null, true, "CA Foundation", null, "ca-foundation" },
                    { new Guid("22222222-2222-2222-2222-222222222202"), null, 2, null, true, "CA Intermediate", null, "ca-intermediate" },
                    { new Guid("22222222-2222-2222-2222-222222222203"), null, 3, null, true, "Books", null, "books" },
                    { new Guid("22222222-2222-2222-2222-222222222204"), null, 4, null, true, "Test Series", null, "test-series" }
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "Description", "DisplayName", "IsActive", "IsSystem", "Name" },
                values: new object[,]
                {
                    { new Guid("44444444-4444-4444-4444-444444444401"), null, "Super Admin", true, true, "super_admin" },
                    { new Guid("44444444-4444-4444-4444-444444444402"), null, "Admin", true, true, "admin" },
                    { new Guid("44444444-4444-4444-4444-444444444403"), null, "Student", true, true, "student" },
                    { new Guid("44444444-4444-4444-4444-444444444404"), null, "Faculty", true, true, "faculty" },
                    { new Guid("44444444-4444-4444-4444-444444444405"), null, "Franchise Admin", true, true, "franchise_admin" },
                    { new Guid("44444444-4444-4444-4444-444444444406"), null, "Operations", true, true, "operations" }
                });

            migrationBuilder.InsertData(
                table: "Subjects",
                columns: new[] { "Id", "DisplayOrder", "IsActive", "Level", "Name", "Slug" },
                values: new object[,]
                {
                    { new Guid("33333333-3333-3333-3333-333333333301"), 1, true, CourseLevel.Beginner, "Economics", "economics" },
                    { new Guid("33333333-3333-3333-3333-333333333302"), 2, true, CourseLevel.Beginner, "Law", "law" },
                    { new Guid("33333333-3333-3333-3333-333333333303"), 3, true, CourseLevel.Beginner, "Accounts", "accounts" },
                    { new Guid("33333333-3333-3333-3333-333333333304"), 4, true, CourseLevel.Beginner, "Maths (QA)", "maths-qa" },
                    { new Guid("33333333-3333-3333-3333-333333333305"), 5, true, CourseLevel.Intermediate, "Costing", "costing" },
                    { new Guid("33333333-3333-3333-3333-333333333306"), 6, true, CourseLevel.Intermediate, "Audit", "audit" },
                    { new Guid("33333333-3333-3333-3333-333333333307"), 7, true, CourseLevel.Intermediate, "Direct Tax", "direct-tax" },
                    { new Guid("33333333-3333-3333-3333-333333333308"), 8, true, CourseLevel.Intermediate, "Adv. Accounting", "adv-accounting" }
                });

            migrationBuilder.InsertData(
                table: "app_settings",
                columns: new[] { "Key", "Category", "Description", "UpdatedAt", "Value", "ValueType" },
                values: new object[,]
                {
                    { "company_address", "general", null, new DateTime(2026, 5, 14, 16, 55, 54, 162, DateTimeKind.Utc).AddTicks(8823), "Prestige Point, Bajirao Road, Pune 411002", "string" },
                    { "company_email", "general", null, new DateTime(2026, 5, 14, 16, 55, 54, 162, DateTimeKind.Utc).AddTicks(8822), "enquiry@riocommerce.com", "string" },
                    { "company_name", "general", null, new DateTime(2026, 5, 14, 16, 55, 54, 162, DateTimeKind.Utc).AddTicks(8041), "RioCommerce", "string" },
                    { "company_phone_1", "general", null, new DateTime(2026, 5, 14, 16, 55, 54, 162, DateTimeKind.Utc).AddTicks(8817), "9975242929", "string" },
                    { "company_phone_2", "general", null, new DateTime(2026, 5, 14, 16, 55, 54, 162, DateTimeKind.Utc).AddTicks(8820), "9766582929", "string" },
                    { "company_phone_3", "general", null, new DateTime(2026, 5, 14, 16, 55, 54, 162, DateTimeKind.Utc).AddTicks(8821), "9766182929", "string" }
                });

            migrationBuilder.InsertData(
                table: "faculty",
                columns: new[] { "Id", "BankAccount", "BankIfsc", "BankName", "Bio", "Designation", "DisplayName", "DisplayOrder", "GstRegistered", "Gstin", "IsActive", "PanNumber", "PhotoUrl", "Qualifications", "ShortCode", "Subjects", "UserId", "WhatsappNumber", "YoutubeUrl" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111101"), null, null, null, "Founder of RioCommerce. 1,00,000+ students taught.", "Founder, RioCommerce", "CA Harshad Jaju", 1, false, null, true, null, null, null, "HJ", new[] { "Economics", "Costing", "Audit" }, null, null, null },
                    { new Guid("11111111-1111-1111-1111-111111111102"), null, null, null, null, "LAW", "CA CS Tejal Katariya", 2, false, null, true, null, null, null, "TK", new[] { "Law" }, null, null, null },
                    { new Guid("11111111-1111-1111-1111-111111111103"), null, null, null, null, "Maths (QA)", "Prof. Raj Awate", 3, false, null, true, null, null, null, "RA", new[] { "Maths" }, null, null, null },
                    { new Guid("11111111-1111-1111-1111-111111111104"), null, null, null, null, "Accounts", "CA CS Harish Mathariya", 4, false, null, true, null, null, null, "HM", new[] { "Accounts", "Adv. Accounting" }, null, null, null },
                    { new Guid("11111111-1111-1111-1111-111111111105"), null, null, null, null, "FM & SM", "CA Nipurn Modi", 5, false, null, true, null, null, null, "NM", new[] { "FM", "SM" }, null, null, null },
                    { new Guid("11111111-1111-1111-1111-111111111106"), null, null, null, null, "Direct Tax", "CA Vedant Bajaj", 6, false, null, true, null, null, null, "VB", new[] { "Direct Tax" }, null, null, null }
                });

            migrationBuilder.InsertData(
                table: "users",
                columns: new[] { "Id", "AvatarUrl", "City", "CourseInterest", "Email", "FirstName", "FullName", "GoogleId", "IsActive", "IsVerified", "LastLoginAt", "LastName", "LoginCount", "PasswordHash", "Phone", "PhoneCountry", "ReferralCode", "ReferredById", "State" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), null, "Pune", null, "admin@riocommerce.com", null, "CA Harshad Jaju", null, true, true, null, null, 0, "$2a$11$K3rKZvGBCsRKGMbKe1F6UOHpGfLCH3RGZsFTFn3MYVJHqKMOyFm6y", "9975242929", "+91", null, null, "Maharashtra" });

            migrationBuilder.InsertData(
                table: "UserRoles",
                columns: new[] { "Id", "FranchiseId", "IsActive", "RoleId", "UserId" },
                values: new object[] { new Guid("55555555-5555-5555-5555-555555555501"), null, true, new Guid("44444444-4444-4444-4444-444444444401"), new Guid("00000000-0000-0000-0000-000000000001") });

            migrationBuilder.CreateIndex(
                name: "IX_BlogPosts_AuthorId",
                table: "BlogPosts",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_ProductId",
                table: "CartItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_ProductModeId",
                table: "CartItems",
                column: "ProductModeId");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_UserId",
                table: "CartItems",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentId",
                table: "Categories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_ProductId",
                table: "Enrollments",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Enrollments_UserId",
                table: "Enrollments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_faculty_ShortCode",
                table: "faculty",
                column: "ShortCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_faculty_UserId",
                table: "faculty",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacultySharingRules_FacultyId",
                table: "FacultySharingRules",
                column: "FacultyId");

            migrationBuilder.CreateIndex(
                name: "IX_FacultySharingRules_ProductId",
                table: "FacultySharingRules",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Franchises_AdminUserId",
                table: "Franchises",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OrderId",
                table: "Invoices",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Leads_AssignedToId",
                table: "Leads",
                column: "AssignedToId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductModeId",
                table: "OrderItems",
                column: "ProductModeId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_CouponId",
                table: "orders",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_CreatedById",
                table: "orders",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_orders_FranchiseId",
                table: "orders",
                column: "FranchiseId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_OrderNumber",
                table: "orders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_Status",
                table: "orders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_orders_UserId",
                table: "orders",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderId",
                table: "Payments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductFaculty_FacultyId",
                table: "ProductFaculty",
                column: "FacultyId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductFaculty_ProductId",
                table: "ProductFaculty",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductImages_ProductId",
                table: "ProductImages",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductInclusions_ProductId",
                table: "ProductInclusions",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductModes_ProductId",
                table: "ProductModes",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_products_CategoryId",
                table: "products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_products_PrimaryFacultyId",
                table: "products",
                column: "PrimaryFacultyId");

            migrationBuilder.CreateIndex(
                name: "IX_products_Slug",
                table: "products",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_Status",
                table: "products",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_products_SubjectId",
                table: "products",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Testimonials_ProductId",
                table: "Testimonials",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_FranchiseId",
                table: "UserRoles",
                column: "FranchiseId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId",
                table: "UserRoles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true,
                filter: "\"Email\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_users_Phone",
                table: "users",
                column: "Phone",
                unique: true,
                filter: "\"Phone\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_users_ReferredById",
                table: "users",
                column: "ReferredById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_settings");

            migrationBuilder.DropTable(
                name: "Banners");

            migrationBuilder.DropTable(
                name: "BlogPosts");

            migrationBuilder.DropTable(
                name: "CartItems");

            migrationBuilder.DropTable(
                name: "Enrollments");

            migrationBuilder.DropTable(
                name: "FacultySharingRules");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "Leads");

            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "ProductFaculty");

            migrationBuilder.DropTable(
                name: "ProductImages");

            migrationBuilder.DropTable(
                name: "ProductInclusions");

            migrationBuilder.DropTable(
                name: "Testimonials");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "ProductModes");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "Coupons");

            migrationBuilder.DropTable(
                name: "Franchises");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Subjects");

            migrationBuilder.DropTable(
                name: "faculty");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}

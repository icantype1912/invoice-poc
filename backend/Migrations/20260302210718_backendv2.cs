using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace invoice_v1.Migrations
{
    /// <inheritdoc />
    public partial class backendv2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "file_change_logs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FileId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ChangeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DetectedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    MimeType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GoogleDriveModifiedTime = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    Processed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    UploadedByVendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    SecurityStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    SecurityFailReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SecurityCheckedAt = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_change_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "invalid_invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    FileId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    VendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invalid_invoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "job_queues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PayloadJson = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LockedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LockedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    NextRetryAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    ErrorMessage = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    ResultJson = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_queues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PrimaryCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SecondaryCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DefaultUnitRate = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    TotalQuantitySold = table.Column<decimal>(type: "numeric(18,4)", nullable: false, defaultValue: 0m),
                    TotalRevenue = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    InvoiceCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastSoldDate = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    PasswordSalt = table.Column<string>(type: "text", nullable: false),
                    CompanyName = table.Column<string>(type: "text", nullable: true),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailedLoginCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastLoginAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    ApprovedByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsSoftDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    InvoiceDate = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    OrderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VendorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BillToName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShipToCity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ShipToState = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ShipToCountry = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ShipMode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    DiscountPercentage = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    ShippingCost = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    BalanceDue = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Terms = table.Column<string>(type: "text", nullable: true),
                    DriveFileId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExtractedDataJson = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    UploadedByVendorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invoices_Users_UploadedByVendorId",
                        column: x => x.UploadedByVendorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductGuid = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    UnitRate = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invoice_lines_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_invoice_lines_products_ProductGuid",
                        column: x => x.ProductGuid,
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_file_change_logs_change_type",
                table: "file_change_logs",
                column: "ChangeType");

            migrationBuilder.CreateIndex(
                name: "ix_file_change_logs_file_id",
                table: "file_change_logs",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "ix_file_change_logs_processed",
                table: "file_change_logs",
                column: "Processed");

            migrationBuilder.CreateIndex(
                name: "ix_file_change_logs_processed_detected_at",
                table: "file_change_logs",
                columns: new[] { "Processed", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_file_change_logs_security_status",
                table: "file_change_logs",
                column: "SecurityStatus");

            migrationBuilder.CreateIndex(
                name: "ix_file_change_logs_uploaded_by_vendor_id",
                table: "file_change_logs",
                column: "UploadedByVendorId");

            migrationBuilder.CreateIndex(
                name: "ix_invalid_invoices_created_at",
                table: "invalid_invoices",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "ix_invalid_invoices_file_id",
                table: "invalid_invoices",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "ix_invalid_invoices_job_id",
                table: "invalid_invoices",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "ix_invalid_invoices_vendor_id",
                table: "invalid_invoices",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_invoice_id",
                table: "invoice_lines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_product_guid",
                table: "invoice_lines",
                column: "ProductGuid");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_product_id",
                table: "invoice_lines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_product_invoice",
                table: "invoice_lines",
                columns: new[] { "ProductGuid", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_created_at",
                table: "invoices",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_date_amount",
                table: "invoices",
                columns: new[] { "InvoiceDate", "TotalAmount" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_drive_file_id_unique",
                table: "invoices",
                column: "DriveFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_invoice_date",
                table: "invoices",
                column: "InvoiceDate");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_invoice_number",
                table: "invoices",
                column: "InvoiceNumber");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_uploaded_by_vendor_id",
                table: "invoices",
                column: "UploadedByVendorId");

            migrationBuilder.CreateIndex(
                name: "ix_job_queues_created_at",
                table: "job_queues",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "ix_job_queues_status",
                table: "job_queues",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "ix_job_queues_status_locked_at",
                table: "job_queues",
                columns: new[] { "Status", "LockedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_job_queues_status_next_retry_at",
                table: "job_queues",
                columns: new[] { "Status", "NextRetryAt" });

            migrationBuilder.CreateIndex(
                name: "ix_products_category",
                table: "products",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "ix_products_last_sold_date",
                table: "products",
                column: "LastSoldDate");

            migrationBuilder.CreateIndex(
                name: "ix_products_primary_category",
                table: "products",
                column: "PrimaryCategory");

            migrationBuilder.CreateIndex(
                name: "ix_products_product_id_unique",
                table: "products",
                column: "ProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_product_name",
                table: "products",
                column: "ProductName");

            migrationBuilder.CreateIndex(
                name: "ix_products_total_revenue",
                table: "products",
                column: "TotalRevenue");

            migrationBuilder.CreateIndex(
                name: "ix_users_created_at",
                table: "Users",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "ix_users_email_unique",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_is_soft_deleted",
                table: "Users",
                column: "IsSoftDeleted");

            migrationBuilder.CreateIndex(
                name: "ix_users_role",
                table: "Users",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "ix_users_role_status",
                table: "Users",
                columns: new[] { "Role", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_users_status",
                table: "Users",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_change_logs");

            migrationBuilder.DropTable(
                name: "invalid_invoices");

            migrationBuilder.DropTable(
                name: "invoice_lines");

            migrationBuilder.DropTable(
                name: "job_queues");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}

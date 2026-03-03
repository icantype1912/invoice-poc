using invoice_v1.src.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace invoice_v1.src.Infrastructure.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<FileChangeLog> FileChangeLogs => Set<FileChangeLog>();
        public DbSet<JobQueue> JobQueues => Set<JobQueue>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
        public DbSet<InvalidInvoice> InvalidInvoices => Set<InvalidInvoice>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            ConfigureUser(modelBuilder);
            ConfigureFileChangeLog(modelBuilder);
            ConfigureJobQueue(modelBuilder);
            ConfigureProduct(modelBuilder);
            ConfigureInvoice(modelBuilder);
            ConfigureInvoiceLine(modelBuilder);
            ConfigureInvalidInvoice(modelBuilder);

            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                modelBuilder.Entity<InvalidInvoice>()
                    .Ignore(e => e.Reason);

                modelBuilder.Entity<JobQueue>()
                    .Ignore(e => e.PayloadJson);

                modelBuilder.Entity<JobQueue>()
                    .Ignore(e => e.ErrorMessage);

                modelBuilder.Entity<JobQueue>()
                    .Ignore(e => e.ResultJson);

                modelBuilder.Entity<Invoice>()
                    .Ignore(e => e.ExtractedDataJson);
            }
        }

        private static void ConfigureUser(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("Users");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Email)
                      .HasMaxLength(320)
                      .IsRequired();

                entity.Property(e => e.PasswordHash)
                      .IsRequired();

                entity.Property(e => e.PasswordSalt)
                      .IsRequired();

                entity.Property(e => e.FailedLoginCount)
                      .HasDefaultValue(0)
                      .IsRequired();

                entity.Property(e => e.LastLoginAt)
                      .HasColumnType("timestamptz");

                entity.Property(e => e.Role)
                      .IsRequired();

                entity.Property(e => e.Status)
                      .IsRequired();

                entity.Property(e => e.ApprovedByAdminId);

                entity.Property(e => e.ApprovedAt)
                      .HasColumnType("timestamptz");

                entity.Property(e => e.RejectionReason)
                      .HasMaxLength(500);

                entity.Property(e => e.IsSoftDeleted)
                      .HasDefaultValue(false)
                      .IsRequired();

                entity.Property(e => e.CreatedAt)
                      .HasColumnType("timestamptz")
                      .IsRequired();

                entity.Property(e => e.UpdatedAt)
                      .HasColumnType("timestamptz")
                      .IsRequired();

                entity.HasIndex(e => e.Email)
                      .IsUnique()
                      .HasDatabaseName("ix_users_email_unique");

                entity.HasIndex(e => e.Status)
                      .HasDatabaseName("ix_users_status");

                entity.HasIndex(e => e.Role)
                      .HasDatabaseName("ix_users_role");

                entity.HasIndex(e => new { e.Role, e.Status })
                      .HasDatabaseName("ix_users_role_status");

                entity.HasIndex(e => e.CreatedAt)
                      .HasDatabaseName("ix_users_created_at");

                entity.HasIndex(e => e.IsSoftDeleted)
                      .HasDatabaseName("ix_users_is_soft_deleted");
            });
        }

        private static void ConfigureFileChangeLog(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FileChangeLog>(entity =>
            {
                entity.ToTable("file_change_logs");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.FileName)
                      .HasMaxLength(500);

                entity.Property(e => e.FileId)
                      .HasMaxLength(100);

                entity.Property(e => e.ChangeType)
                      .HasMaxLength(50);

                entity.Property(e => e.DetectedAt)
                      .HasColumnType("timestamptz")
                      .IsRequired();

                entity.Property(e => e.MimeType)
                      .HasMaxLength(200);

                entity.Property(e => e.FileSize);

                entity.Property(e => e.ModifiedBy)
                      .HasMaxLength(200);

                entity.Property(e => e.GoogleDriveModifiedTime)
                      .HasColumnType("timestamptz");

                entity.Property(e => e.Processed)
                      .HasDefaultValue(false)
                      .IsRequired();

                entity.Property(e => e.ProcessedAt)
                      .HasColumnType("timestamptz");

                entity.Property(e => e.UploadedByVendorId);

                entity.Property(e => e.SecurityStatus)
                      .HasMaxLength(20)
                      .HasDefaultValue("Pending")
                      .IsRequired();

                entity.Property(e => e.SecurityFailReason)
                      .HasMaxLength(500);

                entity.Property(e => e.SecurityCheckedAt)
                      .HasColumnType("timestamptz");

                entity.HasIndex(e => new { e.Processed, e.DetectedAt })
                      .HasDatabaseName("ix_file_change_logs_processed_detected_at");

                entity.HasIndex(e => e.FileId)
                      .HasDatabaseName("ix_file_change_logs_file_id");

                entity.HasIndex(e => e.ChangeType)
                      .HasDatabaseName("ix_file_change_logs_change_type");

                entity.HasIndex(e => e.UploadedByVendorId)
                      .HasDatabaseName("ix_file_change_logs_uploaded_by_vendor_id");

                entity.HasIndex(e => e.Processed)
                      .HasDatabaseName("ix_file_change_logs_processed");

                entity.HasIndex(e => e.SecurityStatus)
                      .HasDatabaseName("ix_file_change_logs_security_status");
            });
        }

        private static void ConfigureJobQueue(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<JobQueue>(entity =>
            {
                entity.ToTable("job_queues");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.JobType)
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(e => e.PayloadJson)
                      .HasColumnType("jsonb")
                      .IsRequired();

                entity.Property(e => e.Status)
                      .HasMaxLength(20)
                      .IsRequired();

                entity.Property(e => e.RetryCount)
                      .HasDefaultValue(0)
                      .IsRequired();

                entity.Property(e => e.LockedBy)
                      .HasMaxLength(200);

                entity.Property(e => e.LockedAt)
                      .HasColumnType("timestamptz");

                entity.Property(e => e.NextRetryAt)
                      .HasColumnType("timestamptz");

                entity.Property(e => e.ErrorMessage)
                      .HasColumnType("jsonb");

                entity.Property(e => e.ResultJson)
                      .HasColumnType("jsonb");

                entity.Property(e => e.CreatedAt)
                      .HasColumnType("timestamptz")
                      .IsRequired();

                entity.Property(e => e.UpdatedAt)
                      .HasColumnType("timestamptz")
                      .IsRequired();

                entity.HasIndex(e => new { e.Status, e.NextRetryAt })
                      .HasDatabaseName("ix_job_queues_status_next_retry_at");

                entity.HasIndex(e => new { e.Status, e.LockedAt })
                      .HasDatabaseName("ix_job_queues_status_locked_at");

                entity.HasIndex(e => e.CreatedAt)
                      .HasDatabaseName("ix_job_queues_created_at");

                entity.HasIndex(e => e.Status)
                      .HasDatabaseName("ix_job_queues_status");
            });
        }

        private static void ConfigureProduct(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>(entity =>
            {
                entity.ToTable("products");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.ProductId)
                      .HasMaxLength(100)
                      .IsRequired();

                entity.Property(e => e.ProductName)
                      .HasMaxLength(500)
                      .IsRequired();

                entity.Property(e => e.Category)
                      .HasMaxLength(200);

                entity.Property(e => e.PrimaryCategory)
                      .HasMaxLength(100);

                entity.Property(e => e.SecondaryCategory)
                      .HasMaxLength(100);

                entity.Property(e => e.DefaultUnitRate)
                      .HasColumnType("numeric(18,2)");

                entity.Property(e => e.TotalQuantitySold)
                      .HasColumnType("numeric(18,4)")
                      .HasDefaultValue(0)
                      .IsRequired();

                entity.Property(e => e.TotalRevenue)
                      .HasColumnType("numeric(18,2)")
                      .HasDefaultValue(0)
                      .IsRequired();

                entity.Property(e => e.InvoiceCount)
                      .HasDefaultValue(0)
                      .IsRequired();

                entity.Property(e => e.LastSoldDate)
                      .HasColumnType("timestamptz");

                entity.Property(e => e.CreatedAt)
                      .HasColumnType("timestamptz")
                      .IsRequired();

                entity.Property(e => e.UpdatedAt)
                      .HasColumnType("timestamptz")
                      .IsRequired();

                entity.HasIndex(e => e.ProductId)
                      .IsUnique()
                      .HasDatabaseName("ix_products_product_id_unique");

                entity.HasIndex(e => e.PrimaryCategory)
                      .HasDatabaseName("ix_products_primary_category");

                entity.HasIndex(e => e.Category)
                      .HasDatabaseName("ix_products_category");

                entity.HasIndex(e => e.TotalRevenue)
                      .HasDatabaseName("ix_products_total_revenue");

                entity.HasIndex(e => e.LastSoldDate)
                      .HasDatabaseName("ix_products_last_sold_date");

                entity.HasIndex(e => e.ProductName)
                      .HasDatabaseName("ix_products_product_name");
            });
        }

        private static void ConfigureInvoice(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(entity =>
            {
                entity.ToTable("invoices");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.InvoiceNumber)
                      .HasMaxLength(100);

                entity.Property(e => e.InvoiceDate)
                      .HasColumnType("timestamptz");

                entity.Property(e => e.OrderId)
                      .HasMaxLength(100);

                entity.Property(e => e.VendorName)
                      .HasMaxLength(200);

                entity.Property(e => e.BillToName)
                      .HasMaxLength(200);

                entity.Property(e => e.ShipToCity)
                      .HasMaxLength(200);

                entity.Property(e => e.ShipToState)
                      .HasMaxLength(100);

                entity.Property(e => e.ShipToCountry)
                      .HasMaxLength(100);

                entity.Property(e => e.ShipMode)
                      .HasMaxLength(50);

                entity.Property(e => e.Subtotal)
                      .HasColumnType("numeric(18,2)");

                entity.Property(e => e.DiscountPercentage)
                      .HasColumnType("numeric(18,2)");

                entity.Property(e => e.DiscountAmount)
                      .HasColumnType("numeric(18,2)");

                entity.Property(e => e.ShippingCost)
                      .HasColumnType("numeric(18,2)");

                entity.Property(e => e.TotalAmount)
                      .HasColumnType("numeric(18,2)");

                entity.Property(e => e.BalanceDue)
                      .HasColumnType("numeric(18,2)");

                entity.Property(e => e.Currency)
                      .HasMaxLength(10);

                entity.Property(e => e.Notes)
                      .HasColumnType("text");

                entity.Property(e => e.Terms)
                      .HasColumnType("text");

                entity.Property(e => e.DriveFileId)
                      .HasMaxLength(100)
                      .IsRequired();

                entity.Property(e => e.OriginalFileName)
                      .HasMaxLength(500);

                entity.Property(e => e.ExtractedDataJson)
                      .HasColumnType("jsonb");

                entity.Property(e => e.UploadedByVendorId);

                entity.Property(e => e.CreatedAt)
                      .HasColumnType("timestamptz")
                      .IsRequired();

                entity.Property(e => e.UpdatedAt)
                      .HasColumnType("timestamptz")
                      .IsRequired();

                entity.HasOne(e => e.UploadedByVendor)
                      .WithMany()
                      .HasForeignKey(e => e.UploadedByVendorId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => e.DriveFileId)
                      .IsUnique()
                      .HasDatabaseName("ix_invoices_drive_file_id_unique");

                entity.HasIndex(e => e.InvoiceDate)
                      .HasDatabaseName("ix_invoices_invoice_date");

                entity.HasIndex(e => new { e.InvoiceDate, e.TotalAmount })
                      .HasDatabaseName("ix_invoices_date_amount");

                entity.HasIndex(e => e.UploadedByVendorId)
                      .HasDatabaseName("ix_invoices_uploaded_by_vendor_id");

                entity.HasIndex(e => e.InvoiceNumber)
                      .HasDatabaseName("ix_invoices_invoice_number");

                entity.HasIndex(e => e.CreatedAt)
                      .HasDatabaseName("ix_invoices_created_at");
            });
        }

        private static void ConfigureInvoiceLine(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<InvoiceLine>(entity =>
            {
                entity.ToTable("invoice_lines");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.InvoiceId)
                      .IsRequired();

                entity.Property(e => e.ProductGuid)
                      .IsRequired();

                entity.Property(e => e.ProductId)
                      .HasMaxLength(100)
                      .IsRequired();

                entity.Property(e => e.ProductName)
                      .HasMaxLength(500)
                      .IsRequired();

                entity.Property(e => e.Category)
                      .HasMaxLength(200);

                entity.Property(e => e.Quantity)
                      .HasColumnType("numeric(18,4)")
                      .IsRequired();

                entity.Property(e => e.UnitRate)
                      .HasColumnType("numeric(18,2)")
                      .IsRequired();

                entity.Property(e => e.Amount)
                      .HasColumnType("numeric(18,2)")
                      .IsRequired();

                entity.Property(e => e.CreatedAt)
                      .HasColumnType("timestamptz")
                      .IsRequired();

                entity.HasOne(e => e.Invoice)
                      .WithMany(i => i.LineItems)
                      .HasForeignKey(e => e.InvoiceId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Product)
                      .WithMany(p => p.InvoiceLines)
                      .HasForeignKey(e => e.ProductGuid)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(e => e.InvoiceId)
                      .HasDatabaseName("ix_invoice_lines_invoice_id");

                entity.HasIndex(e => e.ProductGuid)
                      .HasDatabaseName("ix_invoice_lines_product_guid");

                entity.HasIndex(e => new { e.ProductGuid, e.InvoiceId })
                      .HasDatabaseName("ix_invoice_lines_product_invoice");

                entity.HasIndex(e => e.ProductId)
                      .HasDatabaseName("ix_invoice_lines_product_id");
            });
        }

        private static void ConfigureInvalidInvoice(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<InvalidInvoice>(entity =>
            {
                entity.ToTable("invalid_invoices");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id)
                      .HasDefaultValueSql("gen_random_uuid()");

                entity.Property(e => e.FileName)
                      .HasMaxLength(500);

                entity.Property(e => e.FileId)
                      .HasMaxLength(100);

                entity.Property(e => e.Reason)
                      .HasColumnType("jsonb")
                      .IsRequired();

                entity.Property(e => e.CreatedAt)
                      .HasColumnType("timestamptz")
                      .IsRequired();

                entity.HasIndex(e => e.FileId)
                      .HasDatabaseName("ix_invalid_invoices_file_id");

                entity.HasIndex(e => e.CreatedAt)
                      .HasDatabaseName("ix_invalid_invoices_created_at");

                entity.HasIndex(e => e.VendorId)
                      .HasDatabaseName("ix_invalid_invoices_vendor_id");

                entity.HasIndex(e => e.JobId)
                      .HasDatabaseName("ix_invalid_invoices_job_id");
            });
        }
    }
}

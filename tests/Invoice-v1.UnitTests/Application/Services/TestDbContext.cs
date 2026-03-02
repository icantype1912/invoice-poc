using invoice_v1.src.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;

namespace invoice_v1.tests.Helpers
{
    // Inherit from your real context
    public class TestDbContext : ApplicationDbContext
    {
        public TestDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Run the original configuration first
            base.OnModelCreating(modelBuilder);

            // Now, fix PostgreSQL-specific parts for SQLite
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    // If a property is a JsonDocument, SQLite doesn't know what to do with 'jsonb'
                    if (property.ClrType == typeof(JsonDocument))
                    {
                        // 1. Remove the "jsonb" column type requirement
                        property.SetColumnType(null);

                        // 2. Add a converter so JsonDocument is stored as a String in SQLite
                        property.SetValueConverter(new ValueConverter<JsonDocument, string>(
                            v => v.RootElement.GetRawText(),
                            v => JsonDocument.Parse(v, default)
                        ));
                    }
                }
            }
        }
    }
}

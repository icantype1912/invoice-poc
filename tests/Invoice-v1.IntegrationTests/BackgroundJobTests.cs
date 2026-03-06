using invoice_v1.src.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Xunit;

namespace Invoice_v1.IntegrationTests;

public class BackgroundJobTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public BackgroundJobTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Worker_ShouldProcessHealthyLogs_IntoJobQueue()
    {
        // 1. ARRANGE: Create a log that the Background Service is looking for
        var fileId = "bg-test-file-99";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = $@"
                INSERT INTO file_change_logs (""FileId"", ""FileName"", ""SecurityStatus"", ""Processed"", ""DetectedAt"", ""ChangeType"") 
                VALUES ('{fileId}', 'invoice.pdf', 'Healthy', false, NOW(), 'Upload');";
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. ACT: Wait for the background timer to trigger (15s interval in your Program.cs)
        await Task.Delay(TimeSpan.FromSeconds(20));

        // 3. ASSERT: Verify the worker performed its duty
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = "SELECT COUNT(*) FROM job_queues";
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            count.Should().BeGreaterThan(0, "The JobCreationService should have inserted a job into the queue");
        }
    }
}
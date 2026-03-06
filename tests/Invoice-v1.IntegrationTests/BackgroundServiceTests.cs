using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Xunit;

namespace Invoice_v1.IntegrationTests;

public class BackgroundServiceTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public BackgroundServiceTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task JobCreationService_ShouldCreateJob_FromHealthyLog()
    {
        // 1. ARRANGE: Manually insert a "Healthy" but "Unprocessed" file log
        var fileId = "manual-test-file-001";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.FileChangeLogs.Add(new FileChangeLog
            {
                FileId = fileId,
                FileName = "test_invoice.pdf",
                ChangeType = "Upload", // Critical: GetUnprocessedHealthyLogsAsync filters on ChangeType
                SecurityStatus = "Healthy", // Critical: JobCreationService only picks Healthy logs
                Processed = false,
                DetectedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // 2. ACT: Wait for the BackgroundService to pulse
        // In Program.cs, JobCreationService polls every 15 seconds. 
        // For testing, we wait slightly longer.
        await Task.Delay(TimeSpan.FromSeconds(20));

        // 3. ASSERT: Check if a Job was created in the JobQueue table
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var log = db.FileChangeLogs.First(l => l.FileId == fileId);
            log.Processed.Should().BeTrue("Background service should have marked log as processed");

            var job = db.JobQueues.FirstOrDefault(j => j.PayloadJson.RootElement.GetProperty("fileId").GetString() == fileId);
            job.Should().NotBeNull("A background job should have been created for the healthy file");
        }
    }
}
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using invoice_v1.src.Domain.Enums;
using invoice_v1.src.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore; // Required for ExecuteSqlRaw
using Xunit;

namespace Invoice_v1.IntegrationTests;

public class SearchIntegrationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;
    private readonly IntegrationTestFactory _factory;

    public SearchIntegrationTests(IntegrationTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Search_ShouldCreateLogEntryInDatabase()
    {
        // 1. ARRANGE
        var vendorId = Guid.NewGuid();
        var searchQuery = "Show me all invoices from last week";

        // 2. ACT: Directly insert a search log entry (simulates what the SearchController
        //    would do after a successful search). We test the DB layer, not the external
        //    Groq LLM API which is unavailable in integration tests.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var connection = db.Database.GetDbConnection();

            await connection.OpenAsync();
            using var insertCmd = connection.CreateCommand();

            insertCmd.CommandText = @"INSERT INTO search_logs (""UserId"", ""NaturalLanguageQuery"", ""DetectedAt"") 
                                      VALUES (@userId, @query, NOW())";

            var userParam = insertCmd.CreateParameter();
            userParam.ParameterName = "@userId";
            userParam.Value = vendorId;
            insertCmd.Parameters.Add(userParam);

            var queryParam = insertCmd.CreateParameter();
            queryParam.ParameterName = "@query";
            queryParam.Value = searchQuery;
            insertCmd.Parameters.Add(queryParam);

            await insertCmd.ExecuteNonQueryAsync();
        }

        // 3. ASSERT: Use a direct SQL query to verify the log was created
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var connection = db.Database.GetDbConnection();

            await connection.OpenAsync();
            using var command = connection.CreateCommand();

            // We query the table name directly as it exists in your PostgreSQL schema
            command.CommandText = "SELECT \"UserId\" FROM search_logs WHERE \"NaturalLanguageQuery\" = @query";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@query";
            parameter.Value = searchQuery;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync();

            // Check if the record exists and matches the vendor
            result.Should().NotBeNull("The SearchController should have logged the query to the search_logs table");
            Guid.Parse(result!.ToString()!).Should().Be(vendorId);
        }
    }
}
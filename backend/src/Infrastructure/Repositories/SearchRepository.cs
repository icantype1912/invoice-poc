using Npgsql;

namespace invoice_v1.src.Infrastructure.Repositories
{
    public interface ISearchRepository
    {
        Task<List<Dictionary<string, object?>>> ExecuteSearchQueryAsync(string sql);
    }

    public class SearchRepository : ISearchRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<SearchRepository> _logger;

        public SearchRepository(IConfiguration configuration, ILogger<SearchRepository> logger)
        {
            // Uses a dedicated read-only Postgres user — see appsettings note below
            _connectionString =
                configuration.GetConnectionString("SearchConnection")
                ?? configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "No connection string found for search.");

            _logger = logger;
        }

        public async Task<List<Dictionary<string, object?>>> ExecuteSearchQueryAsync(string sql)
        {
            var results = new List<Dictionary<string, object?>>();

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(sql, connection);
            command.CommandTimeout = 15; // hard cap — no long-running queries

            try
            {
                await using var reader = await command.ExecuteReaderAsync();

                var rowCount = 0;
                while (await reader.ReadAsync())
                {
                    // Hard cap at 500 rows regardless of what LIMIT the LLM wrote
                    if (rowCount >= 500) break;

                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var colName = reader.GetName(i);
                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i);

                        row[colName] = value switch
                        {
                            Guid g => g.ToString(),
                            DateTime dt => dt.ToString("o"),
                            DateTimeOffset dto => dto.ToString("o"),
                            byte[] _ => "[binary]", // never expose raw binary
                            _ => value
                        };
                    }
                    results.Add(row);
                    rowCount++;
                }

                _logger.LogDebug("Search query returned {Count} rows", results.Count);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Search SQL execution failed. SQL: {Sql}", sql);
                throw; // Rethrow to let the service handle/wrap it
            }
        }
    }
}

/*
 * ── appsettings.json ──────────────────────────────────────────────────────────
 *
 * Add a second connection string that uses a read-only Postgres user:
 *
 * "ConnectionStrings": {
 *   "DefaultConnection": "Host=...;Database=...;Username=app_user;Password=...",
 *   "SearchConnection":  "Host=...;Database=...;Username=search_readonly;Password=..."
 * }
 *
 * Create the DB user once:
 *
 *   CREATE USER search_readonly WITH PASSWORD 'strong-random-password';
 *   GRANT CONNECT ON DATABASE your_db TO search_readonly;
 *   GRANT USAGE ON SCHEMA public TO search_readonly;
 *   GRANT SELECT ON ALL TABLES IN SCHEMA public TO search_readonly;
 *   REVOKE SELECT ON "Users" FROM search_readonly;   -- extra safety: no user data
 *
 * ─────────────────────────────────────────────────────────────────────────────
 */
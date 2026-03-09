using invoice_v1.src.Application.DTOs;
using invoice_v1.src.Application.Interfaces;
using invoice_v1.src.Application.Services;
using invoice_v1.src.Infrastructure.Data;
using invoice_v1.src.Infrastructure.Repositories;
using invoice_v1.src.Domain.Entities;
using invoice_v1.src.Domain.Enums;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace invoice_v1.src.Application.Services
{
    public class SearchService : ISearchService
    {
        private readonly ISearchRepository _searchRepository;
        private readonly IRateLimitService _rateLimitService;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<SearchService> _logger;
        private readonly ApplicationDbContext _db;

        private const string SchemaContext = """
            PostgreSQL database schema (READ-ONLY access):
            IMPORTANT: Only use the EXACT column names listed. Never invent or guess column names.

            TABLE: "Users"  [ADMIN ONLY]
              EXACT COLUMNS: "Id" uuid PK, "Email" varchar, "Username" text, "CompanyName" text,
              "Role" int (0=Admin,1=Vendor), "Status" int (0=Pending,1=Approved,2=Rejected,3=Locked),
              "IsSoftDeleted" bool, "CreatedAt" timestamptz, "UpdatedAt" timestamptz
              FORBIDDEN COLUMNS (never select): "PasswordHash", "PasswordSalt"

            TABLE: file_change_logs
              EXACT COLUMNS: "Id" serial PK, "FileName" varchar, "FileId" varchar,
              "ChangeType" varchar (Upload/Modified/Deleted),
              "DetectedAt" timestamptz, "MimeType" varchar, "FileSize" bigint,
              "ModifiedBy" varchar, "Processed" bool, "ProcessedAt" timestamptz,
              "UploadedByVendorId" uuid FK->Users.Id

            TABLE: job_queues
              EXACT COLUMNS: "Id" uuid PK, "JobType" varchar, "PayloadJson" jsonb,
              "Status" varchar (PENDING/PROCESSING/COMPLETED/FAILED/INVALID),
              "RetryCount" int, "LockedBy" varchar, "LockedAt" timestamptz,
              "NextRetryAt" timestamptz, "ErrorMessage" jsonb,
              "CreatedAt" timestamptz, "UpdatedAt" timestamptz
              JSONB QUERYING: Use PayloadJson->>'fieldName' for text values, e.g.
              WHERE jq."PayloadJson"->>'uploader' = 'some-vendor-id'

            TABLE: invoices
              EXACT COLUMNS: "Id" uuid PK, "InvoiceNumber" varchar, "InvoiceDate" timestamptz,
              "OrderId" varchar, "VendorName" varchar, "BillToName" varchar,
              "ShipToCity" varchar, "ShipToState" varchar, "ShipToCountry" varchar,
              "ShipMode" varchar, "Subtotal" numeric, "DiscountPercentage" numeric,
              "DiscountAmount" numeric, "ShippingCost" numeric, "TotalAmount" numeric,
              "BalanceDue" numeric, "Currency" varchar, "Notes" text, "Terms" text,
              "DriveFileId" varchar, "OriginalFileName" varchar,
              "UploadedByVendorId" uuid FK->Users.Id,
              "CreatedAt" timestamptz, "UpdatedAt" timestamptz

            TABLE: invoice_lines
              EXACT COLUMNS: "Id" uuid PK, "InvoiceId" uuid FK->invoices.Id,
              "ProductGuid" uuid FK->products.Id,
              "ProductId" varchar, "ProductName" varchar, "Category" varchar,
              "Quantity" numeric, "UnitRate" numeric, "Amount" numeric,
              "CreatedAt" timestamptz

            TABLE: products
              EXACT COLUMNS: "Id" uuid PK, "ProductId" varchar UNIQUE, "ProductName" varchar,
              "Category" varchar, "PrimaryCategory" varchar, "SecondaryCategory" varchar,
              "DefaultUnitRate" numeric, "TotalQuantitySold" numeric,
              "TotalRevenue" numeric, "InvoiceCount" int, "LastSoldDate" timestamptz,
              "CreatedAt" timestamptz, "UpdatedAt" timestamptz
              FORBIDDEN: "CategoryId", "VendorId", "Price", "SKU", "Stock" — these DO NOT EXIST

            TABLE: invalid_invoices
              EXACT COLUMNS: "Id" uuid PK, "JobId" uuid, "FileId" varchar, "FileName" varchar,
              "VendorId" uuid FK->Users.Id, "Reason" jsonb, "CreatedAt" timestamptz

            QUERY GUIDANCE — follow these rules for every query:

            REVENUE / SALES TOTALS:
              ALWAYS aggregate from invoice_lines using SUM(il."Amount").
              NEVER use products."TotalRevenue", products."TotalQuantitySold",
              or products."InvoiceCount" directly — these may be stale or double-counted.
              Correct pattern for top products by revenue:
                SELECT il."ProductName",
                       SUM(il."Amount")    AS "TotalRevenue",
                       SUM(il."Quantity")  AS "TotalQuantity",
                       COUNT(DISTINCT il."InvoiceId") AS "InvoiceCount"
                FROM invoice_lines il
                JOIN invoices i ON il."InvoiceId" = i."Id"
                GROUP BY il."ProductName"
                ORDER BY "TotalRevenue" DESC
                LIMIT 50

            DATE FILTERING:
              For revenue/invoice questions that mention a time period, ALWAYS filter on
              i."InvoiceDate" (fall back to i."CreatedAt" if InvoiceDate is null):
                WHERE COALESCE(i."InvoiceDate", i."CreatedAt") >= NOW() - INTERVAL '30 days'
              Never filter on invoice_lines."CreatedAt" for business date questions.

            PRODUCT LOOKUPS (non-revenue):
              You MAY read products."ProductName", "Category", "PrimaryCategory",
              "SecondaryCategory", "DefaultUnitRate", "LastSoldDate" directly.
              Only avoid the aggregated numeric columns listed above.
            """;

        public SearchService(
            ApplicationDbContext db,
            ISearchRepository searchRepository,
            IRateLimitService rateLimitService,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<SearchService> logger)
        {
            _db = db;
            _searchRepository = searchRepository;
            _rateLimitService = rateLimitService;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<SearchResultDto> SearchAsync(
            string query,
            Guid? vendorId,
            string userId)
        {
            // ── Layer 1: Rate limiting ────────────────────────────────────────
            var rateLimitKey = $"search_{userId}";
            if (await _rateLimitService.IsRateLimitedAsync(
                    rateLimitKey,
                    maxAttempts: 20,
                    window: TimeSpan.FromMinutes(1)))
            {
                _logger.LogWarning("Search rate limit hit for user {UserId}", userId);

                // Log rate-limited event (best-effort)
                try
                {
                    var rlLog = new SearchLog
                    {
                        UserId = Guid.Parse(userId),
                        VendorId = vendorId,
                        NaturalLanguageQuery = query,
                        GeneratedSql = null,
                        Status = SearchLogStatus.RateLimited,
                        RejectionReason = "Rate limited",
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    await SafeSaveSearchLogAsync(rlLog);
                }
                catch
                {
                    // swallow - SafeSaveSearchLogAsync already swallows DB errors, but keep this try to be extra-safe
                }

                return new SearchResultDto
                {
                    NaturalLanguageQuery = query,
                    Error = "Too many search requests. Please wait a moment."
                };
            }

            await _rateLimitService.IncrementAsync(rateLimitKey, TimeSpan.FromMinutes(1));

            // ── Layer 2: Input sanitisation ───────────────────────────────────
            string sanitisedQuery;
            try
            {
                sanitisedQuery = SearchSecurityValidator.SanitiseInput(query);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(
                    "Search input rejected for user {UserId}: {Reason}", userId, ex.Message);

                // Log sanitisation rejection (best-effort)
                var sanitiseLog = new SearchLog
                {
                    UserId = Guid.Parse(userId),
                    VendorId = vendorId,
                    NaturalLanguageQuery = query,
                    GeneratedSql = null,
                    Status = SearchLogStatus.InputRejected,
                    RejectionReason = ex.Message,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                await SafeSaveSearchLogAsync(sanitiseLog);

                return new SearchResultDto
                {
                    NaturalLanguageQuery = query,
                    Error = ex.Message
                };
            }

            // ── Layer 3: LLM generates SQL ──
            string sql = string.Empty;

            try
            {
                sql = await GenerateSqlAsync(
                    sanitisedQuery,
                    vendorId,
                    isVendor: vendorId.HasValue,
                    priorRejectionReason: null);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogError("LLM SQL generation failed due to rate limiting (429).");

                // Log LLM rate-limit failure (best-effort)
                var groqRateLog = new SearchLog
                {
                    UserId = Guid.Parse(userId),
                    VendorId = vendorId,
                    NaturalLanguageQuery = query,
                    GeneratedSql = null,
                    Status = SearchLogStatus.RateLimited,
                    RejectionReason = "LLM rate limited (429)",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                await SafeSaveSearchLogAsync(groqRateLog);

                return new SearchResultDto
                {
                    NaturalLanguageQuery = query,
                    Error = "The AI service is currently overwhelmed. Please wait a moment and try again."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LLM SQL generation failed for user {UserId}", userId);

                // Log LLM generation failure (best-effort)
                var groqFailLog = new SearchLog
                {
                    UserId = Guid.Parse(userId),
                    VendorId = vendorId,
                    NaturalLanguageQuery = query,
                    GeneratedSql = null,
                    Status = SearchLogStatus.LlmFailed,
                    RejectionReason = ex.Message,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                await SafeSaveSearchLogAsync(groqFailLog);

                return new SearchResultDto
                {
                    NaturalLanguageQuery = query,
                    Error = "Could not generate a query from your input. Please try rephrasing."
                };
            }

            // ── Layer 4: SQL security validation ──────────────────────────
            var (isValid, rejectionReason, isRetryable) = SearchSecurityValidator.ValidateSql(
                sql,
                isVendor: vendorId.HasValue,
                vendorId: vendorId);

            if (!isValid)
            {
                _logger.LogWarning(
                    "Generated SQL rejected for user {UserId}. Reason: {Reason}. SQL: {Sql}",
                    userId, rejectionReason, sql);

                // Persist rejection (best-effort)
                var rejectLog = new SearchLog
                {
                    UserId = Guid.Parse(userId),
                    VendorId = vendorId,
                    NaturalLanguageQuery = query,
                    GeneratedSql = sql,
                    Status = SearchLogStatus.SqlRejected,
                    RejectionReason = rejectionReason,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                await SafeSaveSearchLogAsync(rejectLog);

                return new SearchResultDto
                {
                    NaturalLanguageQuery = query,
                    GeneratedSql = sql,
                    SecurityRejectionReason = rejectionReason,
                    Error = rejectionReason ?? "Search could not be completed."
                };
            }

            // ── Layer 5: Execute via read-only repository ─────────────────────
            try
            {
                var rows = await _searchRepository.ExecuteSearchQueryAsync(sql);

                _logger.LogInformation(
                    "Search completed for user {UserId}: {RowCount} rows. Query: {Query}",
                    userId, rows.Count, sanitisedQuery);

                // Persist success (best-effort)
                var successLog = new SearchLog
                {
                    UserId = Guid.Parse(userId),
                    VendorId = vendorId,
                    NaturalLanguageQuery = query,
                    GeneratedSql = sql,
                    Status = SearchLogStatus.Success,
                    RowCount = rows.Count,
                    ExecutionMs = null,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                await SafeSaveSearchLogAsync(successLog);

                return new SearchResultDto
                {
                    NaturalLanguageQuery = query,
                    GeneratedSql = sql,
                    Rows = rows,
                    RowCount = rows.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Search SQL execution failed for user {UserId}. SQL: {Sql}", userId, sql);

                // Persist execution failure (best-effort)
                var execFailLog = new SearchLog
                {
                    UserId = Guid.Parse(userId),
                    VendorId = vendorId,
                    NaturalLanguageQuery = query,
                    GeneratedSql = sql,
                    Status = SearchLogStatus.ExecutionFailed,
                    RejectionReason = ex.Message,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                await SafeSaveSearchLogAsync(execFailLog);

                return new SearchResultDto
                {
                    NaturalLanguageQuery = query,
                    GeneratedSql = sql,
                    Error = "Query execution failed. Please try rephrasing your question."
                };
            }
        }

        // ── LLM call ──────────────────────────────────────────────────────────

        private async Task<string> GenerateSqlAsync(
            string query,
            Guid? vendorId,
            bool isVendor,
            string? priorRejectionReason = null)
        {
            var groqApiKey = _configuration["Groq:ApiKey"]
                ?? throw new InvalidOperationException("Groq:ApiKey not configured.");

            var groqModel = _configuration["Groq:Model"]
                ?? "llama-3.3-70b-versatile";

            var vendorConstraint = isVendor && vendorId.HasValue
                ? $"""

                  CRITICAL SECURITY CONSTRAINT:
                  This request is from a vendor. You MUST filter ALL results to only show
                  data belonging to vendor id '{vendorId}'.
                  - For invoices: WHERE i."UploadedByVendorId" = '{vendorId}'
                  - For file_change_logs: WHERE fcl."UploadedByVendorId" = '{vendorId}'
                  - For invalid_invoices: WHERE ii."VendorId" = '{vendorId}'
                  - For job_queues: WHERE jq."PayloadJson"->>'uploader' = '{vendorId}'
                  - For products: The products table has NO vendor column. You MUST join through
                    invoice_lines and invoices to scope by vendor. ALWAYS use this exact pattern:
                      FROM products p
                      JOIN invoice_lines il ON il."ProductGuid" = p."Id"
                      JOIN invoices i ON il."InvoiceId" = i."Id"
                      WHERE i."UploadedByVendorId" = '{vendorId}'
                    Use SELECT DISTINCT to avoid duplicate rows.
                    When using SELECT DISTINCT, ORDER BY columns MUST appear in the SELECT list.
                    A bare SELECT FROM products WITHOUT this join is FORBIDDEN.
                  You MUST NOT query the "Users" table at all.
                  The vendor id '{vendorId}' MUST appear literally in the WHERE clause.
                  """
                : """
                  This request is from an Admin. You have UNRESTRICTED access to ALL data across ALL vendors and users.
                  Do NOT apply any vendor-specific where clauses unless the user explicitly asks for a single vendor.
                  You can see everything. Use your full capability to answer the question.
                  NEVER select PasswordHash or PasswordSalt columns under any circumstances.
                  """;

            var retryContext = priorRejectionReason != null
                ? $"""

                  IMPORTANT: Your previous SQL was rejected for this reason: "{priorRejectionReason}"
                  You MUST fix this issue in your new query.
                  """
                : string.Empty;

            var systemPrompt = $"""
                You are a PostgreSQL query generator for an invoice processing application.
                Given a natural language question, produce ONE valid read-only PostgreSQL SELECT query.

                {SchemaContext}
                {vendorConstraint}
                {retryContext}

                STRICT RULES:
                1. Return ONLY the raw SQL — no markdown fences, no explanation, no preamble,
                   nothing before or after the SQL statement.
                2. Only SELECT statements are allowed. Never use INSERT, UPDATE, DELETE,
                   DROP, TRUNCATE, ALTER, CREATE, GRANT, UNION, EXEC, COPY.
                3. Always include LIMIT (default 50, max 5000). Never use LIMIT 0.
                4. Never select PasswordHash or PasswordSalt.
                5. Use table aliases. ALWAYS wrap ALL column names in double-quotes — e.g.
                   "Id", "ProductName", "UploadedByVendorId", "InvoiceId", "ProductGuid",
                   "Category", "TotalRevenue", "LastSoldDate", "UpdatedAt", "CreatedAt".
                   Never use unquoted column names.
                6. For date ranges use NOW() - INTERVAL '...' syntax. Examples:
                   - This month:  WHERE i."InvoiceDate" >= DATE_TRUNC('month', NOW())
                   - Last 30 days: WHERE i."InvoiceDate" >= NOW() - INTERVAL '30 days'
                   - This year:   WHERE i."InvoiceDate" >= DATE_TRUNC('year', NOW())
                7. For ALL text searches, ALWAYS use ILIKE with wildcards, never =. Examples:
                   - Product name:    WHERE p."ProductName" ILIKE '%Bookrack%'
                   - Vendor name:     WHERE i."VendorName" ILIKE '%Acme%'
                   - Category:        WHERE p."Category" ILIKE '%furniture%'
                   - Invoice number:  WHERE i."InvoiceNumber" ILIKE '%INV-001%'
                   - File name:       WHERE fcl."FileName" ILIKE '%receipt%'
                   - Ship city:       WHERE i."ShipToCity" ILIKE '%New York%'
                8. For aggregation queries (totals, counts, averages), always include GROUP BY
                   for all non-aggregated columns in the SELECT list.
                9. Use COALESCE for nullable numeric columns to avoid NULL in results, e.g.
                   COALESCE(p."TotalRevenue", 0).
                10. When using SELECT DISTINCT, every column in ORDER BY MUST also appear
                    in the SELECT list. If ordering by a column not in SELECT, either add it
                    to SELECT or remove the ORDER BY.
                11. For vendor product queries, ALWAYS join through invoice_lines and invoices.
                    Never query products without WHERE i."UploadedByVendorId" = '<vendorId>'.
                12. Never produce multiple SQL statements separated by semicolons.
                    Only one SELECT per response.
                13. If the question cannot be answered with the schema, return exactly:
                    SELECT 'No matching data for this query' AS message;
                """;

            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = query }
            };

            var requestBody = new
            {
                model = groqModel,
                messages,
                temperature = 0.1,
                max_tokens = 1024
            };

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {groqApiKey}");

            var response = await client.PostAsync(
                "https://api.groq.com/openai/v1/chat/completions",
                new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json"));

            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var groqResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);

            var rawSql = groqResponse
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;

            // ── Post-processing pipeline ──────────────────────────────────────

            // 1. Strip markdown fences
            rawSql = Regex.Replace(rawSql, @"```(?:sql)?", "", RegexOptions.IgnoreCase).Trim();

            // 2. Strip any leading prose before the SELECT keyword
            var selectIndex = rawSql.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase);
            if (selectIndex > 0)
                rawSql = rawSql[selectIndex..];

            // 3. Strip any trailing prose — keep only up to the last semicolon
            var semiIndex = rawSql.LastIndexOf(';');
            if (semiIndex >= 0)
                rawSql = rawSql[..(semiIndex + 1)];

            // 4. Ensure single statement — strip anything after the first semicolon
            var firstSemi = rawSql.IndexOf(';');
            if (firstSemi >= 0 && firstSemi < rawSql.Length - 1)
            {
                var afterSemi = rawSql[(firstSemi + 1)..].Trim();
                if (afterSemi.Length > 0 && !afterSemi.StartsWith("--"))
                    rawSql = rawSql[..(firstSemi + 1)];
            }

            // 5. Remove trailing semicolon
            rawSql = rawSql.TrimEnd(';', ' ', '\n', '\r').Trim();

            // 6. Auto-fix unquoted PascalCase identifiers (e.g. i.Id -> i."Id")
            rawSql = Regex.Replace(
                rawSql,
                @"\b([a-z]{1,5})\.([A-Z][a-zA-Z]+)\b",
                m => $"{m.Groups[1].Value}.\"{m.Groups[2].Value}\"");

            // 7. Safety net: inject vendor scope if LLM forgot it
            if (isVendor && vendorId.HasValue)
                rawSql = InjectVendorScopeIfMissing(rawSql, vendorId.Value);

            // 8. Fix SELECT DISTINCT + ORDER BY column mismatch
            rawSql = FixDistinctOrderBy(rawSql);

            // 9. Clamp LIMIT to safe range (1–5000)
            rawSql = ClampLimit(rawSql, isVendor ? 200 : 5000);

            _logger.LogInformation("Generated SQL: {Sql}", rawSql);

            return rawSql;
        }

        // ── Safety net: inject vendor scope if LLM forgot it ─────────────────

        private static string InjectVendorScopeIfMissing(string sql, Guid vendorId)
        {
            var vendorIdStr = vendorId.ToString();

            if (sql.Contains(vendorIdStr, StringComparison.OrdinalIgnoreCase))
                return sql;

            bool hasProducts = Regex.IsMatch(sql, @"\bproducts\b", RegexOptions.IgnoreCase);
            bool hasInvoices = Regex.IsMatch(sql, @"\binvoices\b", RegexOptions.IgnoreCase);
            bool hasInvoiceLines = Regex.IsMatch(sql, @"\binvoice_lines\b", RegexOptions.IgnoreCase);

            // Case 1: bare products query — rewrite entirely with safe join pattern
            if (hasProducts && !hasInvoices && !hasInvoiceLines)
            {
                var limitMatch = Regex.Match(sql, @"\bLIMIT\s+(\d+)", RegexOptions.IgnoreCase);
                var limit = limitMatch.Success ? limitMatch.Groups[1].Value : "50";

                return $"""
                    SELECT DISTINCT p."ProductId", p."ProductName", p."Category",
                           p."PrimaryCategory", p."SecondaryCategory",
                           COALESCE(p."DefaultUnitRate", 0) AS "DefaultUnitRate",
                           p."LastSoldDate"
                    FROM products p
                    JOIN invoice_lines il ON il."ProductGuid" = p."Id"
                    JOIN invoices i ON il."InvoiceId" = i."Id"
                    WHERE i."UploadedByVendorId" = '{vendorIdStr}'
                    ORDER BY p."LastSoldDate" DESC NULLS LAST
                    LIMIT {limit}
                    """;
            }

            // Case 2: invoice_lines without invoices join
            if (hasInvoiceLines && !hasInvoices)
            {
                sql = Regex.Replace(
                    sql,
                    @"\bWHERE\b",
                    $"JOIN invoices i ON il.\"InvoiceId\" = i.\"Id\" WHERE i.\"UploadedByVendorId\" = '{vendorIdStr}' AND ",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                return sql;
            }

            // Case 3: invoices query missing vendor filter
            if (hasInvoices && !hasProducts)
            {
                if (Regex.IsMatch(sql, @"\bWHERE\b", RegexOptions.IgnoreCase))
                {
                    sql = Regex.Replace(
                        sql,
                        @"\bWHERE\b",
                        $"WHERE i.\"UploadedByVendorId\" = '{vendorIdStr}' AND ",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                else
                {
                    sql = Regex.Replace(
                        sql,
                        @"\b(ORDER\s+BY|LIMIT)\b",
                        $"WHERE i.\"UploadedByVendorId\" = '{vendorIdStr}' $1",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                return sql;
            }

            return sql;
        }

        // ── Fix SELECT DISTINCT + ORDER BY column mismatch ────────────────────

        private static string FixDistinctOrderBy(string sql)
        {
            if (!Regex.IsMatch(sql, @"\bSELECT\s+DISTINCT\b", RegexOptions.IgnoreCase))
                return sql;

            if (!Regex.IsMatch(sql, @"\bORDER\s+BY\b", RegexOptions.IgnoreCase))
                return sql;

            var selectMatch = Regex.Match(sql,
                @"SELECT\s+DISTINCT\s+(.*?)\s+FROM\b",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!selectMatch.Success) return sql;

            var selectList = selectMatch.Groups[1].Value;

            var orderByMatch = Regex.Match(sql,
                @"\bORDER\s+BY\s+(.*?)(?:\s+LIMIT|\s*$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!orderByMatch.Success) return sql;

            var orderByClause = orderByMatch.Groups[1].Value.Trim();
            bool needsFix = false;

            foreach (var term in orderByClause.Split(','))
            {
                var trimmed = term.Trim();
                var match = Regex.Match(trimmed, @"^(\w+)\.""?(\w+)""?", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var colName = match.Groups[2].Value;
                    if (!selectList.Contains(colName, StringComparison.OrdinalIgnoreCase))
                    {
                        needsFix = true;
                        break;
                    }
                }
            }

            if (needsFix)
            {
                sql = Regex.Replace(sql,
                    @"\s+ORDER\s+BY\s+.*?(?=\s+LIMIT|\s*$)",
                    "",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline).Trim();
            }

            return sql;
        }

        // ── Clamp LIMIT to safe range (1–200) ────────────────────────────────

        private static string ClampLimit(string sql, int max = 5000)
        {
            if (!Regex.IsMatch(sql, @"\bLIMIT\s+\d+\b", RegexOptions.IgnoreCase))
            {
                return sql.TrimEnd() + " LIMIT 50";
            }
            return Regex.Replace(sql,
                @"\bLIMIT\s+(\d+)\b",
                m =>
                {
                    var val = int.Parse(m.Groups[1].Value);
                    if (val <= 0) val = 50;
                    if (val > max) val = max;
                    return $"LIMIT {val}";
                },
                RegexOptions.IgnoreCase);
        }

        // ── Helper: safe logging to SearchLogs (never throw) ────────────────

        private async Task SafeSaveSearchLogAsync(SearchLog log)
        {
            try
            {
                // Add and attempt to save; any failure is logged but swallowed so we don't impact search behavior.
                await _db.SearchLogs.AddAsync(log);
                await _db.SaveChangesAsync();
            }
            catch (Exception dbEx)
            {
                // Log the DB failure but do not rethrow — the search functionality must remain unchanged.
                _logger.LogError(dbEx, "Failed to persist SearchLog (non-fatal). Log: {@Log}", log);
            }
        }
    }
}
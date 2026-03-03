using System.Text.RegularExpressions;

namespace invoice_v1.src.Application.Services
{
    public static class SearchSecurityValidator
    {
        // ── Columns that must NEVER appear in results ─────────────────────────
        private static readonly string[] BlockedColumns =
        {
            "PasswordHash", "PasswordSalt"
        };

        // ── Tables vendors cannot query at all ────────────────────────────────
        private static readonly string[] VendorBlockedTables =
        {
            "Users"
        };

        // ── SQL keywords/patterns that are dangerous ──────────────────────────
        private static readonly string[] BlockedSqlPatterns =
        {
            @"\bINSERT\b", @"\bUPDATE\b", @"\bDELETE\b", @"\bDROP\b",
            @"\bTRUNCATE\b", @"\bALTER\b", @"\bCREATE\b", @"\bGRANT\b",
            @"\bREVOKE\b", @"\bEXEC\b", @"\bEXECUTE\b", @"\bCOPY\b",
            @"\bPG_SLEEP\b", @"\bPG_READ_FILE\b", @"\bPG_WRITE_FILE\b",
            @"\bUNION\b",
            @"--",
            @"/\*",
            @"\bINFORMATION_SCHEMA\b",
            @"\bPG_CATALOG\b",
            @"\bCURRENT_USER\b",
            @"\bSESSION_USER\b",
            @"\bSELECT\s+INTO\b",   // prevents data exfiltration via SELECT INTO
            @"\bINTO\s+OUTFILE\b"
        };

        // ── Input-level checks (before LLM call) ─────────────────────────────

        /// <summary>
        /// Validates and sanitises the raw user input before it is sent to the LLM.
        /// Throws InvalidOperationException with a safe user-facing message on failure.
        /// </summary>
        public static string SanitiseInput(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new InvalidOperationException("Query cannot be empty.");

            if (query.Length > 500)
                throw new InvalidOperationException(
                    "Query must be 500 characters or fewer.");

            var suspiciousInputPatterns = new[]
            {
                @"ignore\s+(previous|above|all)\s+instructions",
                @"disregard\s+(previous|above|all)",
                @"you\s+are\s+now",
                @"forget\s+(everything|all)",
                @"new\s+instructions",
                @"system\s*:",
                @"<\s*system\s*>",
                @"\bDROP\s+TABLE\b",
                @"\bDELETE\s+FROM\b",
                @"\bINSERT\s+INTO\b",
                @"\bUPDATE\s+\w+\s+SET\b",
                @"\bUNION\s+SELECT\b",
                @"\bFROM\s+""?Users""?\b",
                @"\bSELECT\s+.*?\bFROM\s+""?Users""?\b"
            };

            foreach (var pattern in suspiciousInputPatterns)
            {
                if (Regex.IsMatch(query, pattern, RegexOptions.IgnoreCase))
                    throw new InvalidOperationException(
                        "Query contains disallowed content.");
            }

            return query.Trim();
        }

        // ── SQL-level checks (after LLM returns SQL) ──────────────────────────

        /// <summary>
        /// Full security validation of LLM-generated SQL.
        /// Returns (true, null, true) if safe.
        /// Returns (false, reason, isRetryable) if rejected.
        /// </summary>
        public static (bool IsValid, string? Reason, bool IsRetryable) ValidateSql(
            string sql,
            bool isVendor,
            Guid? vendorId)
        {
            if (string.IsNullOrWhiteSpace(sql))
                return (false, "Generated SQL is empty.", true);

            var normalised = sql.Trim();

            // 1. Must be a SELECT
            if (!normalised.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                return (false, "Only SELECT queries are permitted.", true);

            // 2. Must not contain multiple statements
            var stripped = Regex.Replace(normalised, @"'[^']*'", "''");
            var statementCount = stripped.Count(c => c == ';');
            if (statementCount > 1)
                return (false, "Only a single SQL statement is permitted.", true);

            // 3. Dangerous keyword blocklist (HARD VIOLATION)
            foreach (var pattern in BlockedSqlPatterns)
            {
                if (Regex.IsMatch(normalised, pattern, RegexOptions.IgnoreCase))
                    return (false, "Query contains disallowed SQL construct.", false);
            }

            // 4. Sensitive columns must never appear (HARD VIOLATION)
            foreach (var col in BlockedColumns)
            {
                if (normalised.Contains(col, StringComparison.OrdinalIgnoreCase))
                    return (false, "Query references a restricted column.", false);
            }

            // 5. Vendors cannot query the Users table at all (HARD VIOLATION)
            if (isVendor)
            {
                foreach (var table in VendorBlockedTables)
                {
                    if (Regex.IsMatch(normalised,
                        $@"\b{Regex.Escape(table)}\b",
                        RegexOptions.IgnoreCase))
                    {
                        return (false, "Access to requested data is not permitted.", false);
                    }
                }
            }

            // 6. Vendor scope check (HARD VIOLATION)
            if (isVendor && vendorId.HasValue)
            {
                var vendorIdStr = vendorId.Value.ToString();
                if (!normalised.Contains(vendorIdStr, StringComparison.OrdinalIgnoreCase))
                {
                    return (false,
                        "Search could not be completed. Try rephrasing — for example: 'show my invoices' or 'show my products'.",
                        false);
                }

                // Products table has no vendor column — must join through invoices
                bool queriesProducts = Regex.IsMatch(normalised, @"\bproducts\b", RegexOptions.IgnoreCase);
                bool joinsInvoices = Regex.IsMatch(normalised, @"\binvoices\b", RegexOptions.IgnoreCase);
                if (queriesProducts && !joinsInvoices)
                {
                    return (false,
                        "Search could not be completed. Try rephrasing — for example: 'show my products' or 'what products have I sold'.",
                        false);
                }
            }

            // 7. Must contain LIMIT (SOFT VIOLATION - we auto-inject, but if it's missing or wrong, it's retryable)
            var limitMatch = Regex.Match(normalised, @"\bLIMIT\s+(\d+)\b", RegexOptions.IgnoreCase);
            if (!limitMatch.Success)
                return (false, "Generated query is missing a LIMIT clause.", true);

            var limitVal = int.Parse(limitMatch.Groups[1].Value);
            var maxAllowed = isVendor ? 200 : 5000;
            if (limitVal <= 0 || limitVal > maxAllowed)
                return (false, $"Generated query has an invalid LIMIT value (max {maxAllowed}).", true);

            return (true, null, true);
        }
    }
}
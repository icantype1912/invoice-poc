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
                @"\bUNION\s+SELECT\b"
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
        /// Returns (true, null) if safe, (false, reason) if rejected.
        /// </summary>
        public static (bool IsValid, string? Reason) ValidateSql(
            string sql,
            bool isVendor,
            Guid? vendorId)
        {
            if (string.IsNullOrWhiteSpace(sql))
                return (false, "Generated SQL is empty.");

            var normalised = sql.Trim();

            // 1. Must be a SELECT
            if (!normalised.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                return (false, "Only SELECT queries are permitted.");

            // 2. Must not contain multiple statements
            // Strip string literals first to avoid false positives on semicolons in strings
            var stripped = Regex.Replace(normalised, @"'[^']*'", "''");
            var statementCount = stripped.Count(c => c == ';');
            if (statementCount > 1)
                return (false, "Only a single SQL statement is permitted.");

            // 3. Dangerous keyword blocklist
            foreach (var pattern in BlockedSqlPatterns)
            {
                if (Regex.IsMatch(normalised, pattern, RegexOptions.IgnoreCase))
                    return (false, "Query contains disallowed SQL construct.");
            }

            // 4. Sensitive columns must never appear
            foreach (var col in BlockedColumns)
            {
                if (normalised.Contains(col, StringComparison.OrdinalIgnoreCase))
                    return (false, "Query references a restricted column.");
            }

            // 5. Vendors cannot query the Users table at all
            if (isVendor)
            {
                foreach (var table in VendorBlockedTables)
                {
                    if (Regex.IsMatch(normalised,
                        $@"\b{Regex.Escape(table)}\b",
                        RegexOptions.IgnoreCase))
                    {
                        return (false, "Access to requested data is not permitted.");
                    }
                }
            }

            // 6. Vendor scope check — vendorId must appear literally in the SQL
            if (isVendor && vendorId.HasValue)
            {
                var vendorIdStr = vendorId.Value.ToString();
                if (!normalised.Contains(vendorIdStr, StringComparison.OrdinalIgnoreCase))
                {
                    return (false,
                        "Search could not be completed. Try rephrasing — for example: 'show my invoices' or 'show my products'.");
                }

                // Products table has no vendor column — must join through invoices
                bool queriesProducts = Regex.IsMatch(normalised, @"\bproducts\b", RegexOptions.IgnoreCase);
                bool joinsInvoices = Regex.IsMatch(normalised, @"\binvoices\b", RegexOptions.IgnoreCase);
                if (queriesProducts && !joinsInvoices)
                {
                    return (false,
                        "Search could not be completed. Try rephrasing — for example: 'show my products' or 'what products have I sold'.");
                }
            }

            // 7. Must contain LIMIT with a sane value (1–200)
            var limitMatch = Regex.Match(normalised, @"\bLIMIT\s+(\d+)\b", RegexOptions.IgnoreCase);
            if (!limitMatch.Success)
                return (false, "Generated query is missing a LIMIT clause.");

            var limitVal = int.Parse(limitMatch.Groups[1].Value);
            if (limitVal <= 0 || limitVal > 200)
                return (false, "Generated query has an invalid LIMIT value.");

            return (true, null);
        }
    }
}
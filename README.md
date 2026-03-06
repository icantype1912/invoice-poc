# Invoice Processing System

An end-to-end, AI-powered invoice processing platform built with a .NET 8 backend, an Angular 21 frontend, and a Python AI worker. The system automates the detection, extraction, validation, and storage of invoice data from uploaded PDFs using LLM-based intelligence (Groq / Llama 3.3). It includes role-based access control, a product analytics dashboard, a natural-language Q&A chatbot for querying invoice data, and a background job pipeline with automatic retry logic.

---

## Architecture

```
                          +-------------------+
                          |   Angular 21 SPA  |
                          |   (Frontend)      |
                          +--------+----------+
                                   |
                                   | HTTP / JWT
                                   v
+------------------+      +-------------------+      +-------------------+
| Google Drive     | ---> | .NET 8 Backend    | <--- | Python Worker     |
| (Invoice Source) |      | (REST API)        |      | (AI Extractor)    |
+------------------+      +--------+----------+      +--------+----------+
                                   |                          |
                          +--------v----------+      +--------v----------+
                          | PostgreSQL        |      | Groq LLM          |
                          | (Database)        |      | (Llama 3.3 70B)   |
                          +-------------------+      +-------------------+
                                   |
                          +--------v----------+
                          | Redis (optional)  |
                          | (Caching)         |
                          +-------------------+
```

**Data flow:**

1. Vendors upload invoice PDFs through the Angular frontend or directly to a monitored Google Drive folder.
2. A background service in the .NET backend detects new files and creates processing jobs.
3. The Python worker claims jobs from the queue, downloads the PDFs, and extracts structured data using OCR and the Groq LLM.
4. Extracted data is sent back to the backend via HMAC-signed callbacks and persisted to PostgreSQL.
5. Users can view invoices, products, analytics dashboards, and query the data using the natural-language Q&A bot.

---

## Features

- **Automated invoice detection** -- Monitors a Google Drive folder for new uploads and creates processing jobs automatically.
- **AI-powered extraction** -- Uses Groq (Llama 3.3 70B) for intelligent field extraction from invoice PDFs (OCR + LLM).
- **Role-based access control** -- JWT authentication with Admin and Vendor roles. Vendors see only their own data.
- **Product analytics dashboard** -- Revenue trends, category breakdowns, trending products, and time-series charts with configurable date ranges.
- **Natural-language Q&A bot** -- Ask questions about invoice data in plain English. The backend translates queries into read-only SQL and returns results. Includes rate limiting and SQL injection protection.
- **File security pipeline** -- Uploaded files pass through file-type validation, magic-byte verification, token-count limits, and VirusTotal scanning.
- **Job queue with retry** -- Failed jobs are retried with configurable policies. Permanently failed jobs are flagged as invalid for review.
- **Structured data storage** -- Invoices, line items, products, and vendor metadata are stored in PostgreSQL with EF Core.
- **Caching** -- Optional Redis-backed distributed cache. Falls back to in-memory caching when Redis is not configured.
- **Secure callbacks** -- HMAC-SHA256 signed communication between the Python worker and the .NET backend.
- **Comprehensive logging** -- Serilog-based structured logging with console and rolling file sinks.

---

## Tech Stack

| Layer      | Technology                                        |
| ---------- | ------------------------------------------------- |
| Frontend   | Angular 21, Angular Material, TypeScript           |
| Backend    | .NET 8, ASP.NET Core, Entity Framework Core        |
| Worker     | Python 3.11, Groq SDK, PyMuPDF, pytesseract        |
| Database   | PostgreSQL                                         |
| Cache      | Redis (optional, falls back to in-memory)          |
| LLM        | Groq API (Llama 3.3 70B Versatile)                 |
| Storage    | Google Drive (via service account)                  |
| Auth       | JWT Bearer tokens                                  |
| Logging    | Serilog                                            |
| Testing    | xUnit, Moq, Vitest, Cypress                        |

---

## Project Structure

```
invoice-poc/
|-- backend/                     .NET 8 Backend API
|   |-- src/
|   |   |-- Api/
|   |   |   |-- Controllers/     REST controllers (Auth, Invoices, Products, Jobs, Analytics, Search, etc.)
|   |   |   +-- Middleware/      Exception handling middleware
|   |   |-- Application/
|   |   |   |-- BackgroundServices/  Job creation, retry, and Drive monitoring
|   |   |   |-- DTOs/            Data transfer objects
|   |   |   |-- Interfaces/     Service and repository contracts
|   |   |   |-- Security/       Password hashing, HMAC, JWT, file validation pipeline
|   |   |   +-- Services/       Business logic (Auth, Invoice, Search, Analytics, etc.)
|   |   |-- Domain/
|   |   |   |-- Entities/       EF Core entity models
|   |   |   +-- Enums/          Status and role enumerations
|   |   +-- Infrastructure/
|   |       |-- Data/           DbContext and configuration
|   |       +-- Repositories/   Data access layer
|   |-- Migrations/              EF Core database migrations
|   |-- Program.cs               Application entry point and DI configuration
|   |-- appsettings.json         Application configuration (git-ignored values)
|   +-- Dockerfile
|
|-- frontend/                    Angular 21 SPA
|   |-- src/app/
|   |   |-- core/               Guards, interceptors, services, layout (navbar)
|   |   +-- features/
|   |       |-- auth/           Login and registration
|   |       |-- dashboard/      Analytics dashboard with charts
|   |       |-- invoices/       Invoice listing and detail views
|   |       |-- products/       Product catalog
|   |       |-- job-queue/      Job queue monitoring
|   |       |-- search/         Q&A chatbot interface
|   |       |-- upload/         Invoice upload
|   |       |-- admin/          User administration
|   |       |-- logs-component/ File change logs
|   |       +-- landing/        Landing page
|   |-- cypress/                 End-to-end tests
|   +-- Dockerfile
|
|-- worker/                      Python 3.11 AI Worker
|   |-- app/
|   |   |-- database/           Job claimer (PostgreSQL)
|   |   |-- extractors/         OCR, PDF text, and LLM-based extraction
|   |   |-- services/           Callback, Drive download, MIME detection
|   |   |-- models/             Pydantic data models
|   |   +-- utils/              HMAC signing, text cleaning utilities
|   |-- tests/                   Worker unit tests (pytest)
|   +-- Dockerfile
|
|-- tests/
|   |-- Invoice-v1.UnitTests/        Backend unit tests (xUnit + Moq)
|   +-- Invoice-v1.IntegrationTests/ Backend integration tests
|
|-- secrets/                     Git-ignored folder for credentials
|-- .env.template                Root environment variable template
|-- .github/workflows/           CI/CD pipeline
+-- invoice-v1.slnx             Visual Studio solution file
```

---

## Prerequisites

- .NET 8 SDK
- Node.js (v18 or later) and npm
- Python 3.11+
- PostgreSQL 14+ (running locally or via Docker)
- A Google Cloud service account with Drive API access
- A Groq API key (https://console.groq.com)
- (Optional) Redis, for distributed caching

---

## Local Development Setup

### 1. Clone the Repository

```bash
git clone https://github.com/akdino27/invoice-poc.git
cd invoice-poc
```

### 2. Database Setup (PostgreSQL)

Ensure PostgreSQL is running. Create the database if it does not already exist:

```sql
CREATE DATABASE "InvoiceProcessingV2";
```

The backend will apply EF Core migrations automatically on startup, creating all required tables.

### 3. Q&A Bot Database Configuration

The Q&A / search chatbot executes read-only SQL queries against the database. It requires a dedicated read-only PostgreSQL user so that the chatbot can never modify data, even if a malicious query is attempted. Run the following SQL against your PostgreSQL instance using a superuser (e.g., `postgres`) via pgAdmin or `psql`:

```sql
-- 1. Create the read-only user
CREATE USER search_readonly WITH PASSWORD 'your_secure_password';

-- 2. Grant connection permission to the database
GRANT CONNECT ON DATABASE "InvoiceProcessingV2" TO search_readonly;

-- 3. Grant usage on the public schema
GRANT USAGE ON SCHEMA public TO search_readonly;

-- 4. Grant SELECT on all existing tables (read-only access)
GRANT SELECT ON ALL TABLES IN SCHEMA public TO search_readonly;

-- 5. Ensure future tables also get read-only access for this user
ALTER DEFAULT PRIVILEGES IN SCHEMA public
GRANT SELECT ON TABLES TO search_readonly;

-- 6. Grant SELECT on sequences (required for certain JOIN operations)
GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO search_readonly;
```

After creating the user, update the `SearchConnection` connection string in the backend configuration (see step 4 below) to use this user's credentials.

### 4. Backend Configuration

Edit `backend/appsettings.json` (or create one from the template) with your local values:

```jsonc
{
  "ConnectionStrings": {
    // Primary connection (used by EF Core for migrations and writes)
    "DefaultConnection": "Host=localhost;Port=5432;Database=InvoiceProcessingV2;Username=postgres;Password=YOUR_PASSWORD;Include Error Detail=true",
    // Read-only connection (used by the Q&A chatbot)
    "SearchConnection": "Host=localhost;Database=InvoiceProcessingV2;Username=search_readonly;Password=YOUR_SEARCH_PASSWORD"
  },
  "Jwt": {
    "Issuer": "invoice-v1-api",
    "Audience": "invoice-v1-client",
    "Secret": "YOUR_JWT_SECRET_BASE64_MIN_64_CHARS",
    "AccessTokenMinutes": 60
  },
  "GoogleDrive": {
    "ServiceAccountKeyPath": "/absolute/path/to/service-account-key.json",
    "SharedFolderId": "YOUR_GOOGLE_DRIVE_FOLDER_ID"
  },
  "Groq": {
    "ApiKey": "gsk_YOUR_GROQ_API_KEY",
    "Model": "llama-3.3-70b-versatile"
  },
  "Security": {
    "CallbackSecret": "YOUR_HMAC_SECRET_BASE64",
    "AdminApiKey": "YOUR_ADMIN_API_KEY",
    "MaxTokensAllowed": 120000,
    "MaxUploadsPerHour": 20
  },
  "AdminBootstrap": {
    "Email": "admin@invoice.com",
    "Password": "YourAdminPassword!"
  },
  "Cors": {
    "AllowedOrigins": "http://localhost:4200"
  }
}
```

Key notes:
- `DefaultConnection` is the primary connection used by Entity Framework Core for all migrations and write operations.
- `SearchConnection` is a separate, read-only connection used exclusively by the Q&A chatbot. It must use the `search_readonly` user created in step 3.
- The `AdminBootstrap` section seeds an initial admin account on first startup.

### 5. Run the Backend

```bash
cd backend
dotnet restore
dotnet run
```

The API starts at `http://localhost:5247`. Swagger UI is available at the root URL in development mode.

### 6. Frontend Setup

```bash
cd frontend
npm install
npm start
```

The Angular application starts at `http://localhost:4200`.

### 7. Worker Setup

```bash
cd worker

# Create and activate a virtual environment
python -m venv venv

# Windows
venv\Scripts\activate

# Linux / macOS
source venv/bin/activate

# Install dependencies
pip install -r requirements.txt
```

Create `worker/.env` from the template and fill in your values:

```bash
cp .env.template .env
```

```env
DB_HOST=localhost
DB_PORT=5432
DB_NAME=InvoiceProcessingV2
DB_USER=postgres
DB_PASSWORD=YOUR_PASSWORD

BACKEND_URL=http://localhost:5247
CALLBACK_SECRET=YOUR_HMAC_SECRET_BASE64

GOOGLE_SERVICE_ACCOUNT_KEY=./secrets/service-account-key.json

GROQ_API_KEY=gsk_YOUR_GROQ_API_KEY
GROQ_MODEL=llama-3.3-70b-versatile

WORKER_ID=worker-1
POLL_INTERVAL=5
```

Run the worker:

```bash
python -m app.main
```

### 8. Verify the Setup

1. Open the frontend at `http://localhost:4200`.
2. Log in with the admin credentials configured in `AdminBootstrap`.
3. Upload an invoice PDF through the upload page or place one in the monitored Google Drive folder.
4. Monitor the backend and worker logs to confirm job creation and processing.
5. Check the invoices, products, and analytics pages to see extracted data.
6. Try the Q&A search page to query your invoice data in natural language.

---

## Testing

### Backend Unit Tests

```bash
cd tests/Invoice-v1.UnitTests
dotnet test
```

### Backend Integration Tests

```bash
cd tests/Invoice-v1.IntegrationTests
dotnet test
```

### Worker Tests

```bash
cd worker
pytest tests/ -v
```

### Frontend End-to-End Tests (Cypress)

```bash
cd frontend
npx cypress open
```

---

## Database Migrations

The backend applies pending migrations automatically on startup. To manage migrations manually:

```bash
cd backend

# Create a new migration
dotnet ef migrations add MigrationName

# Apply pending migrations
dotnet ef database update

# Rollback to a specific migration
dotnet ef database update PreviousMigrationName

# Remove the last migration (only if not yet applied)
dotnet ef migrations remove
```

---

## Environment Variables Reference

### Backend (`appsettings.json`)

| Key                                   | Description                                    |
| ------------------------------------- | ---------------------------------------------- |
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string (primary, read-write) |
| `ConnectionStrings:SearchConnection`  | PostgreSQL connection string (read-only, for Q&A bot) |
| `Jwt:Secret`                          | JWT signing secret (base64, minimum 64 characters) |
| `Jwt:AccessTokenMinutes`              | Token expiration in minutes                     |
| `GoogleDrive:ServiceAccountKeyPath`   | Absolute path to Google service account JSON key |
| `GoogleDrive:SharedFolderId`          | Google Drive folder ID to monitor               |
| `Groq:ApiKey`                         | Groq LLM API key                                |
| `Groq:Model`                          | Groq model name (e.g., `llama-3.3-70b-versatile`) |
| `Security:CallbackSecret`            | HMAC-SHA256 callback secret (base64)            |
| `Security:AdminApiKey`               | Admin API key for protected endpoints            |
| `Security:MaxTokensAllowed`          | Maximum token count for uploaded files           |
| `Security:MaxUploadsPerHour`         | Rate limit for file uploads per hour             |
| `AdminBootstrap:Email`               | Email for the seeded admin account               |
| `AdminBootstrap:Password`            | Password for the seeded admin account            |
| `Cors:AllowedOrigins`                | Comma-separated allowed CORS origins             |

### Worker (`.env`)

| Key                          | Description                                      |
| ---------------------------- | ------------------------------------------------ |
| `DB_HOST`                    | PostgreSQL hostname                               |
| `DB_PORT`                    | PostgreSQL port (default: 5432)                   |
| `DB_NAME`                    | Database name                                     |
| `DB_USER`                    | Database username                                 |
| `DB_PASSWORD`                | Database password                                 |
| `BACKEND_URL`                | Backend API URL for callbacks                     |
| `CALLBACK_SECRET`            | HMAC secret (must match backend)                  |
| `GOOGLE_SERVICE_ACCOUNT_KEY` | Path to Google service account JSON key            |
| `GROQ_API_KEY`               | Groq LLM API key                                  |
| `GROQ_MODEL`                 | Groq model name                                   |
| `WORKER_ID`                  | Unique identifier for this worker instance         |
| `POLL_INTERVAL`              | Job polling interval in seconds                    |

---

## Security Notes

- Never commit `.env` files, `appsettings.json` with real credentials, or `service-account-key.json` to version control.
- Store all secrets in git-ignored files (`.env`, `secrets/`).
- The Q&A bot connects to PostgreSQL through a dedicated read-only user (`search_readonly`). This ensures that even if a query is maliciously crafted, it cannot modify or delete data.
- HMAC-SHA256 authentication secures all worker-to-backend callback communication.
- The `CALLBACK_SECRET` must be identical in both the backend (`appsettings.json`) and the worker (`.env`).
- Uploaded files are validated through a multi-stage security pipeline before processing.
- Rotate secrets regularly and follow the principle of least privilege for all service accounts.

---

## Troubleshooting

### Backend cannot connect to PostgreSQL

- Confirm PostgreSQL is running and accepting connections on the configured host and port.
- Verify the `DefaultConnection` string in `appsettings.json`.
- Check that the database `InvoiceProcessingV2` exists.

### Q&A bot returns permission errors

- Confirm that the `search_readonly` user was created with the SQL commands in step 3.
- Verify the `SearchConnection` string in `appsettings.json` uses the `search_readonly` credentials.
- If new tables were created before the `ALTER DEFAULT PRIVILEGES` command was run, re-run step 4 of the SQL setup to grant SELECT on those tables.

### Worker cannot connect to the database

- Verify the `DB_HOST`, `DB_PORT`, `DB_USER`, and `DB_PASSWORD` values in `worker/.env`.
- If running PostgreSQL in Docker, ensure the port is mapped to the host.

### HMAC validation failed

- The `CALLBACK_SECRET` must be the exact same base64 string in both:
  - Backend: `appsettings.json` under `Security:CallbackSecret`
  - Worker: `.env` as `CALLBACK_SECRET`

### LLM extraction fails (rate limit)

- Check your Groq API quota at the Groq console.
- Verify the `GROQ_API_KEY` is valid and has not expired.
- The worker implements retry logic, but sustained rate-limit errors require a quota increase.

### Frontend cannot reach the backend

- Confirm the backend is running at `http://localhost:5247`.
- Check that `Cors:AllowedOrigins` in `appsettings.json` includes `http://localhost:4200`.

---

## License

This project is for internal use.

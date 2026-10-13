# SalesApp

A sales management application for an Indian trading business: customers, products, quotations and
orders, GST invoices, payments, stock, and reports, with Claude-powered AI features built in.

This is a ground-up rebuild of an ASP.NET MVC 5 application I wrote early in my career. The original
source was lost, so the app is being rebuilt on a modern stack, with AI designed in from the start.

## Status

| Phase | Scope | State |
|---|---|---|
| P0 | Foundation: solution layout, sign-in (JWT + roles), React app shell, tests | ✅ Done |
| P1 | Customers, products, HSN/GST rates, users, company settings | Planned |
| P2 | Inventory: stock ledger, receipts, adjustments, low-stock list | Planned |
| P3 | Orders and quotations, GST invoicing (CGST/SGST/IGST), payments | Planned |
| P4 | Demo data generator, dashboard, reports, Excel export | Planned |
| P5 | AI: smart order entry from email/WhatsApp text, PDF or photo | Planned |
| P6 | AI: ask your sales data in plain English (read-only, sandboxed SQL) | Planned |
| P7 | AI: assistant that drafts follow-ups, quotations and payment reminders | Planned |
| P8 | AI: insights, anomaly detection and demand forecasting | Planned |
| P9 | Hardening, AI usage dashboard, deployment guide | Planned |

## Tech stack

| Layer | Choice |
|---|---|
| Frontend | React 19, TypeScript, Vite, React Router, TanStack Query, Tailwind CSS + shadcn/ui, React Hook Form + Zod |
| API | ASP.NET Core 10 Web API, built-in OpenAPI + Scalar, JWT bearer auth |
| Data | EF Core 10 on SQL Server (LocalDB in development) |
| Auth | ASP.NET Core Identity (users, roles, password hashing, lockout) + JWT. Roles: Admin, SalesManager, SalesRep |
| AI (from P5) | Claude API via the official `Anthropic` C# SDK. All calls happen server-side. |
| Tests | xUnit v3 (unit + integration against a throwaway LocalDB database), Vitest + Testing Library |

## Prerequisites

- **.NET 10 SDK** (pinned in `global.json`)
- **Node.js 22+**
- **SQL Server LocalDB 2019 or later** (`sqllocaldb info MSSQLLocalDB`). EF Core 10 doesn't support SQL Server 2008/2008 R2.

Visual Studio is optional: everything runs from the command line or VS Code.

## Getting started

```powershell
# One-time setup: restores tools, creates a dev JWT key in user-secrets, installs npm packages
./scripts/setup-dev.ps1

# Terminal 1: API on http://localhost:5080. Creates and migrates the SalesAppDb database on first run.
dotnet run --project backend/src/SalesApp.Api --launch-profile http

# Terminal 2: React app on http://localhost:5173 (proxies /api to the API)
npm run dev --prefix frontend
```

Open <http://localhost:5173> and sign in with the development admin account. Its email and password are
in `backend/src/SalesApp.Api/appsettings.Development.json`, under `Seed`. That account exists only in your
local LocalDB database.

API reference (development only): <http://localhost:5080/scalar>

## Common tasks

| Task | Command |
|---|---|
| Backend tests | `dotnet test --solution backend/SalesApp.slnx` |
| Frontend tests | `npm test --prefix frontend` |
| Frontend lint / typecheck | `npm run lint --prefix frontend` · `npm run typecheck --prefix frontend` |
| Regenerate TypeScript API types (API must be running) | `npm run gen:api --prefix frontend` |
| Add an EF Core migration | `dotnet ef migrations add <Name> --project backend/src/SalesApp.Infrastructure --startup-project backend/src/SalesApp.Api --output-dir Data/Migrations` |
| Production build of the UI (output goes to the API's `wwwroot`) | `npm run build --prefix frontend` |

## Configuration and secrets

Secrets are never committed. In development they live in
[dotnet user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets); in production, use
environment variables (for example `Jwt__SigningKey`).

| Setting | Where | Notes |
|---|---|---|
| `ConnectionStrings:Default` | `appsettings.Development.json` | LocalDB, Windows authentication |
| `Jwt:SigningKey` | user-secrets | Random, at least 32 characters. `setup-dev.ps1` generates it. |
| `Seed:AdminPassword` | `appsettings.Development.json` (dev only) / user-secrets or env elsewhere | Creates the first admin when none exists |
| `Anthropic:ApiKey` | user-secrets | Needed from P5. The app works without it; AI features stay hidden. |

## Project structure

```
backend/
  src/SalesApp.Api/              Controllers, auth, error handling, OpenAPI
  src/SalesApp.Core/             Entities, DTOs, business rules (no EF, no ASP.NET)
  src/SalesApp.Infrastructure/   EF Core DbContext, migrations, seeding
  tests/SalesApp.Tests/          Unit tests
  tests/SalesApp.IntegrationTests/  API tests against a throwaway LocalDB database
frontend/
  src/features/                  Feature folders (auth, ...)
  src/components/                App shell and shadcn/ui components
  src/lib/api/                   Typed API client generated from OpenAPI
scripts/setup-dev.ps1            One-time developer setup
```

## Conventions

- **One error shape** for every API failure: `{ message, traceId, errors? }`. `errors` is keyed by camelCase field name, so forms can show messages next to the right input.
- **UTC timestamps** everywhere (`*Utc` columns, `TimeProvider` for testability).
- **`CancellationToken`** passed through every async layer.
- **Money is `decimal(18,2)`**; quantities will be `decimal(18,3)`.

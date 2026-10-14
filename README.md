# SalesApp

A sales management application for an Indian trading business: customers, products, quotations and
orders, GST invoices, payments, stock, and reports, with Claude-powered AI features built in.

This is a ground-up rebuild of an ASP.NET MVC 5 application I wrote early in my career. The original
source was lost, so the app is being rebuilt on a modern stack, with AI designed in from the start.

## Status

| Phase | Scope | State |
|---|---|---|
| P0 | Foundation: solution layout, sign-in (JWT + roles), React app shell, tests | ✅ Done |
| P1 | Customers, products, HSN/GST rates, users, company settings | ✅ Done |
| P2 | Inventory: stock ledger, receipts, adjustments, low-stock list | ✅ Done |
| P3 | Orders and quotations, GST invoicing (CGST/SGST/IGST), payments | ✅ Done |
| P4 | Demo data generator, dashboard, reports, Excel export | ✅ Done |
| P5 | AI: smart order entry from email/WhatsApp text, PDF or photo | ✅ Done |
| P6 | AI: ask your sales data in plain English (read-only, sandboxed SQL) | ✅ Built (live check with a real key pending) |
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

### Demo data

A fresh database is empty. To see the dashboard, reports and (later) the AI features with something to
work on, generate two years of synthetic trading history for a Tamil Nadu textile and yarn distributor:

```powershell
# Stop the API first. --reset replaces existing customers, products, orders, invoices and stock;
# users, company settings and GST reference data are kept.
dotnet run --project backend/src/SalesApp.Api -- --seed-demo --months 24 --reset
```

It runs in under 10 seconds and is repeatable (fixed random seed). The history has a festive October to
November peak, year-on-year growth, customers who stop buying, slow payers, a few stockouts and a bulk
order, and a current pipeline of open orders and products due for reorder. It also creates a demo team
to sign in as, each with the password `Seed:DemoUserPassword` from `appsettings.Development.json`:

| User | Role |
|---|---|
| `meena.krishnan@salesapp.local` | Sales manager (sees margins and stock value) |
| `arun.kumar@salesapp.local`, `divya.rajan@salesapp.local`, `farhan.sheikh@salesapp.local` | Sales reps |

All names, GSTINs and figures are made up. The command only runs in the Development environment.

## Common tasks

| Task | Command |
|---|---|
| Backend tests | `dotnet test --solution backend/SalesApp.slnx` |
| Frontend tests | `npm test --prefix frontend` |
| Frontend lint / typecheck | `npm run lint --prefix frontend` · `npm run typecheck --prefix frontend` |
| Regenerate TypeScript API types (API must be running) | `npm run gen:api --prefix frontend` |
| Add an EF Core migration | `dotnet ef migrations add <Name> --project backend/src/SalesApp.Infrastructure --startup-project backend/src/SalesApp.Api --output-dir Data/Migrations` |
| Production build of the UI (output goes to the API's `wwwroot`) | `npm run build --prefix frontend` |
| Regenerate demo data (API stopped) | `dotnet run --project backend/src/SalesApp.Api -- --seed-demo --months 24 --reset` |

## Configuration and secrets

Secrets are never committed. In development they live in
[dotnet user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets); in production, use
environment variables (for example `Jwt__SigningKey`).

| Setting | Where | Notes |
|---|---|---|
| `ConnectionStrings:Default` | `appsettings.Development.json` | LocalDB, Windows authentication |
| `Jwt:SigningKey` | user-secrets | Random, at least 32 characters. `setup-dev.ps1` generates it. |
| `Seed:AdminPassword` | `appsettings.Development.json` (dev only) / user-secrets or env elsewhere | Creates the first admin when none exists |
| `Seed:DemoUserPassword` | `appsettings.Development.json` (dev only) | Password for the demo team created by `--seed-demo` |
| `Anthropic:ApiKey` | user-secrets / `Anthropic__ApiKey` env | Turns on the AI features. Without it the app works normally and hides them. |
| `Ai:*` | `appsettings.json` | Model and effort per feature, daily budget per user, requests per minute, price list. See [AI features](#ai-features). |

## AI features

AI runs on the server only; the browser never sees the API key. To turn it on, add your
[Claude API key](https://console.anthropic.com/) to user-secrets and restart the API:

```powershell
dotnet user-secrets set "Anthropic:ApiKey" "<your key>" --project backend/src/SalesApp.Api
```

`GET /api/ai/status` reports whether AI is available; the UI shows AI screens only when it is.

**Smart order entry** (Orders → Smart entry) reads a pasted WhatsApp message or email, a PDF purchase order or a
photo into a draft order. Orders can be in English, Tamil script, Tanglish or a mix. The user chooses the customer
first; one Claude call with structured output then returns the lines, and the server checks everything: product
ids must exist, prices always come from the price list (a price the customer quoted is only shown as a
difference), and if the order's GSTIN, phone or name points to a different customer the review screen says so
and offers to switch. Nothing is saved until the user reviews the draft and confirms. When the user picks a different product for some wording, that
wording is saved as an alias and offered to Claude next time.

**Ask your data** (Insights → Ask your data) answers questions such as "top 10 customers this financial year" or
"who owes us the most" from the reporting views. Claude writes SQL and calls a `run_sql_query` tool; it answers by
pointing at the result it wants shown, so the table, chart and Excel download are the database's own rows, never
numbers retyped by the model. Progress streams to the browser as server-sent events, and the SQL is shown with every
answer. The safety layers, each tested on its own:

1. Claude is only told about the `rpt` reporting views (managers also see `rpt_mgr.Margins`), which leave out
   contact details and cost prices.
2. `SqlGuard` parses the SQL with Microsoft's T-SQL parser and accepts exactly one `SELECT` over those views, with
   built-in functions from an allow-list. Other tables, databases, the `sys` catalog, variables, hints, `INTO`,
   `OPENROWSET` and every non-`SELECT` statement are refused, and the reason goes back to Claude.
3. Queries run as `ai_reader` / `ai_reader_mgr`, database users with no login that can read only the reporting views
   (`EXECUTE AS USER ... WITH NO REVERT` on a dedicated unpooled connection). Tests prove writes, `dbo` tables,
   `REVERT` and `EXECUTE AS LOGIN` fail at the database even when the checker is bypassed.
4. Snapshot isolation (reads never block order entry), a 15-second timeout, a 500-row cap and at most two queries at
   a time. Claude sees at most 50 rows.
5. Every query attempt is logged in `AiQueryLogs` with the SQL, outcome and row count.

Known limits: through SQL Server's built-in guest access the reporting users can see server metadata such as
database names (no business data), and a database user can always change its own default schema; the checker
refuses both kinds of statement, so the AI path can't issue them.

What is sent to Anthropic: the order text or file, today's date, and the product catalog (SKU, name, unit,
category, description, learned aliases). Not sent: cost prices, stock levels, the customer list or any other
customer data.

**Cost.** The instructions and catalog are a cached prompt prefix, so repeat calls within 5 minutes pay about 5%
for that part. Measured with the default model (Claude Opus 5.5 at medium effort): a WhatsApp-style text order
about ₹2.3, a one-page PDF purchase order about ₹3.5, a phone photo about ₹2.7 when the cache is warm; each read
takes 8–14 seconds. Every call is logged in the `AiUsageLogs` table with tokens,
cache use, estimated cost and outcome. Each user has a daily budget (`Ai:DailyBudgetUsdPerUser`, default
$2 per rolling 24 hours) and a per-minute request limit (`Ai:RequestsPerMinutePerUser`, default 10). The
estimates use the price list in `Ai:Pricing`; Anthropic's console has the actual bill.

## Troubleshooting

**`Cannot create file '...\SalesAppDb.mdf' because it already exists`** when the API starts: LocalDB has
lost track of the database although its files are still there, so the API tries to create it again. Re-attach
the existing files (nothing is lost), then start the API again:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "CREATE DATABASE [SalesAppDb] ON (FILENAME='$env:USERPROFILE\SalesAppDb.mdf'), (FILENAME='$env:USERPROFILE\SalesAppDb_log.ldf') FOR ATTACH"
```

**Port 5080 or 5173 already in use**: another copy of the API or the Vite dev server is still running. Close
that terminal, or stop the process listening on the port.

## Project structure

```
backend/
  src/SalesApp.Api/              Controllers, auth, error handling, OpenAPI
  src/SalesApp.Core/             Entities, DTOs, business rules (no EF, no ASP.NET)
  src/SalesApp.Infrastructure/   EF Core DbContext, migrations, seeding, reports, demo data
  src/SalesApp.AI/               Claude gateway (budget, caching, usage log) and the AI features
                                 Sql/*.sql: reporting views (rpt, rpt_mgr) and the AI read-only users, applied after migrations
  tests/SalesApp.Tests/          Unit tests
  tests/SalesApp.IntegrationTests/  API tests against a throwaway LocalDB database
frontend/
  src/features/                  Feature folders (auth, customers, products, inventory, sales, dashboard, reports, ...)
  src/components/                App shell and shadcn/ui components
  src/lib/api/                   Typed API client generated from OpenAPI
scripts/setup-dev.ps1            One-time developer setup
```

## Conventions

- **One error shape** for every API failure: `{ message, traceId, errors? }`. `errors` is keyed by camelCase field name, so forms can show messages next to the right input.
- **UTC timestamps** everywhere (`*Utc` columns, `TimeProvider` for testability).
- **`CancellationToken`** passed through every async layer.
- **Money is `decimal(18,2)`**; quantities are `decimal(18,3)` (kg to the gram, metres to the millimetre).
- **Optimistic concurrency**: editable records carry a `rowVersion`; saving a stale copy returns 409 and the UI offers a reload.
- **List endpoints** share `q`, `page`, `pageSize` (max 100), `sort` (`name` or `-name`) and `includeInactive`; list state lives in the page URL.

## Reference data

- **States**: every GST state code, seeded by migration (union territories without a legislature are flagged for UTGST).
- **Demo company**: *Demo Textiles Private Limited*, GSTIN `33ABCDE1234F1Z7` (built from the standard example PAN). Replace it in **Company settings**.
- **Starter HSN codes and GST rates are illustrative.** Confirm current rates with your tax advisor and edit them in **Categories & HSN**. Invoices copy the rate when they're issued, so later changes don't alter past invoices.
- **GSTINs are validated** for format and the GSTN check character, both in the browser and on the server, and must match the billing state.

# SalesApp

A sales management application for an Indian trading business: customers, products, quotations and
orders, GST invoices, payments, returns and credit notes, purchases from suppliers, stock, and reports, with Claude-powered AI
features built in.

This is a ground-up rebuild of an ASP.NET MVC 5 application I wrote early in my career. The original
source was lost, so the app has been rebuilt on a modern stack, with AI designed in from the start.

## Status

| Phase | Scope | State |
|---|---|---|
| P0 | Foundation: solution layout, sign-in (JWT + roles), React app shell, tests | ✅ Done |
| P1 | Customers, products, HSN/GST rates, users, company settings | ✅ Done |
| P2 | Inventory: stock ledger, receipts, adjustments, low-stock list | ✅ Done |
| P3 | Orders and quotations, GST invoicing (CGST/SGST/IGST), payments | ✅ Done |
| P4 | Demo data generator, dashboard, reports, Excel export | ✅ Done |
| P5 | AI: smart order entry from email/WhatsApp text, PDF or photo | ✅ Done |
| P6 | AI: ask your sales data in plain English (read-only, sandboxed SQL) | ✅ Done |
| P7 | AI: assistant that drafts follow-ups, quotations and payment reminders | ✅ Done |
| P8 | AI: insights, anomaly detection and demand forecasting | ✅ Done |
| P9 | Hardening, AI usage dashboard, deployment guide, live evals | ✅ Done |

## Tech stack

| Layer | Choice |
|---|---|
| Frontend | React 19, TypeScript, Vite, React Router, TanStack Query, Tailwind CSS + shadcn/ui, React Hook Form + Zod, Motion (animation), three.js + React Three Fiber (3D) |
| API | ASP.NET Core 10 Web API, built-in OpenAPI + Scalar, JWT bearer auth |
| Data | EF Core 10 on SQL Server (LocalDB in development) |
| Auth | ASP.NET Core Identity (users, roles, password hashing, lockout) + JWT. Roles are editable permission sets, with per-user changes |
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

A fresh database is empty. To see the dashboard, reports and the AI features with something to work on, generate two years of synthetic trading history for a Tamil Nadu textile and yarn distributor:

```powershell
# Stop the API first. --reset replaces existing customers, products, orders, invoices and stock;
# users, company settings and GST reference data are kept.
dotnet run --project backend/src/SalesApp.Api -- --seed-demo --months 24 --reset
```

It runs in under 10 seconds and is repeatable (fixed random seed). The history has a festive October to
November peak, year-on-year growth, customers who stop buying, slow payers, a few stockouts and a bulk
order, and a current pipeline of open orders and products due for reorder. The buying side matches it: suppliers
(one in Gujarat, so its bills carry IGST), purchase orders delivered in parts and sometimes late, supplier bills (a
couple waiting for approval of a price difference) and payments, with one supplier paid slowly. Returns run through
both: credit notes for goods sent back and price corrections (one fabric comes back far more often than the rest),
credit set against later invoices or refunded, one customer still holding credit today, goods returned to suppliers
and deliveries partly rejected at the door. It also creates a demo team
to sign in as, each with the password `Seed:DemoUserPassword` from `appsettings.Development.json`:

| User | Role |
|---|---|
| `meena.krishnan@salesapp.local` | Sales manager (sees margins and stock value) |
| `arun.kumar@salesapp.local`, `divya.rajan@salesapp.local`, `farhan.sheikh@salesapp.local` | Sales representatives |

All names, GSTINs and figures are made up. The command only runs in the Development environment.

## Common tasks

| Task | Command |
|---|---|
| Backend tests | `dotnet test --solution backend/SalesApp.slnx` (the live AI evals skip unless enabled) |
| Tests against another SQL Server (with a SQL login) | set `SALESAPP_TEST_SERVER`, `SALESAPP_TEST_SQL_USER` and `SALESAPP_TEST_SQL_PASSWORD`, then run the backend tests |
| Live AI evals: calls Claude, about ₹50, stops at $3 | `$env:SALESAPP_RUN_EVALS = 'yes'; dotnet test --project backend/tests/SalesApp.Evals -c Release` |
| Frontend tests | `npm test --prefix frontend` |
| Frontend lint / typecheck | `npm run lint --prefix frontend` · `npm run typecheck --prefix frontend` |
| Regenerate TypeScript API types (API must be running) | `npm run gen:api --prefix frontend` |
| Add an EF Core migration | `dotnet ef migrations add <Name> --project backend/src/SalesApp.Infrastructure --startup-project backend/src/SalesApp.Api --output-dir Data/Migrations` |
| Production build of the UI (output goes to the API's `wwwroot`) | `npm run build --prefix frontend` |
| Publish for IIS (after the UI build) | `dotnet publish backend/src/SalesApp.Api -c Release -o publish`, then see [docs/DEPLOYMENT-IIS.md](docs/DEPLOYMENT-IIS.md) |
| Regenerate demo data (API stopped) | `dotnet run --project backend/src/SalesApp.Api -- --seed-demo --months 24 --reset` |
| Print the insight figures for the current data (no AI, nothing saved) | `dotnet run --project backend/src/SalesApp.Api -- --insights-metrics` |
| Point the dev web app at another API (a second one on a test database) | `$env:SALESAPP_API_URL = 'http://localhost:5099'; npm run dev --prefix frontend` |

## Users, roles and permissions

Administration → **Users & roles** decides who can do what.

- **Permissions** follow the menus: each has **View**, and **Add & change** where the menu changes data (orders,
  invoices, payments, credit notes, customers, products, categories, stock, suppliers, purchase orders, supplier
  bills, supplier payments, supplier returns, users). A few special rights sit beside them: see cost prices and margins, set credit terms, override
  credit limits, can be a customer's sales rep, approve purchase orders above the limit, approve bill differences,
  and read supplier bills with AI.
- **Roles** are sets of permissions. Three are built in: **Administrator** (everything, and it can't be changed),
  **Sales manager** (everything except administration, rate updates and the two purchase approvals) and **Sales representative** (orders, customers, smart order
  entry, viewing invoices, payments, credit notes, products and stock, reports, Ask your data and the assistant; no
  cost prices or credit terms). Their permissions can be changed; add your own roles too, such as an accountant or a storekeeper.
- **Each user** has one role and can have permissions added or taken away just for them; the user list shows this as
  `+1 −1` beside the role.
- Granting a permission grants what it needs (Add & change needs View; smart order entry needs Orders: Add & change;
  recording a payment needs Invoices: View, because it's done from the invoice; so does issuing a credit note;
  returning goods to a supplier needs Supplier bills: View; receiving goods needs Purchase orders: View), and taking
  one away takes away what depends on it.
- **Changes apply on the user's next request**, without signing in again. The API checks the permission on every
  request; the web app only hides menus and buttons the user can't use. A deactivated user's session stops working
  at once.
- **The AI features follow them too.** The assistant's lookups need the same permission as the screen (customers,
  invoices, orders and quotations, products; its SQL needs Ask your data), and only users who may see cost prices
  get margins from Ask your data and the assistant.
- **Guard rails:** you can't change your own role or permissions, there is always an active administrator, you can't
  change your own role so that you lose Users & roles, and a role can't be deleted while anyone has it.

## Rate updates

Masters → **Rate updates** (administrators by default; anyone given the Rate updates permission) sets each product's
selling rate for a period, such as a month's rate list from 1 to 31 January.

- **Which price an order gets:** the rate whose period covers the order's date, or the product's list price when no
  period does. The order form fills in that price when a product is picked (and it can still be changed on the line),
  and smart order entry, the assistant and Ask your data (`rpt.Products.CurrentPrice`) use the same rule.
- **Saved orders and invoices keep their prices.** A line's price is copied when the order is saved, so changing or
  removing a rate later only affects orders priced afterwards.
- **Setting a month's rates:** choose the period (This month and Next month fill in the dates; leave the last day
  empty for "until replaced"), then type new rates, or change all shown products by a percentage rounded to whole
  rupees or to the paisa. Or download the rate sheet for Excel, fill in its "New rate" column and upload it; the
  rows come back to the screen to review before anything is saved.
- **Periods never overlap.** Saving exactly the same period again changes its rates; any other overlap is refused,
  naming the product and the period in the way. Rate history lists every period with who set it and when; periods
  can be edited or removed there. Each product's page shows its periods and today's price.

## Purchases

The **Purchases** menu covers buying, from the order to the supplier's payment: purchase order → goods received →
supplier bill → payment. Suppliers are under Masters.

- **Purchase orders.** A draft can be edited; a line left without a price gets the last price paid to that supplier
  for the product, else its cost price. Placing an order above the approval limit in Company settings (default
  ₹5,00,000) sends it for approval, unless the person placing it may approve purchase orders; an approver can approve
  it or send it back with a reason. Orders print for the supplier with the terms from Company settings.
- **Reordering.** Stock → **Reorder low stock**, and **Draft purchase orders** on AI insights' reorder suggestions,
  draft one order per preferred supplier for the products chosen, with suggested quantities. Stock already on
  unfinished orders counts, so a product those orders cover isn't suggested again.
- **Receiving goods.** Every receipt is against a purchase order, so stock can't arrive unplanned. Deliveries can come
  in parts, and up to 5% more than ordered is accepted (Company settings). Receiving posts the goods to stock at the
  order's price and updates each product's cost by **weighted average**: 100 kg at ₹200 plus 50 kg at ₹230 gives
  ₹210. An order can be closed to stop further deliveries, or cancelled if nothing has arrived.
- **Supplier bills and the three-way check.** A bill is entered against its order, by hand or by reading the
  supplier's PDF or photo with AI (see [AI features](#ai-features)). It is checked against the order and what was
  received: no more billed than received, prices within 2% of the order (Company settings), the right GST rate, and
  lines that add up to the total printed on the invoice (₹1 either way is rounding). A bill that passes can be paid;
  one that doesn't shows each difference in a sentence and waits until someone who may approve bill differences
  approves it with a note. GST is CGST + SGST from a supplier in our state and IGST from one in another. The same
  supplier invoice number can't be entered twice.
- **Freight and price differences go into stock cost.** Freight and other charges on a bill are shared across its
  lines by value, and a price on the bill that differs from the order's adds the difference. Both apply to the part
  of the delivery still in stock; the part already sold went at the old cost. Cancelling the bill reverses it.
- **Paying suppliers.** Payments are recorded from the bill, in parts if needed, and can't exceed what's due. Cash of
  more than ₹10,000 to one supplier in a day is refused (Income-tax Act s.40A(3)). TDS isn't handled yet.
- **Reports.** Purchase register (bills with taxable value and input GST), Payables ageing (what we owe each
  supplier, by how overdue) and GST payable (output GST on sales less input credit on bills, by month), for anyone
  who may view supplier bills.
- **Earlier goods receipts.** Receipts from before purchase orders existed keep their lines, and the upgrade creates
  a supplier for each supplier name they used, marked "Address not recorded"; add the address and GSTIN in Suppliers.

## Returns and credit notes

**Credit notes** (Sales → Credit notes) correct an invoice after it's issued, as CGST Act s.34 provides; **supplier
returns** (Purchases → Supplier returns) send goods back against a supplier's bill as our debit note.

- **From the invoice.** Open the invoice and choose **Credit note**: either goods came back (enter the quantity per
  line, and whether each line goes back into stock or was damaged), or no goods move and the value is corrected (a
  wrong rate, short supply, a discount agreed later: enter the amount off each line, before GST). The screen shows the
  credit note exactly as the server works it out, before anything is saved. Credit notes have their own gapless
  series, CN/26-27/00001, and print like the invoice they correct.
- **Never more than was invoiced.** Each line can give back at most what's left on it after earlier credit notes,
  and the last piece takes exactly what's left, so a line returned in several goes adds up to the paisa. After a price
  correction, each returned unit is credited at the lower value.
- **Stock.** Goods fit to sell go back into stock at the product's cost; damaged ones are recorded as returned and
  written off the same day, so the stock ledger shows both the return and the loss.
- **Money.** The credit first comes off what's still due on that invoice. Anything left over (the invoice was
  already paid) stays as credit on the customer's account: set it against another of their invoices (**Use credit**
  on the invoice, or **Set against an invoice** on the credit note) or record a refund. Receivables ageing shows each
  customer's unused credit, and the credit limit check counts it in their favour.
- **The GST time limit.** A credit note can reduce GST only until 30 November after the end of the invoice's
  financial year (the annual return date can come earlier; it isn't tracked). After that it credits the value only,
  marked as such, and the GST reports leave it out.
- **Cancelling.** An invoice can be cancelled only while it's unpaid, uncredited and still in its own month; after
  that it has gone into that month's GSTR-1, so a credit note is the way to correct it. A credit note can be
  cancelled in its own month if its credit hasn't been used on another invoice or refunded; returned goods leave
  stock again and the invoice owes its full amount again.
- **Supplier returns.** On a supplier bill choose **Return goods**: the goods leave stock at the bill's price (what
  stays keeps its true average cost), the input GST on them is reversed, and the value comes off what we owe on the
  bill. Anything beyond that is credit with the supplier, for their next bill or a refund to us. Debit notes have
  their own series, DN/26-27/00001, and print for the supplier; note the supplier's own credit note number when it
  arrives (the list shows which are still awaited). The same time limit and cancelling rules apply.
- **Rejected on delivery.** In **Receive goods**, part of a delivery can be refused at the door with a reason: it
  never enters stock, the order still expects it, and the goods receipt shows what was rejected and why.
- **Reports.** Sales figures are net of credit notes everywhere (dashboard, reports, forecasts, Ask your data), each
  counted in the month it was issued; the GST summary shows credit notes apart from invoices, and GST payable takes
  supplier returns off the input credit. New registers: Credit notes and Supplier returns.

## Look and feel: layouts, 3D, motion and photos

The app has a calm indigo-and-teal look in light and dark, with depth and motion on every screen.

- **Three layouts, each user's choice.** **Layout** in the account menu (the avatar, top right): an *icon rail* on
  the left that slides open with labels as you point at it, with the page moving over to make room (the default), *top menus* with large drop-down panels
  describing each page, or a *dock* at the bottom whose icons grow under the pointer and pop up their pages.
  Switching animates from one to the other and is remembered per user in that browser. Phones always get a menu that
  slides in from the left.
- **Search everything: Ctrl+K** (⌘K on a Mac), or the search box in the top bar. Jump to any page, start a new order,
  customer, product or purchase order, or find a customer, invoice, order, product or supplier by name or number
  (also by GSTIN or phone where the list supports it); also switches layout and light/dark.
- **Records open beside their list.** Clicking an invoice, order, customer, product, supplier, purchase order,
  supplier bill, credit note, supplier return or goods receipt grows a panel out of the row, with the list still
  behind it (that row stays marked, and the list keeps its search, filters and page). **Full width** widens the
  panel; Escape, the cross, Back or clicking the list closes it. The address is the record's own
  (/invoices/42), so it can be bookmarked or shared: the list opens behind it. The arrows in the panel's top bar
  (or J and K) step to the next and previous record in the list without closing it, and the record's title,
  status, actions and key figures (total, balance due, credit left) stay pinned at the top as it scrolls. Where the
  panel is too narrow for a line-items table, each line becomes a card with labelled values.
- **Keyboard shortcuts.** ? shows them all. / jumps to the list's search; J and K move along a list and Enter opens
  the row; N starts a new order, customer, product, supplier or purchase order on its list; G then a letter jumps to
  a page (G D dashboard, G O orders, G I invoices, G C customers, G P products, G S stock, G B supplier bills, G R
  reports). Letter keys never fire while you're typing in a field.
- **When something goes wrong.** A page that fails shows a friendly screen with Try again (inside the app, so the
  menus stay; a failing record shows it in the panel) and the technical details to copy for support; after an
  update it offers to reload. A notice under the top bar says when you're offline or the server can't be reached,
  checks again every few seconds, and reloads the page's data once it's back.
- **Light and dark.** The sun/moon button in the top bar switches between them; **Appearance** in the account menu
  also offers *Same as Windows* (the default). Printed documents are always light.
- **Motion, and how much.** **Motion** in the account menu: *Full* (3D scenes, tilting cards, pages that swing into
  place), *Subtle* (quick fades only, no 3D) or *Off*. Until someone picks, the app follows Windows' "Animation
  effects" setting, so anyone who has turned animations off gets none.
- **3D scenes**, drawn in code with three.js (no model or picture files):
  - the dashboard's hero: the last 24 months of sales as the buildings of a small city (last year's street behind
    this year's), lit windows at night, cars on the road between; drag to turn it, point at a building for its
    month;
  - behind every page: a softly billowing woven cloth with a few bright threads running through it;
  - the sign-in page: yarn cones, a roll of cloth and folded linen floating in soft light, leaning towards the pointer;
  - each product page: its photo on a card that tilts with the pointer, or a 3D model of its kind until it has one;
  - Ask your data, smart order entry and AI insights: a rippling ball that speeds up while Claude is working.
  The 3D library (about 250 KB) downloads after the page has loaded, once, and a scene stops drawing when it's out
  of view; the backdrop draws at 25 frames a second. Browsers without 3D support, and the Subtle and Off settings,
  get still versions.
- **Success moments.** Saved messages draw a tick; a **PAID** stamp thuds onto settled invoices and supplier bills,
  with a burst of confetti when your payment is the one that settles it; and the dashboard celebrates (once) when
  this month's sales already beat every earlier month.
- **Accessible.** Checked against WCAG 2.1 AA with axe-core in light and dark and in all three layouts (an automated
  test in `src/test/a11y.test.tsx` keeps the shared parts honest): text contrast of at least 4.5:1, every list row
  and control reachable by keyboard, a "Skip to the page" link, labelled icon buttons, and list filters that screen
  readers announce as a choice of options.
- **Everywhere else:** cards rise into place one after another, and further down a page they rise as you scroll to
  them; page titles drift as you scroll; table rows cascade in; a soft light follows the pointer across cards and
  main buttons lean towards it; summary and report tiles tilt; numbers count up; dialogs swing forward; empty lists
  show an animated drawing; and a thin bar along the top shows when something is loading.
- **Product photos.** On a product's page, drop a picture on the photo area or choose **Add photo** (JPEG, PNG or
  WebP; needs Products: Add & change). The browser shrinks it before upload, to at most 1200 px plus a 400 px square
  thumbnail, so it uploads in a moment. Photos then show in the product list (and its **Gallery** view), the
  product picker on orders and purchase orders, invoice lines and the stock screens. They're stored in the database
  (one backup covers everything) and changes appear in the product's history. Pictures load from signed addresses,
  because an `<img>` tag can't send the sign-in token: only the server can make one, and each new photo gets a new
  address, so browsers cache photos for good.

## Audit trail

Administration → **Audit log** (administrators by default; anyone given the Audit log permission) shows who did what
and when.

- **Every change, field by field.** Creating, changing or removing a customer (including its addresses), product,
  rate, category, HSN code, order (including its lines), invoice, payment, supplier, purchase order, goods receipt,
  supplier bill, supplier payment, credit note, supplier return, credit used or refunded, stock adjustment, user, role
  or the company settings is recorded with each field's old and new value, written as people read them
  ("Credit limit: ₹26,00,000 → ₹30,00,000", "Sales rep: Arun Kumar → Divya Rajan").
- **Security events:** sign-ins, failed sign-ins (under the email that was tried, with the reason), lockouts,
  password resets, changes to a user's role or permissions and to a role's permissions, and Excel downloads.
- **Who and from where:** the user's name at the time and the address the request came from. Background work is
  recorded as "System".
- **History on records:** customers, products, orders and invoices have a History panel listing their own changes.
- **It can be trusted:** a change and its entry are saved in the same transaction, and a database trigger refuses
  any update or delete of an entry. Generating demo data isn't recorded, so it doesn't bury real activity.

## Deployment

One IIS site serves the API and the web app: the UI build goes into the API's `wwwroot`, and any path that isn't
an API call returns the web app. [docs/DEPLOYMENT-IIS.md](docs/DEPLOYMENT-IIS.md) walks through building, the
database, the IIS site, secrets as application-pool environment variables, the first start and updates.

Security in short:
- **Secrets** come from user-secrets (development) or environment variables, never from committed files.
- **Sign-in:** JWT bearer tokens (8 hours), account lockout, and a per-address limit on sign-in attempts
  (`Security:LoginAttemptsPerMinute`, default 10).
- **Access:** permissions per menu and action, checked by the API on every request and read from the database rather
  than the token, so a change or a deactivation applies at once (see [Users, roles and permissions](#users-roles-and-permissions)).
- **Browser:** security headers on every response, and outside development a content security policy that allows
  only this site's own scripts, styles and API.
- **AI:** see [AI features](#ai-features). In short: read-only database users and a SQL checker for AI-written
  queries, recipients never chosen by the model, no images or outside links in replies, and per-user daily budgets.

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

**Supplier bill reading** (Purchases → Supplier bills → New bill, then **Read the bill**) fills in a bill from the
supplier's PDF or photo. One Claude call with structured output reads it against the lines of the purchase order it's
for, then the server checks the result: order lines must be on that order, the GSTIN should be the supplier's, the
invoice mustn't be entered already, and the lines must add up to the printed total. Problems show as warnings on the
form. Nothing is saved until the clerk reviews the form and saves it, and the three-way check then runs as for any
bill.

**Ask your data** (Insights → Ask your data) answers questions such as "top 10 customers this financial year" or
"who owes us the most" from the reporting views, where sales are net of credit notes and balances net of credit
(`rpt.CreditNotes` lists credit notes with the credit left on each). Claude writes SQL and calls a `run_sql_query` tool; it answers by
pointing at the result it wants shown, so the table, chart and Excel download are the database's own rows, never
numbers retyped by the model. Progress streams to the browser as server-sent events, and the SQL is shown with every
answer. A question keeps running if the user moves to another screen; a notification says when the answer is ready.
The safety layers, each tested on its own:

1. Claude is only told about the `rpt` reporting views, which leave out contact details and cost prices. Users who
   may see cost prices also get `rpt_mgr.Margins` and the purchasing views (suppliers, purchase order lines,
   supplier bills and their lines, supplier payments), so they can ask what was bought, from whom, and at what price.
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

**Assistant** (the Assistant button in the top bar, on every screen) is a chat that looks things up and drafts
messages. It has read-only tools: find customers, a customer's full picture (sales this and last year, outstanding
and overdue, recent orders, top products), outstanding or overdue invoices, any invoice, order or quotation by
number, product and stock search, and the same guarded SQL query as Ask your data for totals and trends. Users who may
view suppliers can also find suppliers and see a supplier's open purchase orders and unpaid bills, and look up a
purchase order or supplier bill by number; orders and bills each need their own View permission. It knows credit
notes and supplier returns too: a customer's sales are net of returns with their unused credit, and any credit note
or debit note can be looked up by number (each needs its own View permission). Replies
stream in as they're written; Stop ends the reply (and Anthropic's billing for it), and closing the panel doesn't.

The two overlap on purpose but do different jobs. Ask your data is for figures: its table and chart come straight
from the database rows and export to Excel. The assistant is for tasks about a customer or a document, and it
writes figures into its replies itself. When an assistant reply only looked up figures, it offers "Open in Ask your
data", which fills the same question in there (without asking it, since asking costs money).

- **Drafts, never sending.** Asked for a payment reminder, follow-up or quotation note, it writes an email or
  WhatsApp draft that the user can edit, copy, or open in their own email or WhatsApp. SalesApp sends nothing. It
  can also write to a supplier, such as chasing a late delivery or querying a bill. The recipient comes from the
  customer or supplier record, never from the model.
- **Drafts are checked.** Every invoice, order or quotation number in a draft must exist and belong to that
  customer, and every exact rupee amount must appear in data the assistant looked up in the conversation; anything
  else shows as a "Check before sending" warning on the draft.
- **Saved conversations.** Conversations are kept per user (only their owner can open them, administrators
  included) and reopen after a reload. Messages are stored exactly as sent to and received from Claude and are
  append-only, enforced by a database trigger. Each conversation keeps the system prompt it started with, and
  today's date goes into each message instead, because Claude's saved reasoning and the prompt cache both rely on
  the start of the conversation never changing.
- **Limits.** Up to 8 rounds of lookups per message (`Ai:Assistant:MaxToolRounds`). A conversation stops taking
  messages at about 100,000 tokens (`Ai:Assistant:MaxContextTokens`), because every turn re-sends the history; the
  user starts a new one.
- **Replies are shown safely.** They are formatted text with no raw HTML. Images are never loaded (an image link is
  a known way to leak data to another server), and only links to pages inside SalesApp are clickable.

**AI insights** (Insights → AI insights; Sales manager and Administrator by default) puts the numbers first. Everything is computed in C# from
the sales data, so the page works fully with AI switched off; Claude only writes the summary on top.

- **Forecast.** Monthly sales for this month and the next two, with a likely range. Each method forecasts the last
  six months from the months before them, and the one with the smallest average error (MAPE) is used, so the
  choice can be checked. The baseline is "same month last year, adjusted for growth"; Holt-Winters (seasonal
  exponential smoothing with a damped trend) joins once there are two full years of history before those six months,
  because with less it overshoots. On the demo's two years the baseline is used, with an error of about 18%.
- **Reorder suggestions.** Reorder point = expected demand over the supplier's lead time + safety stock
  (1.65 × daily spread × √lead time, about a 95% chance of not running out). Demand counts only the days the
  product was in stock, so a stock-out doesn't look like a lack of demand, and allows for the season ahead.
- **Worth a look.** Regular customers who have stopped buying (far longer than their usual gap between orders);
  invoices far larger than the customer's usual ones; products that sold far more or far less than usual for the
  time of year; stock-outs of a week or more found in the stock ledger, with an estimate of the sales missed;
  customers with ₹10,000 or more over 60 days overdue; and products or customers with far more goods coming back
  than usual (at least 5% returned, three times the usual rate, on several credit notes in the last 12 months).
- **A summary you can trust.** Claude writes a headline, a short summary and a few cards from the computed figures.
  Every figure in them is checked against those figures, allowing for normal rounding (₹1,87,17,602 written as
  ₹1.87 crore). If any figure doesn't match, Claude gets one retry with the list of figures that didn't. Anything
  that still doesn't match is left out, and the page says so.
- **When it runs.** Every morning after 06:30 India time (`Ai:Insights:NightlyAt`). A server that was off at that
  time catches up when it starts. Managers can also press **Refresh now**. Each run is saved with the exact figures
  Claude was given (`InsightRuns`).

**AI usage** (Administration → AI usage; administrators by default, or anyone given the AI usage permission) shows
what the AI costs: spend per day, per feature and per person (with each person's last 24 hours against their daily
budget, and the morning insights as "background"), the share of input read from the prompt cache, every call to
Claude with its outcome, and every SQL query the AI wrote with what the checker or the database said about it.
Conversations stay private.

**Evaluations.** `backend/tests/SalesApp.Evals` measures the AI features against Claude itself, on a throwaway
database with the standard demo data:
- 25 Ask-your-data questions, each graded against a reference query by the values in the answer, not the SQL text.
  A question with a one-row answer ("how much did we receive last month?") may be answered in more detail, such as
  a breakdown by payment mode with the total. That passes when the table holds the right row and the written summary
  states its figure.
- 12 orders to read: an English email, WhatsApp messages in English shorthand, Tanglish, Hinglish and Tamil script,
  tonnes and boxes, a quoted price, an unknown product, a message that isn't an order, a prompt injection, the PDF
  purchase order and the photographed Tamil order.

They cost money (about ₹50 for both suites, a few minutes), so they only run when you set `SALESAPP_RUN_EVALS=yes`,
and the run stops if it reaches $3. Each suite writes a report to `backend/tests/SalesApp.Evals/results/` (not
committed): pass or fail per case, why, and what it cost; for a failed question also the AI's SQL and summary. To
re-check a few cases only, list their ids: `$env:SALESAPP_EVAL_CASES = 'top-debtor,payments-last-month'`. The
graders, and a check that every reference query still runs, are ordinary tests that run every time at no cost.

Results of the first run (15 Oct 2026, `claude-opus-5-5`):

| Suite | Passed | Cost |
|---|---|---|
| Order entry | 12 of 12 | ₹12 |
| Ask your data | 25 of 25 | ₹38, plus ₹6 to re-check three |

The three re-checked questions had failed under the first grader: "who owes us the most?", "how much did we
receive last month?" and "which month had the highest sales?". Claude answered each with a fuller table: the top 10
debtors, payments by mode with the total, and all 12 months. Each table held the reference answer and its summary
named it, so the grader now accepts this kind of answer.

What is sent to Anthropic:
- Smart order entry: the order text or file, today's date, and the product catalog (SKU, name, unit, category,
  description, learned aliases). Not sent: cost prices, stock levels, the customer list or any other customer data.
- Supplier bill reading: the bill file, today's date, the supplier's name, and the purchase order's lines (SKU,
  product, unit, HSN code, quantities ordered, received and billed, the order price and GST rate). Not sent: the
  supplier's contact details or anything about other suppliers.
- Ask your data: the question, today's date, the view descriptions and the rows of the queries Claude runs (at most
  50 per query). Those rows can include customer names, cities and amounts, and for users who may see cost prices
  the cost prices in `rpt_mgr.Margins`; the views hold no phone numbers, emails, addresses or GSTINs.
- Assistant: the conversation and the results of its lookups: customer names, codes, cities, GSTINs, contact
  person, credit terms, notes, invoices, orders, products and stock; for suppliers the same kinds of details with
  their purchase orders and bills; plus query rows as for Ask your data. Phone numbers and email addresses of
  customers and suppliers are never sent; the draft card shows them straight from the record.
- AI insights: the computed figures only: sales totals, the forecast, customer names and cities with their sales,
  product codes and stock, overdue amounts and the anomaly descriptions. No contact details.

**Cost.** The instructions and catalog are a cached prompt prefix, so repeat calls within 5 minutes pay about 5%
for that part. Measured with the default model (Claude Opus 5.5 at medium effort): a WhatsApp-style text order
about ₹2.3, a one-page PDF purchase order about ₹3.5, a phone photo about ₹2.7 when the cache is warm; each read
takes 8–14 seconds. An Ask-your-data question that needs one query is about ₹3 (two Claude calls) and 5–10 seconds;
questions that need several queries cost more, up to the limit of five queries per question. An assistant message
costs more the more lookups it makes, since each round of lookups is one more Claude call. Measured: "which
products are low on stock?" about ₹3.3 (two calls, 8 seconds); a payment reminder email for the biggest debtor about
₹6.7 (four calls, 20 seconds); a follow-up asking for a WhatsApp version about ₹5.6. That follow-up came half an
hour later, after the five-minute prompt cache had expired, so the whole conversation had to be cached again; a
quick follow-up costs less. The insights summary is one Claude call a day (two if the figure check needs a
retry): measured at about ₹6.6 for the morning run and ₹3.5 for a refresh within five minutes of it (the figures are
then read from the cache), about 20 seconds each; both first runs passed the figure check with no retry. Background runs are logged without a user and have their own daily budget. Every call is logged in the `AiUsageLogs` table with tokens,
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

**Integration tests fail with `Error occurred during LocalDB instance startup: SQL Server process failed to
start`**: LocalDB is already running, started from another Windows session (for example by the API in a
different terminal), and this session can't see it. Stop the API and run `sqllocaldb stop MSSQLLocalDB`, or point
the tests at the running instance's pipe name: `$env:SALESAPP_TEST_SERVER = 'np:\\.\pipe\LOCALDB#<id>\tsql\query'`.
List the open LocalDB pipes with `[System.IO.Directory]::GetFiles('\\.\pipe\') -match 'LOCALDB'`.

**`running scripts is disabled on this system`** when running `setup-dev.ps1` or `npm`: Windows PowerShell's
execution policy blocks scripts. Run the setup once with
`powershell -ExecutionPolicy Bypass -File scripts/setup-dev.ps1`, and use `npm.cmd` instead of `npm` (for example
`npm.cmd run dev --prefix frontend`), or run the commands from a Command Prompt.

## Project structure

```
backend/
  src/SalesApp.Api/              Controllers, auth, error handling, OpenAPI
  src/SalesApp.Core/             Entities, DTOs, business rules (no EF, no ASP.NET)
  src/SalesApp.Infrastructure/   EF Core DbContext, migrations, seeding, reports, demo data, insight metrics
                                 Sql/*.sql: reporting views (rpt, rpt_mgr), the AI read-only users and the assistant's
                                 append-only rule, applied after migrations
  src/SalesApp.AI/               Claude gateway (budget, caching, usage log) and the AI features
  tests/SalesApp.Tests/          Unit tests
  tests/SalesApp.IntegrationTests/  API tests against a throwaway LocalDB database
  tests/SalesApp.Evals/          Live AI evaluations (opt-in, cost money) and their graders
frontend/
  src/features/                  Feature folders (auth, customers, products, inventory, sales, dashboard, reports, ...)
  src/components/                App shell and shadcn/ui components
  src/components/layout/         The shell: the three layouts, top bar, Ctrl+K search and the record side panel
  src/components/fx/             Theme, motion and layout preferences, page transitions, tilt, count-up and other effects
  src/components/three/          The 3D scenes (loaded on demand)
  src/lib/api/                   Typed API client generated from OpenAPI
scripts/setup-dev.ps1            One-time developer setup
docs/DEPLOYMENT-IIS.md           Publishing to IIS
```

## Conventions

- **One error shape** for every API failure: `{ message, traceId, errors? }`. `errors` is keyed by camelCase field name, so forms can show messages next to the right input.
- **UTC timestamps** everywhere (`*Utc` columns, `TimeProvider` for testability).
- **`CancellationToken`** passed through every async layer.
- **Money is `decimal(18,2)`**; quantities are `decimal(18,3)` (kg to the gram, metres to the millimetre).
- **Optimistic concurrency**: editable records carry a `rowVersion`; saving a stale copy returns 409 and the UI offers a reload.
- **List endpoints** share `q`, `page`, `pageSize` (max 100), `sort` (`name` or `-name`) and `includeInactive`; list state lives in the page URL.
- **Continuous integration**: GitHub Actions (`.github/workflows/ci.yml`) builds and runs every test on every push
  and pull request: the backend against a throwaway SQL Server container, and the web app's lint, type check, tests
  and production build. A pull request into `main` can only be merged once both pass.

## Reference data

- **States**: every GST state code, seeded by migration (union territories without a legislature are flagged for UTGST).
- **Demo company**: *Demo Textiles Private Limited*, GSTIN `33ABCDE1234F1Z7` (built from the standard example PAN). Replace it in **Company settings**.
- **Starter HSN codes and GST rates are illustrative.** Confirm current rates with your tax advisor and edit them in **Categories & HSN**. Invoices copy the rate when they're issued, so later changes don't alter past invoices.
- **GSTINs are validated** for format and the GSTN check character, both in the browser and on the server, and must match the billing state.

# Deploying SalesApp on IIS

One IIS site serves both the API and the web app: the React build is published into the API's `wwwroot`, and
any path that isn't an API call returns the web app. This guide was checked against a published build running in
the Production environment.

## What you need

| | |
|---|---|
| Server | Windows Server 2019 or later (or Windows 10/11 for a small office), with IIS |
| .NET | The **ASP.NET Core 10.0 Hosting Bundle** (installs the runtime and the IIS module). Restart IIS after installing it: `iisreset` |
| Database | SQL Server 2016 SP1 or later; Express is fine. Older versions (2008, 2012, 2014) won't work: the reporting views use `CREATE OR ALTER`, and EF Core 10 needs 2016 features |
| Certificate | An HTTPS certificate for the site's address. The app sends HSTS and redirects HTTP to HTTPS outside development |
| Build machine | The .NET 10 SDK and Node.js 22 or later (this can be your development PC) |
| AI (optional) | A Claude API key, and outbound HTTPS from the server to `api.anthropic.com` |

## 1. Build

On the build machine, from the repository root:

```powershell
npm ci --prefix frontend
npm run build --prefix frontend          # writes the web app into backend/src/SalesApp.Api/wwwroot
dotnet publish backend/src/SalesApp.Api -c Release -o publish
```

The `publish` folder is the whole deployment: the API, the built web app in `wwwroot`, and a `web.config` for IIS.
That `web.config` turns off IIS's dynamic compression for the site, because it would hold back the live progress of
Ask your data and the assistant. The web app's static files are still compressed.

## 2. Database

Create an empty database and give the site's application pool access to it. With Windows authentication and the
SQL Server on the same machine:

```sql
CREATE DATABASE SalesApp;
GO
CREATE LOGIN [IIS APPPOOL\SalesApp] FROM WINDOWS;
GO
USE SalesApp;
CREATE USER [IIS APPPOOL\SalesApp] FOR LOGIN [IIS APPPOOL\SalesApp];
ALTER ROLE db_owner ADD MEMBER [IIS APPPOOL\SalesApp];
```

`db_owner` is needed because the app applies its own migrations at startup and runs `Sql/*.sql` after them: the
reporting views, the two database users the AI queries run as (`ai_reader`, `ai_reader_mgr`, with no login), and
snapshot isolation so reports never block order entry. If SQL Server is on another machine, use a SQL login in the
connection string instead (`User ID=...;Password=...`), and keep that connection string out of files (step 4).

## 3. IIS site

1. Copy the `publish` folder to the server, for example to `C:\inetpub\salesapp`.
2. In IIS Manager, add an **application pool** named `SalesApp` with **.NET CLR version: No Managed Code**.
   In its Advanced Settings, set **Start Mode: AlwaysRunning** and **Idle Time-out: 0**, so the morning insights run
   on time. (Without this, IIS stops the app when nobody uses it, and the insights run at the first visit after
   06:30 instead.)
3. Add a **website** that uses that pool, with its physical path set to the folder from step 1 and an **https**
   binding with your certificate. In the site's Advanced Settings, set **Preload Enabled: True**.

## 4. Settings and secrets

Give the application pool these environment variables. Values set on the pool aren't stored in the site folder:

| Variable | Value |
|---|---|
| `ConnectionStrings__Default` | `Server=.\SQLEXPRESS;Database=SalesApp;Trusted_Connection=True;TrustServerCertificate=True` (adjust the server name) |
| `Jwt__SigningKey` | A random secret of at least 32 characters (command below). Changing it signs everyone out |
| `Database__MigrateOnStartup` | `true`: creates and updates the tables on startup |
| `Seed__AdminEmail` | The first administrator's email, e.g. `admin@yourcompany.in` |
| `Seed__AdminPassword` | Their first password. Only used when no administrator exists yet; remove it after the first start |
| `Anthropic__ApiKey` | Optional: your Claude API key. Without it the app runs normally and hides the AI features |

Generate a signing key (Windows PowerShell):

```powershell
$bytes = New-Object byte[] 48; [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes); [Convert]::ToBase64String($bytes)
```

Set a variable on the pool from an elevated command prompt (repeat for each):

```bat
%windir%\system32\inetsrv\appcmd.exe set config -section:system.applicationHost/applicationPools /+"[name='SalesApp'].environmentVariables.[name='Jwt__SigningKey',value='PASTE-THE-KEY']" /commit:apphost
```

Other settings can be overridden the same way, for example `Ai__DailyBudgetUsdPerUser`, `Ai__Insights__NightlyAt`
(`HH:mm`, India time), `Ai__Insights__NightlyEnabled` or `Security__LoginAttemptsPerMinute`. The names follow
`appsettings.json`, with `__` between levels.

## 5. Start and check

1. Start the site, then open `https://your-address/health`. It should say `Healthy`. The first start takes a little
   longer while it creates the tables.
2. Open the site and sign in with the administrator from step 4. Then remove `Seed__AdminPassword` from the pool.
3. In **Company settings**, enter your company's details, GSTIN, bank details and invoice prefixes. Then add your
   HSN codes, products and customers, and your staff in **Users**. (The demo data generator only runs in
   development, so it can never touch this database.)

If the site doesn't start, turn on the module's log: in `web.config` set `stdoutLogEnabled="true"`, create a
`logs` folder next to it that the application pool can write to, and look at the newest `logs\stdout_*.log`. Turn
it off again afterwards.

## Updating

1. Build and publish the new version (step 1).
2. Put a file named `app_offline.htm` in the site folder. IIS shows it and releases the app's files.
3. Copy the new `publish` folder over the old one (keep your `web.config` changes, if any), then delete
   `app_offline.htm`. Migrations apply on the next start.

Back up the database before an update. Schedule regular SQL Server backups in any case; the app keeps everything
there, including AI conversations and the usage log.

## Notes

- **Behind a load balancer or reverse proxy**, configure ASP.NET Core's forwarded headers so the app sees the real
  client address. Sign-in attempts are limited per address.
- **The AI features** send data to Anthropic as described in the README's AI section. Costs show on the **AI usage**
  page (administrators). Each person's AI spend is capped by `Ai:DailyBudgetUsdPerUser`.
- **Time zone:** business dates (invoice dates, "today", the morning run) use `Business:TimeZone`, `Asia/Kolkata` by
  default, whatever the server's clock is set to.

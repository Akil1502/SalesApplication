using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using SalesApp.AI;
using SalesApp.Api.Errors;
using SalesApp.Api.OpenApi;
using SalesApp.Api.Security;
using SalesApp.Core.Security;
using SalesApp.Infrastructure;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.DemoData;
using SalesApp.Infrastructure.Insights;

var builder = WebApplication.CreateBuilder(args);

// ---- Core services ---------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
// The signed-in user of the request, unless a background job has said it acts as the system for its scope.
builder.Services.AddScoped<CurrentUserOverride>();
builder.Services.AddScoped<HttpCurrentUser>();
builder.Services.AddScoped<IAuditActor, HttpAuditActor>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserOverride>().User ?? sp.GetRequiredService<HttpCurrentUser>());
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAi(builder.Configuration);
builder.Services.AddAppRateLimits(builder.Configuration);

builder.Services
    .AddControllers(options =>
    {
        // Validation comes from explicit [Required] attributes only. Without this, every
        // non-nullable parameter also gets an implicit "The request field is required." error.
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        // Numbers must be JSON numbers. The web default also accepts "12" strings, which makes the
        // OpenAPI document (and the generated TypeScript) type every number as number | string.
        options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        // Malformed JSON gets a generic message instead of serializer text with internal type names.
        options.AllowInputFormatterExceptionMessages = false;
    })
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ValidationErrorResponse.Create);

// /api/auth/login rather than /api/Auth/login.
builder.Services.AddRouting(options => options.LowercaseUrls = true);

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// The OpenAPI generator reads these (minimal-API) JSON options rather than MVC's, so mirror the
// enum-as-string setting here; otherwise the document would describe enums as integers.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<CamelCaseQueryParametersTransformer>();
});

builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

// ---- Authentication & authorisation ----------------------------------------
builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep claim types exactly as issued ("sub", "name") instead of the long WS-* URIs.
        options.MapInboundClaims = false;
        options.Events = new JwtBearerEvents
        {
            // Answer 401/403 with our ApiError body instead of an empty response.
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new ApiError(
                    "Your session has expired or you are not signed in.",
                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier));
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ApiError(
                    "You don't have permission to do that.",
                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier));
            },
        };
    });

// Validation parameters are set here (not inline above) so they read the final, validated
// JwtOptions - including values that integration tests override.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
    {
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Value.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Value.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = AppClaimTypes.Name,
        };
    });

// Role and permissions come from the database on each request, so an administrator's change applies at once.
builder.Services.AddScoped<IClaimsTransformation, PermissionClaimsTransformation>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorizationBuilder()
    // Anything not explicitly [AllowAnonymous] requires a signed-in user - a safety net in case
    // a future controller forgets to inherit ApiControllerBase.
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// ---- Pipeline ----------------------------------------------------------------
var app = builder.Build();

app.UseExceptionHandler();
app.UseSecurityHeaders(contentSecurityPolicy: !app.Environment.IsDevelopment());

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options => options.WithTitle("SalesApp API")).AllowAnonymous();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// The built React app (`npm run build --prefix frontend` writes it to wwwroot), served before authentication: the
// app has to load before anyone can sign in. File names under /assets carry a content hash, so they're cached for
// a year; index.html is never cached, so a new release is picked up straight away.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl =
        context.Context.Request.Path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable" : "no-cache",
});

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

// Any other path is a page of the React app (/customers/42, /insights, ...), so the browser gets index.html and
// the client-side router takes over. Unknown /api paths stay a JSON 404 rather than returning a web page.
app.MapFallback(async context =>
{
    var index = app.Environment.WebRootFileProvider.GetFileInfo("index.html");
    if (context.Request.Path.StartsWithSegments("/api") || !index.Exists)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new ApiError(
            index.Exists || context.Request.Path.StartsWithSegments("/api")
                ? "There's nothing at this address."
                : "The web app hasn't been built. Run: npm run build --prefix frontend",
            Activity.Current?.Id ?? context.TraceIdentifier));
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    context.Response.Headers.CacheControl = "no-cache";
    await context.Response.SendFileAsync(index);
}).AllowAnonymous();

// Command-line mode: generate demo history and exit, e.g.
//   dotnet run --project backend/src/SalesApp.Api -- --seed-demo --months 24 --reset
if (args.Contains("--seed-demo", StringComparer.OrdinalIgnoreCase))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("--seed-demo only runs in the Development environment, so it can never touch a real database.");
    }

    await DbInitializer.InitializeAsync(app.Services);
    var monthsIndex = Array.FindIndex(args, a => a.Equals("--months", StringComparison.OrdinalIgnoreCase));
    var months = monthsIndex >= 0 && monthsIndex + 1 < args.Length && int.TryParse(args[monthsIndex + 1], out var m) ? m : 24;

    using var scope = app.Services.CreateScope();
    var summary = await scope.ServiceProvider.GetRequiredService<DemoDataGenerator>()
        .GenerateAsync(new DemoDataOptions(months, Reset: args.Contains("--reset", StringComparer.OrdinalIgnoreCase)), CancellationToken.None);
    Console.WriteLine($"""
        Demo data generated in {summary.Elapsed.TotalSeconds:0.0}s for {summary.From:dd MMM yyyy} to {summary.To:dd MMM yyyy}:
          {summary.Customers} customers, {summary.Products} products, {summary.Orders} orders, {summary.Invoices} invoices,
          {summary.Payments} payments, {summary.Suppliers} suppliers, {summary.PurchaseOrders} purchase orders,
          {summary.StockReceipts} goods receipts, {summary.SupplierBills} supplier bills, {summary.SupplierPayments} supplier payments,
          {summary.CreditNotes} credit notes, {summary.Refunds} refunds, {summary.SupplierReturns} supplier returns, {summary.Rejections} rejected deliveries,
          {summary.StockMovements} stock movements, taxable sales Rs {summary.TaxableSales:N0}
        """);
    return;
}

// Command-line mode: print the insight figures computed from the current data and exit. No AI, nothing saved:
//   dotnet run --project backend/src/SalesApp.Api -- --insights-metrics
if (args.Contains("--insights-metrics", StringComparer.OrdinalIgnoreCase))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("--insights-metrics only runs in the Development environment.");
    }

    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<CurrentUserOverride>().ActAs(SystemUser.Instance);
    var computed = await scope.ServiceProvider.GetRequiredService<IInsightMetricsService>().ComputeAsync(CancellationToken.None);
    Console.OutputEncoding = Encoding.UTF8;
    Console.WriteLine(JsonSerializer.Serialize(computed, new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    }));
    return;
}

if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: false))
{
    await DbInitializer.InitializeAsync(app.Services);
}

await app.RunAsync();

/// <summary>Exposed so WebApplicationFactory&lt;Program&gt; can host the app in integration tests.</summary>
public partial class Program;

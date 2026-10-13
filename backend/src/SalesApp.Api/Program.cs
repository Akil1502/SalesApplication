using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using SalesApp.Api.Errors;
using SalesApp.Api.OpenApi;
using SalesApp.Api.Security;
using SalesApp.Core.Security;
using SalesApp.Infrastructure;
using SalesApp.Infrastructure.Data;
using SalesApp.Infrastructure.DemoData;

var builder = WebApplication.CreateBuilder(args);

// ---- Core services ---------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddInfrastructure(builder.Configuration);

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
        // Keep claim types exactly as issued ("sub", "role") instead of the long WS-* URIs.
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
            RoleClaimType = AppClaimTypes.Role,
        };
    });

builder.Services.AddAuthorizationBuilder()
    // Anything not explicitly [AllowAnonymous] requires a signed-in user - a safety net in case
    // a future controller forgets to inherit ApiControllerBase.
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// ---- Pipeline ----------------------------------------------------------------
var app = builder.Build();

app.UseExceptionHandler();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

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
          {summary.Payments} payments, {summary.StockReceipts} goods receipts, {summary.StockMovements} stock movements,
          taxable sales Rs {summary.TaxableSales:N0}
        """);
    return;
}

if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: false))
{
    await DbInitializer.InitializeAsync(app.Services);
}

await app.RunAsync();

/// <summary>Exposed so WebApplicationFactory&lt;Program&gt; can host the app in integration tests.</summary>
public partial class Program;

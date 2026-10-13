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
using SalesApp.Infrastructure;
using SalesApp.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// ---- Core services ---------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
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
        // Malformed JSON gets a generic message instead of serializer text with internal type names.
        options.AllowInputFormatterExceptionMessages = false;
    })
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ValidationErrorResponse.Create);

// /api/auth/login rather than /api/Auth/login.
builder.Services.AddRouting(options => options.LowercaseUrls = true);

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

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

if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: false))
{
    await DbInitializer.InitializeAsync(app.Services);
}

await app.RunAsync();

/// <summary>Exposed so WebApplicationFactory&lt;Program&gt; can host the app in integration tests.</summary>
public partial class Program;

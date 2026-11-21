using InvoiceHub.Api.Auth;
using InvoiceHub.Api.Errors;
using InvoiceHub.Api.Health;
using InvoiceHub.Api.Hubs;
using InvoiceHub.Api.Logging;
using InvoiceHub.Api.RateLimiting;
using InvoiceHub.Api.Tenancy;
using InvoiceHub.Api.Webhooks;
using InvoiceHub.Application;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Auth;
using InvoiceHub.Infrastructure;
using InvoiceHub.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Serilog replaces the default logger. Sinks and levels come from the "Serilog" section of the configuration,
// so they can be changed per environment without code changes.
builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddHttpContextAccessor();
// One instance per request serves both: it reads the tenant (ITenantProvider) and lets the webhook set it (ITenantAssigner).
builder.Services.AddScoped<ClaimsTenantProvider>();
builder.Services.AddScoped<ITenantProvider>(sp => sp.GetRequiredService<ClaimsTenantProvider>());
builder.Services.AddScoped<ITenantAssigner>(sp => sp.GetRequiredService<ClaimsTenantProvider>());
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton(PaymentWebhookOptions.Load(builder.Configuration)); // fails at startup if the secret is missing
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
builder.Services.AddSignalR();
builder.Services.AddSingleton<IPaymentNotifier, SignalRPaymentNotifier>();

var jwt = JwtOptions.Load(builder.Configuration);
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep claim names exactly as issued (sub, role, tenant_id) instead of remapping them.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = AppClaims.Role,
        };

        // Browsers cannot set an Authorization header on a WebSocket, so SignalR sends the token as
        // ?access_token=. It is accepted only for the hub path; every other endpoint needs the header.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments(InvoiceEventsHub.Path))
                    context.Token = token;
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorizationBuilder()
    // Secure by default: every endpoint needs a signed-in tenant user unless it opts out with [AllowAnonymous].
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireTenant().Build())
    .AddPolicy(Policies.TenantMember, policy => policy.RequireTenant())
    .AddPolicy(Policies.AdminOnly, policy => policy.RequireTenant().RequireRole(Roles.Admin));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Paste the access token from /api/auth/login. Do not include the word Bearer.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = [],
    });
});

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    // Used by docker compose so a fresh database is created on first start.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<InvoiceHub.Infrastructure.Persistence.InvoiceHubDbContext>().Database.MigrateAsync();
}

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging(RequestLogging.Configure);
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
    app.UseHsts(); // tells browsers to use HTTPS only; sent on HTTPS responses
if (!app.Configuration.GetValue<bool>("Https:RedirectDisabled"))
    app.UseHttpsRedirection(); // the compose setup serves plain HTTP on 8080, TLS is the host's job
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<InvoiceEventsHub>(InvoiceEventsHub.Path, options =>
{
    // SignalR checks the token only when a client connects. This closes the connection when the token expires,
    // so a user whose access ended cannot keep receiving payment events on an old connection.
    options.CloseOnAuthenticationExpiration = true;
});

// Probes for the host (Azure App Service, load balancers). Anonymous, and they return only a status word.
// /health says the process is up; /health/ready also checks the database.
app.MapHealthChecks("/health", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

app.Run();

// Exposed so integration tests can host the API with WebApplicationFactory<Program>.
public partial class Program;

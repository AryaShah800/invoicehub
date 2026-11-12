using InvoiceHub.Api.Auth;
using InvoiceHub.Api.Errors;
using InvoiceHub.Api.Tenancy;
using InvoiceHub.Application;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Auth;
using InvoiceHub.Infrastructure;
using InvoiceHub.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, ClaimsTenantProvider>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposed so integration tests can host the API with WebApplicationFactory<Program>.
public partial class Program;

# InvoiceHub

Multi-tenant GST invoicing REST API built with ASP.NET Core (.NET 8).

> Status: in development.

## Tech stack

C# 12, .NET 8, ASP.NET Core Web API, EF Core, SQL Server, Dapper, MediatR, FluentValidation, SignalR, ASP.NET Core Identity (JWT), xUnit, Moq, Serilog, Azure App Service, Azure SQL, Azure DevOps.

## Planned architecture

```
src/
  InvoiceHub.Domain          entities, no dependencies
  InvoiceHub.Application     MediatR commands/queries, validators, DTOs
  InvoiceHub.Infrastructure  EF Core, migrations, Dapper, Identity
  InvoiceHub.Api             controllers, JWT, SignalR hub, Serilog
tests/
  InvoiceHub.UnitTests
  InvoiceHub.IntegrationTests
```

## Roadmap

- [x] Solution skeleton, domain entities, first migration
- [x] Multi-tenancy with EF Core global query filters
- [x] Identity, JWT, policy-based authorization
- [ ] CQRS with MediatR and FluentValidation
- [ ] GST calculation (CGST/SGST vs IGST)
- [ ] Dapper stored procedure for GST reports (with benchmark)
- [ ] Payment webhook (HMAC) and SignalR updates
- [ ] Unit and integration tests
- [ ] Serilog, CI pipeline, Azure deployment

## Getting started

Prerequisites: .NET 8 SDK, SQL Server LocalDB, and the EF Core CLI (`dotnet tool install --global dotnet-ef --version 8.0.34`).

The JWT signing key is never committed. Set one for local development (at least 32 characters) before running the API or the EF tools:

```bash
dotnet user-secrets set "Jwt:SigningKey" "<a long random string>" --project src/InvoiceHub.Api
```

In other environments use the `Jwt__SigningKey` environment variable.

```bash
dotnet build
dotnet ef database update --project src/InvoiceHub.Infrastructure --startup-project src/InvoiceHub.Api
dotnet run --project src/InvoiceHub.Api
```

The default connection string in `src/InvoiceHub.Api/appsettings.json` points at `(localdb)\MSSQLLocalDB`. Swagger UI is served at `/swagger` in Development.

## Authentication

| Endpoint | Access | Purpose |
|---|---|---|
| `POST /api/auth/register` | anonymous | Create a business (tenant) and its first user, who becomes Admin |
| `POST /api/auth/login` | anonymous | Exchange email and password for a JWT (account locks for 15 minutes after 5 failures) |
| `GET /api/auth/me` | any tenant user | Show who the token belongs to |
| `POST /api/users` | Admin only | Create a Staff user in the caller's own tenant |

Tokens carry `sub`, `email`, `tenant_id` and `role` claims. The tenant for every request comes from the `tenant_id` claim, and that is what the EF Core query filters use. Every endpoint requires a signed-in tenant user unless it is marked `[AllowAnonymous]`. In Swagger, use Authorize and paste the token from `/api/auth/login`.

## Tests

```bash
dotnet test
```

Integration tests create a throwaway database on LocalDB from the real migrations and drop it afterwards, so LocalDB must be running.

To add a migration:

```bash
dotnet ef migrations add <Name> --project src/InvoiceHub.Infrastructure --startup-project src/InvoiceHub.Api --output-dir Persistence/Migrations
```

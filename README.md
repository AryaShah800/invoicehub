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
- [x] CQRS with MediatR and FluentValidation
- [x] GST calculation (CGST/SGST vs IGST)
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

## Invoices

| Endpoint | Purpose |
|---|---|
| `POST /api/invoices` | Create a draft invoice for one of your customers |
| `GET /api/invoices/{id}` | Get an invoice with its lines (another tenant's invoice is a 404) |

Requests are MediatR commands and queries. A pipeline behavior runs the FluentValidation validators before the handler, so handlers only see valid input. An invoice line is either a **catalogue line** (`productId` plus `quantity`; price, HSN code and GST rate come from the product and cannot be overridden) or a **custom line** (`description`, `hsnCode`, `unitPrice`, `gstRate`).

Errors are returned as ProblemDetails: 400 validation, 404 not found, 409 conflict (such as a duplicate invoice number), 422 business rule violated.

### GST

The seller's state is copied from the tenant onto each invoice when it is created, and the place of supply is the customer's state.

| Seller state vs place of supply | Tax charged |
|---|---|
| Same | CGST + SGST, each half the rate |
| Different | IGST at the full rate |

Tax is rounded to 2 decimals (half away from zero) per line, and invoice totals are the sum of those rounded line amounts. The response carries `cgstAmount`, `sgstAmount`, `igstAmount` on each line and `taxableTotal`, `cgstTotal`, `sgstTotal`, `igstTotal`, `taxTotal`, `grandTotal` on the invoice. There is no whole-rupee round-off on the grand total. Valid rates are the slabs 0, 0.25, 3, 5, 12, 18 and 28 percent. Cess, reverse charge and e-invoicing are not implemented.

MediatR is pinned to 12.5.0, the last release under the Apache licence (13 and later need a licence key in production).

## Tests

```bash
dotnet test
```

Integration tests create a throwaway database on LocalDB from the real migrations and drop it afterwards, so LocalDB must be running.

To add a migration:

```bash
dotnet ef migrations add <Name> --project src/InvoiceHub.Infrastructure --startup-project src/InvoiceHub.Api --output-dir Persistence/Migrations
```

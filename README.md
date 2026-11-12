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
- [ ] Multi-tenancy with EF Core global query filters
- [ ] Identity, JWT, policy-based authorization
- [ ] CQRS with MediatR and FluentValidation
- [ ] GST calculation (CGST/SGST vs IGST)
- [ ] Dapper stored procedure for GST reports (with benchmark)
- [ ] Payment webhook (HMAC) and SignalR updates
- [ ] Unit and integration tests
- [ ] Serilog, CI pipeline, Azure deployment

## Getting started

Prerequisites: .NET 8 SDK, SQL Server LocalDB, and the EF Core CLI (`dotnet tool install --global dotnet-ef --version 8.0.34`).

```bash
dotnet build
dotnet ef database update --project src/InvoiceHub.Infrastructure --startup-project src/InvoiceHub.Api
dotnet run --project src/InvoiceHub.Api
```

The default connection string in `src/InvoiceHub.Api/appsettings.json` points at `(localdb)\MSSQLLocalDB`. Swagger UI is served at `/swagger` in Development.

To add a migration:

```bash
dotnet ef migrations add <Name> --project src/InvoiceHub.Infrastructure --startup-project src/InvoiceHub.Api --output-dir Persistence/Migrations
```

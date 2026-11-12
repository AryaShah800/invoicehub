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

- [ ] Solution skeleton, domain entities, first migration
- [ ] Multi-tenancy with EF Core global query filters
- [ ] Identity, JWT, policy-based authorization
- [ ] CQRS with MediatR and FluentValidation
- [ ] GST calculation (CGST/SGST vs IGST)
- [ ] Dapper stored procedure for GST reports (with benchmark)
- [ ] Payment webhook (HMAC) and SignalR updates
- [ ] Unit and integration tests
- [ ] Serilog, CI pipeline, Azure deployment

## Getting started

Setup instructions will be added as the project takes shape.

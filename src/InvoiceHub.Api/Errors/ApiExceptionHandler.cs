using FluentValidation;
using InvoiceHub.Application.Common;
using InvoiceHub.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Api.Errors;

/// <summary>
/// Turns the exceptions the application throws on purpose into ProblemDetails responses.
/// Anything else is left alone, so it becomes a plain 500 without leaking details.
/// </summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    private const int SqlUniqueIndexViolation = 2601;
    private const int SqlUniqueConstraintViolation = 2627;
    private const int SqliteUniqueViolation = 19;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            ValidationException validation => new ValidationProblemDetails(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            },
            NotFoundException notFound => Problem(StatusCodes.Status404NotFound, "Not found", notFound.Message),
            ConflictException conflict => Problem(StatusCodes.Status409Conflict, "Conflict", conflict.Message),
            DomainException domain => Problem(StatusCodes.Status422UnprocessableEntity, "Business rule violated", domain.Message),
            DbUpdateConcurrencyException =>
                Problem(StatusCodes.Status409Conflict, "Conflict", "The record was changed by another request. Please retry."),
            DbUpdateException { InnerException: SqlException { Number: SqlUniqueIndexViolation or SqlUniqueConstraintViolation } } =>
                Problem(StatusCodes.Status409Conflict, "Conflict", "The data conflicts with an existing record."),
            // SQLite (the hosted demo): result code 19 is a constraint violation, here a unique index
            DbUpdateException { InnerException: SqliteException { SqliteErrorCode: SqliteUniqueViolation } } =>
                Problem(StatusCodes.Status409Conflict, "Conflict", "The data conflicts with an existing record."),
            _ => null,
        };

        if (problem is null)
            return false;

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };
}

using FluentValidation;
using MediatR;

namespace InvoiceHub.Application.Common;

/// <summary>
/// MediatR pipeline behavior that runs every FluentValidation validator for a request before its
/// handler. Failures are thrown as a single <see cref="ValidationException"/>, so handlers can
/// assume their input is valid.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var validatorList = validators.ToList();
        if (validatorList.Count == 0)
            return await next(cancellationToken);

        // One context per validator: a ValidationContext collects failures, so sharing one would
        // make each validator's result include the others' failures.
        var results = await Task.WhenAll(validatorList.Select(
            v => v.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken)));

        var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();
        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next(cancellationToken);
    }
}

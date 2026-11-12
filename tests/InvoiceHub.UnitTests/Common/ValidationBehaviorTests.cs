using FluentValidation;
using InvoiceHub.Application.Common;
using MediatR;

namespace InvoiceHub.UnitTests.Common;

public sealed class ValidationBehaviorTests
{
    private sealed record Ping(string Name, int Count) : IRequest<string>;

    private sealed class NameValidator : AbstractValidator<Ping>
    {
        public NameValidator() => RuleFor(p => p.Name).NotEmpty().WithMessage("Name is required.");
    }

    private sealed class CountValidator : AbstractValidator<Ping>
    {
        public CountValidator() => RuleFor(p => p.Count).GreaterThan(0).WithMessage("Count must be positive.");
    }

    private static Task<string> RunAsync(Ping request, Action? onHandler, params IValidator<Ping>[] validators)
    {
        var behavior = new ValidationBehavior<Ping, string>(validators);
        return behavior.Handle(request, _ =>
        {
            onHandler?.Invoke();
            return Task.FromResult("handled");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task A_valid_request_reaches_the_handler()
    {
        var result = await RunAsync(new Ping("x", 1), null, new NameValidator(), new CountValidator());

        Assert.Equal("handled", result);
    }

    [Fact]
    public async Task An_invalid_request_throws_and_never_reaches_the_handler()
    {
        var handlerRan = false;

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => RunAsync(new Ping("", 1), () => handlerRan = true, new NameValidator()));

        Assert.False(handlerRan);
        Assert.Equal("Name is required.", Assert.Single(ex.Errors).ErrorMessage);
    }

    [Fact]
    public async Task Failures_from_all_validators_are_reported_together()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => RunAsync(new Ping("", 0), null, new NameValidator(), new CountValidator()));

        Assert.Equal(2, ex.Errors.Count());
    }

    [Fact]
    public async Task A_request_with_no_validators_passes_through()
    {
        var result = await RunAsync(new Ping("", 0), null);

        Assert.Equal("handled", result);
    }
}

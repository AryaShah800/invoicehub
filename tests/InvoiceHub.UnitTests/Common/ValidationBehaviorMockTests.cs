using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using InvoiceHub.Application.Common;
using MediatR;
using Moq;

namespace InvoiceHub.UnitTests.Common;

// Public so Moq can create a proxy for IValidator<MockedPing>.
public sealed record MockedPing(string Name) : IRequest<string>;

/// <summary>
/// ValidationBehavior with mocked validators: checks what the behavior passes to each validator,
/// that every validator runs, and that the handler only runs when all of them pass.
/// </summary>
public sealed class ValidationBehaviorMockTests
{
    private int _handlerCalls;

    private Task<string> Next(CancellationToken _)
    {
        _handlerCalls++;
        return Task.FromResult("handled");
    }

    private static Mock<IValidator<MockedPing>> ValidatorReturning(params ValidationFailure[] failures)
    {
        var validator = new Mock<IValidator<MockedPing>>();
        validator
            .Setup(v => v.ValidateAsync(It.IsAny<IValidationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(failures));
        return validator;
    }

    [Fact]
    public async Task Every_validator_runs_once_with_the_request_and_the_callers_cancellation_token()
    {
        var request = new MockedPing("x");
        var first = ValidatorReturning();
        var second = ValidatorReturning();
        using var cts = new CancellationTokenSource();

        var result = await new ValidationBehavior<MockedPing, string>([first.Object, second.Object])
            .Handle(request, Next, cts.Token);

        result.Should().Be("handled");
        _handlerCalls.Should().Be(1);
        foreach (var validator in new[] { first, second })
        {
            validator.Verify(v => v.ValidateAsync(
                It.Is<IValidationContext>(c => ReferenceEquals(c.InstanceToValidate, request)),
                cts.Token), Times.Once);
        }
    }

    [Fact]
    public async Task Each_validator_gets_its_own_context_so_failures_do_not_leak_between_them()
    {
        var contexts = new List<IValidationContext>();
        var first = ValidatorReturning();
        var second = ValidatorReturning();
        foreach (var validator in new[] { first, second })
        {
            validator
                .Setup(v => v.ValidateAsync(It.IsAny<IValidationContext>(), It.IsAny<CancellationToken>()))
                .Callback<IValidationContext, CancellationToken>((c, _) => contexts.Add(c))
                .ReturnsAsync(new ValidationResult());
        }

        await new ValidationBehavior<MockedPing, string>([first.Object, second.Object])
            .Handle(new MockedPing("x"), Next, CancellationToken.None);

        contexts.Should().HaveCount(2).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task One_failing_validator_blocks_the_handler_and_the_others_still_run()
    {
        var passing = ValidatorReturning();
        var failing = ValidatorReturning(new ValidationFailure("Name", "Name is required."));
        var alsoPassing = ValidatorReturning();

        var act = () => new ValidationBehavior<MockedPing, string>([passing.Object, failing.Object, alsoPassing.Object])
            .Handle(new MockedPing(""), Next, CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Be("Name is required.");
        _handlerCalls.Should().Be(0);
        alsoPassing.Verify(v => v.ValidateAsync(It.IsAny<IValidationContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Failures_from_several_validators_are_combined_into_one_exception()
    {
        var nameRule = ValidatorReturning(new ValidationFailure("Name", "Name is required."));
        var otherRules = ValidatorReturning(
            new ValidationFailure("Name", "Name is too short."),
            new ValidationFailure("Count", "Count must be positive."));

        var act = () => new ValidationBehavior<MockedPing, string>([nameRule.Object, otherRules.Object])
            .Handle(new MockedPing(""), Next, CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Select(e => e.ErrorMessage).Should().BeEquivalentTo(
                "Name is required.", "Name is too short.", "Count must be positive.");
        _handlerCalls.Should().Be(0);
    }

    [Fact]
    public async Task A_cancelled_validation_propagates_and_the_handler_never_runs()
    {
        var validator = new Mock<IValidator<MockedPing>>();
        validator
            .Setup(v => v.ValidateAsync(It.IsAny<IValidationContext>(), It.IsAny<CancellationToken>()))
            .Returns<IValidationContext, CancellationToken>((_, token) => Task.FromCanceled<ValidationResult>(token));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => new ValidationBehavior<MockedPing, string>([validator.Object])
            .Handle(new MockedPing("x"), Next, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _handlerCalls.Should().Be(0);
    }
}

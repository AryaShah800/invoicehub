using FluentValidation;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Common;
using InvoiceHub.Domain.Invoices;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Application.Invoices.Commands.IssueInvoice;

/// <summary>Moves a draft invoice to Issued, after which its lines are locked and it can receive payments.</summary>
public sealed record IssueInvoiceCommand(Guid Id) : IRequest<InvoiceDto>;

public sealed class IssueInvoiceCommandValidator : AbstractValidator<IssueInvoiceCommand>
{
    public IssueInvoiceCommandValidator() => RuleFor(c => c.Id).NotEmpty();
}

public sealed class IssueInvoiceCommandHandler(IApplicationDbContext db, IBusinessClock clock)
    : IRequestHandler<IssueInvoiceCommand, InvoiceDto>
{
    public async Task<InvoiceDto> Handle(IssueInvoiceCommand request, CancellationToken cancellationToken)
    {
        // The tenant query filter hides other tenants' invoices, so they look the same as missing ones.
        var invoice = await db.Invoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Invoice), request.Id);

        invoice.Issue(); // DomainException (422) if it is not a draft or has no lines
        await db.SaveChangesAsync(cancellationToken);

        var customerName = await db.Customers
            .AsNoTracking()
            .Where(c => c.Id == invoice.CustomerId)
            .Select(c => c.Name)
            .FirstAsync(cancellationToken);

        return invoice.ToDto(customerName, clock.Today());
    }
}

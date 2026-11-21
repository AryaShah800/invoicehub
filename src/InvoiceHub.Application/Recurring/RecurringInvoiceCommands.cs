using FluentValidation;
using InvoiceHub.Application.Abstractions;
using InvoiceHub.Application.Common;
using InvoiceHub.Domain.Customers;
using InvoiceHub.Domain.Invoices;
using InvoiceHub.Domain.Products;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Application.Recurring;

public sealed record RecurringInvoiceLineInput(Guid ProductId, decimal Quantity);

public sealed record RecurringInvoiceDto(
    Guid Id, string Name, Guid CustomerId, int IntervalMonths, DateOnly NextRunDate, DateOnly? EndDate,
    int DueInDays, bool IsActive, IReadOnlyList<RecurringInvoiceLineInput> Lines)
{
    public static RecurringInvoiceDto From(RecurringInvoice r) => new(
        r.Id, r.Name, r.CustomerId, r.IntervalMonths, r.NextRunDate, r.EndDate, r.DueInDays, r.IsActive,
        r.Lines.Select(l => new RecurringInvoiceLineInput(l.ProductId, l.Quantity)).ToList());
}

/// <summary>Creates a template. The first invoice is raised by the background job on or after FirstRunDate.</summary>
public sealed record CreateRecurringInvoiceCommand(
    string Name, Guid CustomerId, int IntervalMonths, DateOnly FirstRunDate, DateOnly? EndDate, int DueInDays,
    IReadOnlyList<RecurringInvoiceLineInput> Lines) : IRequest<RecurringInvoiceDto>;

public sealed class CreateRecurringInvoiceCommandValidator : AbstractValidator<CreateRecurringInvoiceCommand>
{
    public CreateRecurringInvoiceCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(RecurringInvoice.NameMaxLength);
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.IntervalMonths).InclusiveBetween(1, RecurringInvoice.MaxIntervalMonths);
        RuleFor(c => c.DueInDays).InclusiveBetween(0, 365);
        RuleFor(c => c.EndDate).GreaterThanOrEqualTo(c => c.FirstRunDate).When(c => c.EndDate.HasValue);
        RuleFor(c => c.Lines).NotNull().NotEmpty().Must(l => l is null || l.Count <= RecurringInvoice.MaxLines);
        RuleForEach(c => c.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0).LessThanOrEqualTo(1_000_000m);
        });
    }
}

public sealed class CreateRecurringInvoiceCommandHandler(IApplicationDbContext db, ITenantProvider tenantProvider)
    : IRequestHandler<CreateRecurringInvoiceCommand, RecurringInvoiceDto>
{
    public async Task<RecurringInvoiceDto> Handle(CreateRecurringInvoiceCommand request, CancellationToken ct)
    {
        var tenantId = tenantProvider.TenantId ?? throw new InvalidOperationException("A current tenant is required.");
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
            ?? throw new NotFoundException(nameof(Customer), request.CustomerId);

        var ids = request.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var missing = ids.FirstOrDefault(id => !products.ContainsKey(id));
        if (missing != Guid.Empty)
            throw new NotFoundException(nameof(Product), missing);

        var template = new RecurringInvoice(tenantId, customer, request.Name, request.IntervalMonths, request.FirstRunDate,
            request.DueInDays, request.EndDate, request.Lines.Select(l => (products[l.ProductId], l.Quantity)));
        db.RecurringInvoices.Add(template);
        await db.SaveChangesAsync(ct);
        return RecurringInvoiceDto.From(template);
    }
}

public sealed record ListRecurringInvoicesQuery : IRequest<IReadOnlyList<RecurringInvoiceDto>>;

public sealed class ListRecurringInvoicesQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ListRecurringInvoicesQuery, IReadOnlyList<RecurringInvoiceDto>>
{
    public async Task<IReadOnlyList<RecurringInvoiceDto>> Handle(ListRecurringInvoicesQuery request, CancellationToken ct)
    {
        var items = await db.RecurringInvoices.Include(r => r.Lines).OrderBy(r => r.NextRunDate).ThenBy(r => r.Name).ToListAsync(ct);
        return items.Select(RecurringInvoiceDto.From).ToList();
    }
}

public sealed record PauseRecurringInvoiceCommand(Guid Id) : IRequest<RecurringInvoiceDto>;

public sealed class PauseRecurringInvoiceCommandHandler(IApplicationDbContext db)
    : IRequestHandler<PauseRecurringInvoiceCommand, RecurringInvoiceDto>
{
    public async Task<RecurringInvoiceDto> Handle(PauseRecurringInvoiceCommand request, CancellationToken ct)
    {
        var template = await db.RecurringInvoices.Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(RecurringInvoice), request.Id);
        template.Pause();
        await db.SaveChangesAsync(ct);
        return RecurringInvoiceDto.From(template);
    }
}

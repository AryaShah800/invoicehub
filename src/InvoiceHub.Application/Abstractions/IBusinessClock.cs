namespace InvoiceHub.Application.Abstractions;

public interface IBusinessClock
{
    DateOnly Today();
}

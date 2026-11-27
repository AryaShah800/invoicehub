using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using InvoiceHub.Application.Auth;
using InvoiceHub.Application.Common;
using InvoiceHub.Application.Customers;
using InvoiceHub.Application.Dashboard;
using InvoiceHub.Application.Invoices;
using InvoiceHub.Application.Invoices.Commands.CreateInvoice;
using InvoiceHub.Application.Invoices.Queries.ListInvoices;
using InvoiceHub.Application.Products;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Web.Services;

public sealed record ApiEndpoint(Uri BaseUri);

/// <summary>An API error the UI can show: a message and, for 400s, the per-field messages.</summary>
public sealed class ApiException(HttpStatusCode status, string message, IReadOnlyDictionary<string, string[]>? errors = null)
    : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();

    public IEnumerable<string> AllMessages => Errors.Count == 0 ? [Message] : Errors.SelectMany(e => e.Value);
}

public sealed record InvoiceFilter(
    string? Status = null, Guid? CustomerId = null, DateOnly? From = null, DateOnly? To = null, bool OverdueOnly = false,
    int Page = 1, int PageSize = 15);

/// <summary>Everything the UI needs from the InvoiceHub API. Components depend on this so tests can fake it.</summary>
public interface IInvoiceApi
{
    Task<AuthResponse> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<DashboardSummary> GetDashboardAsync(CancellationToken ct = default);
    Task<PagedResult<InvoiceSummaryDto>> ListInvoicesAsync(InvoiceFilter filter, CancellationToken ct = default);
    Task<InvoiceDto> GetInvoiceAsync(Guid id, CancellationToken ct = default);
    Task<InvoiceDto> IssueInvoiceAsync(Guid id, CancellationToken ct = default);
    Task<byte[]> GetInvoicePdfAsync(Guid id, CancellationToken ct = default);
    Task<InvoiceDto> CreateInvoiceAsync(CreateInvoiceCommand command, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerDto>> ListCustomersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ProductDto>> ListActiveProductsAsync(CancellationToken ct = default);
}

public sealed class ApiClient(HttpClient http, AuthSession session) : IInvoiceApi
{
    public async Task<AuthResponse> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password), ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new ApiException(response.StatusCode, "Email or password is not correct.");
        return await ReadAsync<AuthResponse>(response, ct);
    }

    public Task<DashboardSummary> GetDashboardAsync(CancellationToken ct = default) =>
        GetAsync<DashboardSummary>("/api/dashboard?months=12", ct);

    public Task<PagedResult<InvoiceSummaryDto>> ListInvoicesAsync(InvoiceFilter f, CancellationToken ct = default)
    {
        var query = new List<string> { $"page={f.Page}", $"pageSize={f.PageSize}" };
        if (!string.IsNullOrEmpty(f.Status)) query.Add($"status={Uri.EscapeDataString(f.Status)}");
        if (f.CustomerId is { } c) query.Add($"customerId={c}");
        if (f.From is { } from) query.Add($"from={from:yyyy-MM-dd}");
        if (f.To is { } to) query.Add($"to={to:yyyy-MM-dd}");
        if (f.OverdueOnly) query.Add("overdue=true");
        return GetAsync<PagedResult<InvoiceSummaryDto>>("/api/invoices?" + string.Join('&', query), ct);
    }

    public Task<InvoiceDto> GetInvoiceAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<InvoiceDto>($"/api/invoices/{id}", ct);

    public async Task<InvoiceDto> IssueInvoiceAsync(Guid id, CancellationToken ct = default) =>
        await ReadAsync<InvoiceDto>(await SendAsync(HttpMethod.Post, $"/api/invoices/{id}/issue", JsonContent.Create(new { }), ct), ct);

    public async Task<byte[]> GetInvoicePdfAsync(Guid id, CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/invoices/{id}/pdf", null, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<InvoiceDto> CreateInvoiceAsync(CreateInvoiceCommand command, CancellationToken ct = default) =>
        await ReadAsync<InvoiceDto>(await SendAsync(HttpMethod.Post, "/api/invoices", JsonContent.Create(command), ct), ct);

    public Task<IReadOnlyList<CustomerDto>> ListCustomersAsync(CancellationToken ct = default) =>
        GetAllPagesAsync<CustomerDto>("/api/customers?", ct);

    public Task<IReadOnlyList<ProductDto>> ListActiveProductsAsync(CancellationToken ct = default) =>
        GetAllPagesAsync<ProductDto>("/api/products?isActive=true&", ct);

    /// <summary>Dropdowns need every item, but the API pages at most 100 at a time.</summary>
    public const int MaxDropdownPages = 20;

    /// <summary>
    /// Reads every page of a list (up to MaxDropdownPages x 100 items). One page used to be read, so the 101st
    /// customer or product silently never appeared in the new-invoice dropdowns.
    /// </summary>
    private async Task<IReadOnlyList<T>> GetAllPagesAsync<T>(string urlWithQuery, CancellationToken ct)
    {
        var items = new List<T>();
        for (var page = 1; page <= MaxDropdownPages; page++)
        {
            var result = await GetAsync<PagedResult<T>>($"{urlWithQuery}page={page}&pageSize={PagedResult<T>.MaxPageSize}", ct);
            items.AddRange(result.Items);
            if (page >= result.TotalPages || result.Items.Count == 0) break;
        }
        return items;
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken ct) =>
        await ReadAsync<T>(await SendAsync(HttpMethod.Get, url, null, ct), ct);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        if (session.Token is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await session.SignOutAsync();
            throw new ApiException(response.StatusCode, "Your session has ended. Please sign in again.");
        }
        return response;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;
        ValidationProblemDetails? problem = null;
        try { problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(ct); }
        catch (Exception) { /* not a problem document */ }
        var message = problem?.Detail ?? problem?.Title ?? $"The server answered {(int)response.StatusCode}.";
        throw new ApiException(response.StatusCode, message, problem?.Errors as IReadOnlyDictionary<string, string[]> ?? problem?.Errors.ToDictionary(e => e.Key, e => e.Value));
    }
}

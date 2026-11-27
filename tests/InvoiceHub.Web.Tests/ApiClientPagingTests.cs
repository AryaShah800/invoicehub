using System.Net;
using System.Net.Http.Json;
using Bunit;
using InvoiceHub.Application.Common;
using InvoiceHub.Application.Customers;
using InvoiceHub.Web.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;

namespace InvoiceHub.Web.Tests;

public sealed class ApiClientPagingTests : TestContext
{
    /// <summary>Serves 150 customers, 100 per page, and records the URLs it was asked for.</summary>
    private sealed class PagedCustomers : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            var page = int.Parse(query["page"]!);
            var pageSize = int.Parse(query["pageSize"]!);
            var items = Enumerable.Range((page - 1) * pageSize, pageSize).Where(i => i < 150)
                .Select(i => new CustomerDto(Guid.NewGuid(), $"Customer {i}", $"c{i}@example.test", null, "27", "1 Road", null, "Pune", "411001"))
                .ToList();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PagedResult<CustomerDto>(items, page, pageSize, 150)),
            });
        }
    }

    [Fact]
    public async Task Dropdown_lists_read_every_page_not_just_the_first_100()
    {
        var handler = new PagedCustomers();
        var session = new AuthSession(new ProtectedSessionStorage(JSInterop.JSRuntime, new EphemeralDataProtectionProvider()));
        var client = new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test") }, session);

        var customers = await client.ListCustomersAsync();

        Assert.Equal(150, customers.Count);
        Assert.Equal("Customer 149", customers[^1].Name);
        Assert.Equal(["/api/customers?page=1&pageSize=100", "/api/customers?page=2&pageSize=100"], handler.Requests);
    }
}

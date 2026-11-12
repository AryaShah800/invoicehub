using System.Net;
using System.Net.Http.Json;
using InvoiceHub.Application.Auth;
using InvoiceHub.Application.Customers;
using InvoiceHub.Application.Customers.Commands;
using InvoiceHub.Application.Invoices;
using InvoiceHub.Application.Invoices.Commands.CreateInvoice;
using InvoiceHub.Application.Products;
using InvoiceHub.Application.Products.Commands;

namespace InvoiceHub.IntegrationTests.Api;

/// <summary>Customers and products: the data an invoice is built from, created through the API.</summary>
[Collection(ApiCollection.Name)]
public sealed class CatalogueApiTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static CreateCustomerCommand NewCustomer(string state = "29", string? gstin = null) => new(
        "Acme Pvt Ltd", TestData.NewEmail(), gstin, state, "5 Park Street", null, "Bengaluru", "560001");

    private static CreateProductCommand NewProduct(decimal rate = 18m) => new("Widget", "8471", 1000m, rate);

    private async Task<AuthResponse> NewStaffAsync(AuthResponse admin)
    {
        var email = TestData.NewEmail();
        var created = await _client.PostAsync("/api/users", new CreateStaffUserRequest(email, TestData.Password), admin.AccessToken);
        created.EnsureSuccessStatusCode();
        return await _client.LoginAsync(email, TestData.Password);
    }

    // ---- customers ----

    [Fact]
    public async Task Creating_a_customer_returns_201_and_the_customer_can_be_fetched()
    {
        var auth = await _client.RegisterAsync();

        var response = await _client.PostAsync("/api/customers", NewCustomer(), auth.AccessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var customer = (await response.Content.ReadFromJsonAsync<CustomerDto>())!;
        Assert.EndsWith($"/api/customers/{customer.Id}", response.Headers.Location!.ToString());

        var fetched = await _client.GetAsync($"/api/customers/{customer.Id}", auth.AccessToken);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("29", (await fetched.Content.ReadFromJsonAsync<CustomerDto>())!.StateCode);
    }

    [Fact]
    public async Task A_staff_user_can_create_customers()
    {
        var staff = await NewStaffAsync(await _client.RegisterAsync());

        var response = await _client.PostAsync("/api/customers", NewCustomer(), staff.AccessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_customer_with_a_GSTIN_from_another_state_is_rejected_with_422()
    {
        var auth = await _client.RegisterAsync();
        var gstinFromMaharashtra = TestData.NewGstin(); // starts with 27

        var response = await _client.PostAsync("/api/customers", NewCustomer(state: "29", gstin: gstinFromMaharashtra), auth.AccessToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData("", "a@b.test", "29")]
    [InlineData("Name", "not-an-email", "29")]
    [InlineData("Name", "a@b.test", "MH")]
    public async Task Invalid_customer_input_is_rejected_with_400(string name, string email, string state)
    {
        var auth = await _client.RegisterAsync();
        var command = NewCustomer() with { Name = name, Email = email, StateCode = state };

        var response = await _client.PostAsync("/api/customers", command, auth.AccessToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Another_tenants_customer_is_a_404()
    {
        var owner = await _client.RegisterAsync();
        var customer = (await (await _client.PostAsync("/api/customers", NewCustomer(), owner.AccessToken))
            .Content.ReadFromJsonAsync<CustomerDto>())!;
        var other = await _client.RegisterAsync();

        var response = await _client.GetAsync($"/api/customers/{customer.Id}", other.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Customer_endpoints_need_a_token()
    {
        var response = await _client.PostAsync("/api/customers", NewCustomer());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- products ----

    [Fact]
    public async Task An_admin_can_create_a_product_and_fetch_it()
    {
        var admin = await _client.RegisterAsync();

        var response = await _client.PostAsync("/api/products", NewProduct(), admin.AccessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = (await response.Content.ReadFromJsonAsync<ProductDto>())!;
        Assert.True(product.IsActive);
        Assert.Equal(18m, product.GstRate);

        var fetched = await _client.GetAsync($"/api/products/{product.Id}", admin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Fact]
    public async Task A_staff_user_cannot_create_products_but_can_read_them()
    {
        var admin = await _client.RegisterAsync();
        var staff = await NewStaffAsync(admin);
        var product = (await (await _client.PostAsync("/api/products", NewProduct(), admin.AccessToken))
            .Content.ReadFromJsonAsync<ProductDto>())!;

        var create = await _client.PostAsync("/api/products", NewProduct(), staff.AccessToken);
        var read = await _client.GetAsync($"/api/products/{product.Id}", staff.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Theory]
    [InlineData("Widget", "12", 100, 18)]     // HSN must be 4, 6 or 8 digits
    [InlineData("Widget", "8471", -1, 18)]    // negative price
    [InlineData("Widget", "8471", 100, 7)]    // not a GST slab
    [InlineData("", "8471", 100, 18)]         // no name
    public async Task Invalid_product_input_is_rejected_with_400(string name, string hsn, decimal price, decimal rate)
    {
        var admin = await _client.RegisterAsync();

        var response = await _client.PostAsync("/api/products", new CreateProductCommand(name, hsn, price, rate), admin.AccessToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Another_tenants_product_is_a_404()
    {
        var owner = await _client.RegisterAsync();
        var product = (await (await _client.PostAsync("/api/products", NewProduct(), owner.AccessToken))
            .Content.ReadFromJsonAsync<ProductDto>())!;
        var other = await _client.RegisterAsync();

        var response = await _client.GetAsync($"/api/products/{product.Id}", other.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- the whole flow, through the API only ----

    [Fact]
    public async Task A_new_business_can_go_from_registration_to_an_IGST_invoice_using_only_the_API()
    {
        var admin = await _client.RegisterAsync(); // seller in state 27
        var customer = (await (await _client.PostAsync("/api/customers", NewCustomer(state: "29"), admin.AccessToken))
            .Content.ReadFromJsonAsync<CustomerDto>())!;
        var product = (await (await _client.PostAsync("/api/products", NewProduct(rate: 12m), admin.AccessToken))
            .Content.ReadFromJsonAsync<ProductDto>())!;

        var command = new CreateInvoiceCommand(
            customer.Id, "INV-0001", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), null,
            [new CreateInvoiceLine(product.Id, null, null, 3, null, null)]);
        var response = await _client.PostAsync("/api/invoices", command, admin.AccessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invoice = (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
        Assert.False(invoice.IsIntraState);
        Assert.Equal(3000m, invoice.TaxableTotal);
        Assert.Equal(360m, invoice.IgstTotal);
        Assert.Equal(3360m, invoice.GrandTotal);
    }
}

using InvoiceHub.Application;
using InvoiceHub.Web.Components;
using InvoiceHub.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddBusinessClock(builder.Configuration);

var apiBase = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5080";
builder.Services.AddSingleton(new ApiEndpoint(new Uri(apiBase)));
builder.Services.AddSingleton(new DemoLogin(
    builder.Configuration.GetValue<bool>("Demo:Enabled"),
    builder.Configuration["Demo:Email"] ?? "demo@invoicehub.example",
    builder.Configuration["Demo:Password"] ?? "Demo#Pass2026"));
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<InvoiceEvents>();
builder.Services.AddHttpClient<IInvoiceApi, ApiClient>((sp, http) => http.BaseAddress = sp.GetRequiredService<ApiEndpoint>().BaseUri);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

app.UseStaticFiles();
app.UseAntiforgery();
app.MapGet("/health", () => Results.Ok("ok"));
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

public partial class Program;

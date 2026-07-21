using System.Text.Json.Serialization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Services;
using Legacy.Maliev.AccountingService.Data;
using Maliev.Aspire.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddDefaultApiVersioning();
builder.AddLegacyAuthServiceTokenExchange();
builder.AddPostgresDbContext<PaymentDbContext>(connectionName: "PaymentDbContext");
builder.AddPostgresDbContext<InvoiceDbContext>(connectionName: "InvoiceDbContext");
builder.AddPostgresDbContext<ReceiptDbContext>(connectionName: "ReceiptDbContext");
builder.AddStandardCache("legacy:accounting:");
builder.AddStandardCors();
builder.AddJwtAuthentication();
builder.AddStandardMiddleware(options => options.EnableRequestLogging = true);
builder.AddStandardOpenApi(
    title: "Legacy MALIEV Accounting Service API",
    description: "Temporary .NET 10 compatibility API for historical payment, invoice, and receipt records. It does not execute payments.");
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.JsonSerializerOptions.PropertyNamingPolicy = null;
    options.JsonSerializerOptions.DictionaryKeyPolicy = null;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<DistributedAccountingCache>();
builder.Services.AddScoped<IAccountingCache>(provider => provider.GetRequiredService<DistributedAccountingCache>());
builder.Services.AddScoped<IIdempotencyStore>(provider => provider.GetRequiredService<DistributedAccountingCache>());
builder.Services.AddScoped<IAccountingService, AccountingRepository>();
builder.Services.AddScoped<IReceiptWorkflowStore, ReceiptWorkflowStore>();
builder.Services.AddScoped<IReceiptOperationLock, PostgresReceiptOperationLock>();
builder.Services.AddScoped<IReceiptOperationJournal, ReceiptOperationJournal>();
builder.Services.AddScoped<IReceiptWorkflow, ReceiptWorkflowService>();
builder.Services.AddScoped<IInvoiceCreationStore, InvoiceCreationStore>();
builder.Services.AddScoped<IInvoiceCreationJournal, InvoiceCreationJournal>();
builder.Services.AddScoped<IInvoiceCreationLock, PostgresInvoiceCreationLock>();
builder.Services.AddScoped<IInvoiceCreationSource, InvoiceCreationSourceClient>();
builder.Services.AddScoped<IInvoiceCreationWorkflow, InvoiceCreationWorkflowService>();
builder.Services.AddHttpClient<IReceiptDocumentClient, ReceiptDocumentClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Document"]
        ?? "https+http://legacy-maliev-document-service");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddServiceDiscovery().AddLegacyServiceAuthentication();
builder.Services.AddHttpClient<IReceiptFileClient, ReceiptFileClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:File"]
        ?? "https+http://legacy-maliev-file-service");
    client.Timeout = TimeSpan.FromMinutes(5);
}).AddServiceDiscovery().AddLegacyServiceAuthentication();
builder.Services.AddHttpClient(ReceiptFileClient.ObjectDownloadClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<IReceiptNotificationClient, ReceiptNotificationClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Notification"]
        ?? "https+http://legacy-maliev-notification-service");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddServiceDiscovery().AddLegacyServiceAuthentication();
builder.Services.AddHttpClient<IReceiptCustomerClient, ReceiptCustomerClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Customer"]
        ?? "https+http://legacy-maliev-customer-service");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddServiceDiscovery().AddLegacyServiceAuthentication();
builder.Services.AddHttpClient<IReceiptSignatureClient, ReceiptSignatureClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Employee"]
        ?? "https+http://legacy-maliev-employee-service");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddServiceDiscovery().AddLegacyServiceAuthentication();
builder.Services.AddHttpClient<IInvoiceCreationDocumentClient, InvoiceCreationDocumentClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Document"] ?? "https+http://legacy-maliev-document-service");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddServiceDiscovery().AddLegacyServiceAuthentication();
builder.Services.AddHttpClient<IInvoiceCreationFileClient, ReceiptFileClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:File"] ?? "https+http://legacy-maliev-file-service");
    client.Timeout = TimeSpan.FromMinutes(5);
}).AddServiceDiscovery().AddLegacyServiceAuthentication();
builder.Services.AddHttpClient<IInvoiceCreationNotificationClient, ReceiptNotificationClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Notification"] ?? "https+http://legacy-maliev-notification-service");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddServiceDiscovery().AddLegacyServiceAuthentication();
builder.Services.AddHttpClient<IInvoiceQuotationCompletionClient, InvoiceQuotationCompletionClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:Quotation"] ?? "https+http://legacy-maliev-quotation-service");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddServiceDiscovery().AddLegacyServiceAuthentication();
AddInvoiceSourceClient(InvoiceCreationSourceClient.QuotationClient, "Services:Quotation", "https+http://legacy-maliev-quotation-service");
AddInvoiceSourceClient(InvoiceCreationSourceClient.CustomerClient, "Services:Customer", "https+http://legacy-maliev-customer-service");
AddInvoiceSourceClient(InvoiceCreationSourceClient.EmployeeClient, "Services:Employee", "https+http://legacy-maliev-employee-service");
AddInvoiceSourceClient(InvoiceCreationSourceClient.CatalogClient, "Services:Catalog", "https+http://legacy-maliev-catalog-service");

var app = builder.Build();
app.UseStandardMiddleware();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints("accounting");
app.MapControllers();
app.MapApiDocumentation(servicePrefix: "accounting");
await app.RunAsync();

void AddInvoiceSourceClient(string name, string configurationKey, string fallback)
{
    builder.Services.AddHttpClient(name, client =>
    {
        client.BaseAddress = new Uri(builder.Configuration[configurationKey] ?? fallback);
        client.Timeout = TimeSpan.FromSeconds(30);
    }).AddServiceDiscovery().AddLegacyServiceAuthentication();
}

public partial class Program;

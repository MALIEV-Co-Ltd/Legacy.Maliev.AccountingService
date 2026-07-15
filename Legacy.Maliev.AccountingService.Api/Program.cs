using System.Text.Json.Serialization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Data;
using Maliev.Aspire.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddDefaultApiVersioning();
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

var app = builder.Build();
app.UseStandardMiddleware();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints("accounting");
app.MapControllers();
app.MapApiDocumentation(servicePrefix: "accounting");
await app.RunAsync();

public partial class Program;

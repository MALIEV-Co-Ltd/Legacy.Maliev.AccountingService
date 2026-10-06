using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Maliev.Aspire.ServiceDefaults.IAM;
using Maliev.Aspire.ServiceDefaults.LegacyAuth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Npgsql;
using Polly.Timeout;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>Actual normal Program/registered provider/HTTP with owned PostgreSQL and controlled source/transport; not real Auth mint acceptance.</summary>
[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class InvoiceEmployeePreparationErrorHttpTests(AccountingBoundaryHttpFixture fixture)
{
    [Theory]
    [InlineData("numeric-constraint")]
    [InlineData("lost-commit-ack")]
    [InlineData("transport-timeout")]
    public async Task TypedFailuresReturnOpaque503AndDuplicateCannotRecreateFinancials(string scenario)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var rsa = RSA.Create(2048);
        await using var invoice = fixture.InvoiceDatabase();
        await using var payment = fixture.Database();
        await using var receipt = fixture.ReceiptDatabase();
        var connections = new Dictionary<string, string>
        {
            ["InvoiceDbContext"] = invoice.Database.GetDbConnection().ConnectionString,
            ["PaymentDbContext"] = payment.Database.GetDbConnection().ConnectionString,
            ["ReceiptDbContext"] = receipt.Database.GetDbConnection().ConnectionString
        };
        var spy = new StoreObservation(scenario == "lost-commit-ack");
        using var factory = new Factory(rsa, connections, scenario, spy);
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.True(scope.ServiceProvider.GetRequiredService<InvoiceDbContext>().Database.CreateExecutionStrategy().RetriesOnFailure);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(rsa, false, Guid.Empty));
        var operation = Guid.NewGuid();
        var request = Request("ERROR-" + operation.ToString("N"));
        var invoicesBefore = await invoice.Invoices.AsNoTracking().CountAsync(budget.Token);
        var itemsBefore = await invoice.Items.AsNoTracking().CountAsync(budget.Token);
        using var first = Message(rsa, operation, request);
        using var failed = await client.SendAsync(first, budget.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        var body = await failed.Content.ReadAsStringAsync(budget.Token);
        Assert.DoesNotContain("22003", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Synthetic", body, StringComparison.Ordinal);
        Assert.DoesNotContain("numeric", body, StringComparison.OrdinalIgnoreCase);
        var admission = await invoice.InvoiceCreationAdmissions.AsNoTracking().SingleAsync(value => value.OperationId == operation, budget.Token);
        Assert.Equal("NeedsReconciliation", admission.State);
        Assert.Null(admission.EmployeeCompletionJson);
        Assert.Null(admission.FinancialResultJson);
        if (scenario == "numeric-constraint")
        {
            Assert.Equal(typeof(DbUpdateException), spy.WrapperType);
            Assert.Equal("22003", spy.SqlState);
            Assert.Equal(1, spy.FailedSaves);
        }
        if (scenario == "lost-commit-ack")
        {
            Assert.True(spy.LostCommitAck);
            Assert.NotNull(admission.FinancialOwnershipJson);
            Assert.Equal(invoicesBefore + 1, await invoice.Invoices.AsNoTracking().CountAsync(budget.Token));
            Assert.Equal(itemsBefore + 1, await invoice.Items.AsNoTracking().CountAsync(budget.Token));
        }
        else
        {
            Assert.Null(admission.FinancialOwnershipJson);
            Assert.Equal(invoicesBefore, await invoice.Invoices.AsNoTracking().CountAsync(budget.Token));
            Assert.Equal(itemsBefore, await invoice.Items.AsNoTracking().CountAsync(budget.Token));
        }
        var transportCalls = factory.TransportCalls;
        using var duplicate = Message(rsa, operation, request);
        using var replay = await client.SendAsync(duplicate, budget.Token);
        Assert.Equal(scenario == "lost-commit-ack" ? HttpStatusCode.OK : HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal(transportCalls, factory.TransportCalls);
        Assert.Equal(invoicesBefore + (scenario == "lost-commit-ack" ? 1 : 0), await invoice.Invoices.AsNoTracking().CountAsync(budget.Token));
        Assert.Equal(itemsBefore + (scenario == "lost-commit-ack" ? 1 : 0), await invoice.Items.AsNoTracking().CountAsync(budget.Token));
    }

    private static HttpRequestMessage Message(RSA rsa, Guid operation, CreateInvoiceFromQuotationRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/invoices/from-quotation/84/prepare") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", operation.ToString("D"));
        message.Headers.Add(InvoiceCreationDelegationVerifier.HeaderName, "Bearer " + Token(rsa, true, operation));
        return message;
    }
    private const string Issuer = "https://employee-error-proof.invalid";
    private const string Audience = "employee-error-accounting";
    private static string Token(RSA rsa, bool delegated, Guid operation)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim> { new("sub", delegated ? "employee:42" : "service:legacy-intranet"), new("jti", Guid.NewGuid().ToString("D")),
            new("iat", new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64) };
        if (delegated) claims.AddRange([new("azp", "service:legacy-intranet"), new("scope", InvoiceCreationDelegationVerifier.Scope),
            new("quotation_id", "84"), new("operation_id", operation.ToString("D"))]);
        else claims.AddRange([new("identity_kind", "service"), new("permissions", AccountingPermissions.Create)]);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(Issuer, delegated ? InvoiceCreationDelegationVerifier.Audience : Audience,
            claims, now, now.AddSeconds(120), new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)));
    }
    private static CreateInvoiceFromQuotationRequest Request(string number) => new(number, null, null, null, null, null, null,
        new(null, null, null, null, null, null, null, null, null), new(null, null, null, null, null, null, null, null, null), null, null, false, false);
    private static InvoiceCreationSourceSnapshot Source(decimal total) => new(new(84, 42, 7, 1, 12m, 0m, total, null, null, null, null, null, null,
        ModifiedDate: new DateTime(2026, 10, 6, 1, 2, 3, DateTimeKind.Unspecified)),
        new(42, "Synthetic customer", "synthetic@example.invalid", null, null, null, null, null, null), new(7, "Synthetic employee"),
        new(1, "THB", "Thai baht"), [new(1, 84, 51, "Synthetic owned item", 3, 4m, 12m)]);

    private sealed class Factory(RSA rsa, Dictionary<string, string> connections, string scenario, StoreObservation spy) : WebApplicationFactory<Program>
    {
        public int TransportCalls;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Jwt:Issuer", Issuer);
            builder.UseSetting("Jwt:Audience", Audience);
            builder.UseSetting("Jwt:PublicKey", Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())));
            builder.UseSetting("Cache:RedisEnabled", "false");
            builder.UseSetting("Cache:AllowInMemoryFallback", "true");
            builder.UseSetting("Services:Quotation", "http://quotation.invalid/");
            foreach (var (name, connection) in connections) builder.UseSetting($"ConnectionStrings:{name}", connection);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                var iam = new Mock<IIamServiceClient>(MockBehavior.Strict);
                iam.Setup(value => value.CheckPermissionLiveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .Returns((string subject, string permission, string? _, CancellationToken _) => Task.FromResult(subject == "service:legacy-intranet" && permission == AccountingPermissions.Create));
                services.RemoveAll<IIamServiceClient>(); services.AddSingleton(iam.Object);
                services.RemoveAll<ILegacyServiceAccessTokenProvider>(); services.AddSingleton<ILegacyServiceAccessTokenProvider, WorkloadToken>();
                services.AddDbContext<InvoiceDbContext>((_, options) => options.AddInterceptors(spy, new CommitObservation(spy)));
                if (scenario != "transport-timeout")
                {
                    var source = new Mock<IInvoiceCreationSource>(MockBehavior.Strict);
                    source.Setup(value => value.GetAsync(84, It.IsAny<CancellationToken>())).ReturnsAsync(Source(scenario == "numeric-constraint" ? 100000000000000000000m : 12m));
                    services.RemoveAll<IInvoiceCreationSource>(); services.AddSingleton(source.Object);
                }
                services.PostConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(handler => handler.PrimaryHandler = new TimeoutTransport(this)));
            });
        }
    }
    private sealed class WorkloadToken : ILegacyServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>("synthetic-workload");
        public void Invalidate(string token) { }
    }
    private sealed class TimeoutTransport(Factory factory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factory.TransportCalls);
            return Task.FromException<HttpResponseMessage>(new TimeoutRejectedException("Synthetic classified transport timeout"));
        }
    }
    private sealed class StoreObservation(bool loseCommitAck) : SaveChangesInterceptor
    {
        public bool LoseCommitAck { get; } = loseCommitAck;
        public Guid WriteContext;
        public bool LostCommitAck;
        public Type? WrapperType;
        public string? SqlState;
        public int FailedSaves;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context;
            if (context is not null && context.ChangeTracker.Entries<Invoice>().Any()) WriteContext = context.ContextId.InstanceId;
            return ValueTask.FromResult(result);
        }
        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            FailedSaves++;
            WrapperType = eventData.Exception.GetType();
            SqlState = (eventData.Exception.InnerException as PostgresException)?.SqlState;
            return Task.CompletedTask;
        }
    }
    private sealed class CommitObservation(StoreObservation spy) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (spy.LoseCommitAck && !spy.LostCommitAck && eventData.Context?.ContextId.InstanceId == spy.WriteContext)
            {
                spy.LostCommitAck = true;
                throw new NpgsqlException("Synthetic lost committed acknowledgment", new IOException());
            }
            return Task.CompletedTask;
        }
    }
}

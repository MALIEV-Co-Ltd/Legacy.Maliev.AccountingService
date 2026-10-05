extern alias accounting_api;
extern alias quotation_api;
using AccountingProgram = accounting_api::Program;
using QuotationProgram = quotation_api::Program;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Commerce.JoinedAuth.Tests;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.QuotationService.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Commerce.AtomicProtocol.Tests;

[CollectionDefinition("atomic-protocol", DisableParallelization = true)]
public sealed class AtomicProtocolCollection : ICollectionFixture<AtomicProtocolSharedFixture>
{
}

/// <summary>Owns one real host pair for the serialized prospective collection.</summary>
public sealed class AtomicProtocolSharedFixture : IAsyncLifetime
{
    public AccountingQuotationBaselineFixture Base { get; } = new();
    public AtomicProtocolScenario Scenario { get; private set; } = null!;
    private int disposed;

    public async Task InitializeAsync()
    {
        try
        {
            await Base.InitializeAsync();
            Scenario = new(Base);
            using var bootstrap = Scenario.Accounting.CreateClient();
        }
        catch { await DisposeAsync(); throw; }
    }

    public async Task DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { if (Scenario is not null) await Scenario.DisposeHostsAsync(); }
        finally { await Base.DisposeAsync(); }
    }
}

/// <summary>Transport-only prospective wrapper; historical fixtures and production registration remain unchanged.</summary>
public sealed class AtomicProtocolScenario : IAsyncDisposable
{
    public sealed record Decision(string Path, JsonElement Body, string Key, string Version, int Status);
    public sealed record OrderCall(int Id, string Key, int Status);
    public sealed record AuthLogin(string Host, int Status);
    public AccountingQuotationBaselineFixture Base { get; }
    public WebApplicationFactory<AccountingProgram> Accounting { get; }
    public WebApplicationFactory<QuotationProgram> Quotation { get; }
    public ConcurrentQueue<Decision> Decisions { get; } = new();
    public ConcurrentQueue<string> Requests { get; } = new();
    public ConcurrentQueue<OrderCall> Orders { get; } = new();
    public ConcurrentQueue<AuthLogin> AuthLogins { get; } = new();
    public Func<Task>? BeforeCompletionLookup;
    public Func<CancellationToken, Task>? BeforeDecisionDispatch;
    public bool LoseDecisionResponse;
    public string? CorruptCompletionLookup;
    public string? AccountingIamFailure;
    public int FailedOrder;
    public int LaterEffects;
    public int IamControls;
    public int Unmatched;
    private readonly QuotationNormalIamBoundary boundary = new();
    private readonly Dictionary<int, string> sourceFinancialScalars = new();
    private int lost;
    private int hostsDisposed;

    public AtomicProtocolScenario(AccountingQuotationBaselineFixture fixture)
    {
        Base = fixture;
        Quotation = fixture.Quotation.App(boundary, environment: "Testing").WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.PostConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(http => http.PrimaryHandler = new OrderTransport(this, http.PrimaryHandler)))));
        try
        {
            using var bootstrap = Quotation.CreateClient();
            Accounting = fixture.Accounting.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services => services.PostConfigureAll<HttpClientFactoryOptions>(options =>
                    options.HttpMessageHandlerBuilderActions.Add(http => http.PrimaryHandler = new AccountingTransport(this,
                        http.Name ?? throw new InvalidOperationException("Missing named HTTP client."), http.PrimaryHandler)))));
        }
        catch { Quotation.Dispose(); throw; }
    }

    public void Reset()
    {
        Base.ResetObservations();
        while (Decisions.TryDequeue(out _)) { }
        while (Requests.TryDequeue(out _)) { }
        while (Orders.TryDequeue(out _)) { }
        while (AuthLogins.TryDequeue(out _)) { }
        while (boundary.IamResponses.TryDequeue(out _)) { }
        BeforeCompletionLookup = null;
        BeforeDecisionDispatch = null;
        LoseDecisionResponse = false;
        CorruptCompletionLookup = null;
        AccountingIamFailure = null;
        FailedOrder = 0;
        LaterEffects = 0;
        IamControls = 0;
        Unmatched = 0;
        lost = 0;
        boundary.SuccessfulStandardResponses = 0;
        boundary.SuccessfulLiveResponses = 0;
        boundary.IamTransportFailures = 0;
        boundary.UnmatchedTransportCalls = 0;
        boundary.IamCalls = 0;
        boundary.LoginCalls = 0;
        sourceFinancialScalars.Clear();
    }

    public async Task<Quotation> SeedAsync(bool? accepted = null)
    {
        var row = await Base.Quotation.SeedAsync(accepted);
        await using var db = Base.Quotation.Context();
        db.OrderItems.Add(new QuotationOrderItem { QuotationId = row.Id, Description = "Synthetic Thai part", Quantity = 1, UnitPrice = 100m });
        await db.SaveChangesAsync();
        var persisted = await db.Quotations.AsNoTracking().SingleAsync(value => value.Id == row.Id);
        sourceFinancialScalars.Add(persisted.Id, FinancialScalars(persisted));
        return persisted;
    }

    public async Task<int> InvoiceAsync()
    {
        await using var db = Base.InvoiceDatabase();
        var row = new Legacy.Maliev.AccountingService.Domain.Invoice.Invoice
        { Number = $"ATOMIC-{Guid.NewGuid():N}", CustomerId = 42, Currency = "THB", Total = 107m };
        db.Invoices.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    public async Task CompleteAsync(int quotation, int invoice, Guid operation, DateTime? version)
    {
        using var bootstrap = Accounting.CreateClient();
        using var scope = Accounting.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IInvoiceQuotationCompletionClient>()
            .CompleteAsync(quotation, invoice, operation, version, CancellationToken.None);
    }

    public async Task<HttpResponseMessage> CreateAsync(int quotation, Guid operation, string number, bool delegated = false)
    {
        using var client = Accounting.CreateClient(new() { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/invoices/from-quotation/{quotation}");
        request.Headers.Authorization = new("Bearer", Base.Auth.IntranetToken);
        request.Headers.Add("Idempotency-Key", operation.ToString("D"));
        if (delegated)
        {
            var issued = await Base.Auth.ExchangeAsync(quotation, operation);
            Assert.Equal(HttpStatusCode.OK, issued.Status);
            Assert.NotNull(issued.Token);
            request.Headers.Add("X-Maliev-Employee-Delegation", "Bearer " + issued.Token);
        }
        request.Content = JsonContent.Create(new
        {
            InvoiceNumber = number,
            SendEmail = false,
            BillingAddress = new { Recipient = "Synthetic Thai customer", Line1 = "Synthetic address", Country = "Thailand" },
            ShippingAddress = new { Recipient = "Synthetic Thai customer", Line1 = "Synthetic address", Country = "Thailand" }
        });
        return await client.SendAsync(request);
    }

    public async Task AssertAcceptedAsync(int quotation, int invoice)
    {
        await using var db = Base.Quotation.Context();
        var row = await db.Quotations.AsNoTracking().SingleAsync(value => value.Id == quotation);
        Assert.True(invoice > 0);
        Assert.True(row.Accepted);
        Assert.Equal(invoice, row.InvoiceId);
        Assert.Equal("customer", row.AcceptanceOrigin);
        Assert.NotNull(row.AcceptedUtc);
        Assert.Equal(sourceFinancialScalars[quotation], FinancialScalars(row));
        var outcome = Assert.Single(await db.AcceptedOutcomes.Where(value => value.QuotationId == quotation).ToArrayAsync());
        Assert.Equal("customer", outcome.AcceptanceOrigin);
        Assert.Equal(row.AcceptedUtc.GetValueOrDefault(), outcome.AcceptedUtc.AddTicks(outcome.AcceptedUtcSubMicrosecondTicks));
        Assert.Empty(await db.GoogleAnalyticsOutbox.Where(value => value.QuotationId == quotation).ToArrayAsync());
        Assert.Equal(0, Unmatched);
        Assert.Equal(0, Base.ExternalProviderCalls);
        Assert.Equal(0, boundary.IamTransportFailures);
        Assert.Equal(0, boundary.UnmatchedTransportCalls);
        var checks = boundary.IamResponses.Where(value => value.Resource == $"/quotations/{quotation}").ToArray();
        Assert.Contains(checks, value => !value.Live && value.Status == 200 && value.Allowed);
        Assert.Contains(checks, value => value.Live && value.Status == 200 && value.Allowed);
        Assert.All(checks, value => { Assert.Equal(200, value.Status); Assert.True(value.Allowed); });
    }

    public void AssertWire(int quotation, int invoice, Guid operation, DateTime version)
    {
        var decision = Assert.Single(Decisions, value => value.Path == $"/quotations/{quotation}/decision");
        Assert.Equal(new[] { "Accepted", "EmployeeInitiated", "InvoiceId" }, decision.Body.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
        Assert.True(decision.Body.GetProperty("Accepted").GetBoolean());
        Assert.False(decision.Body.GetProperty("EmployeeInitiated").GetBoolean());
        Assert.Equal(invoice, decision.Body.GetProperty("InvoiceId").GetInt32());
        Assert.Equal(operation.ToString("D"), decision.Key);
        Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(version, DateTimeKind.Utc)).ToString("O", System.Globalization.CultureInfo.InvariantCulture), decision.Version);
        Assert.DoesNotContain(Requests, value => value == $"PUT /quotations/{quotation}");
    }

    // Per-case cleanup retains status receipts for xUnit diagnostics; collection owns hosts.
    public ValueTask DisposeAsync()
    {
        BeforeCompletionLookup = null;
        BeforeDecisionDispatch = null;
        LoseDecisionResponse = false;
        CorruptCompletionLookup = null;
        AccountingIamFailure = null;
        FailedOrder = 0;
        lost = 0;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeHostsAsync()
    {
        if (Interlocked.Exchange(ref hostsDisposed, 1) != 0) return;
        try { await Accounting.DisposeAsync(); }
        finally { await Quotation.DisposeAsync(); }
    }

    private sealed class AccountingTransport(AtomicProtocolScenario owner, string name, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly HttpMessageInvoker quotations = new(owner.Quotation.Server.CreateHandler());
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing prospective URI.");
            if (uri.Host == "joined-quotation.invalid")
            {
                Assert.Equal("https", uri.Scheme);
                owner.Requests.Enqueue(request.Method + " " + uri.AbsolutePath);
                var completionLookup = request.Method == HttpMethod.Get && !uri.AbsolutePath.EndsWith("/orderitems", StringComparison.Ordinal)
                    && name.Contains(nameof(IInvoiceQuotationCompletionClient), StringComparison.Ordinal);
                if (completionLookup && Interlocked.Exchange(ref owner.BeforeCompletionLookup, null) is { } hook) await hook();
                if (request.Method == HttpMethod.Put && uri.AbsolutePath.EndsWith("/decision", StringComparison.Ordinal)
                    && owner.BeforeDecisionDispatch is { } dispatch) await dispatch(token);
                var response = await quotations.SendAsync(request, token);
                if (completionLookup && owner.CorruptCompletionLookup is { } corrupt)
                {
                    response.Content.Dispose();
                    response.Content = new StringContent(corrupt, Encoding.UTF8, "application/json");
                }
                if (request.Method == HttpMethod.Put && uri.AbsolutePath.EndsWith("/decision", StringComparison.Ordinal))
                {
                    using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                    owner.Decisions.Enqueue(new(uri.AbsolutePath, json.RootElement.Clone(),
                        Assert.Single(request.Headers.GetValues("Idempotency-Key")),
                        Assert.Single(request.Headers.GetValues("X-Expected-Modified-Date")), (int)response.StatusCode));
                    if (owner.LoseDecisionResponse && Interlocked.Exchange(ref owner.lost, 1) == 0)
                    {
                        response.Dispose();
                        throw new HttpRequestException("Synthetic decision acknowledgment loss after producer response.");
                    }
                }
                return response;
            }
            if (uri.Host == "joined-iam.invalid" && owner.AccountingIamFailure is { } mode)
            {
                Assert.Equal("https", uri.Scheme);
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/iam/v1/auth/check-permission", uri.AbsolutePath);
                Assert.Equal("synthetic-joined-live-check", Assert.Single(request.Headers.GetValues("X-Maliev-IAM-Live-Check-Key")));
                Assert.Equal("service:legacy-accounting", new JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter).Subject);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal("service:legacy-intranet", body.RootElement.GetProperty("principalId").GetString());
                Assert.Equal("legacy.accounting.create", body.RootElement.GetProperty("permissionId").GetString());
                Assert.True(body.RootElement.GetProperty("bypassCache").GetBoolean());
                Interlocked.Increment(ref owner.IamControls);
                return Json(new { allowed = false }, mode == "denied" ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable);
            }
            if (uri.Host == "joined-auth.invalid" && uri.AbsolutePath == "/auth/v1/service/login" && request.Method == HttpMethod.Post)
            {
                var response = await base.SendAsync(request, token);
                owner.AuthLogins.Enqueue(new(uri.Host, (int)response.StatusCode));
                return response;
            }
            if (uri.Host == "joined-document.invalid" && request.Method == HttpMethod.Post && uri.AbsolutePath == "/pdfs/invoice")
            {
                AssertWorkload(request, "service:legacy-accounting");
                Interlocked.Increment(ref owner.LaterEffects);
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            }
            if (uri.Host == "joined-file.invalid" && request.Method == HttpMethod.Get && uri.AbsolutePath == "/uploads/SignedUrl")
            {
                AssertWorkload(request, "service:legacy-accounting");
                Interlocked.Increment(ref owner.LaterEffects);
                return Json(new { }); // ExistsAsync inspects status only; no signed URL or download is fabricated.
            }
            return await base.SendAsync(request, token); // Historical strict catch-all remains the final transport.
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) quotations.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class OrderTransport(AtomicProtocolScenario owner, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri;
            if (uri is null || uri.Host != "quotation95-order.invalid")
            {
                var response = await base.SendAsync(request, token);
                if (uri?.Host == "quotation95-auth.invalid" && uri.AbsolutePath == "/auth/v1/service/login" && request.Method == HttpMethod.Post)
                    owner.AuthLogins.Enqueue(new(uri.Host, (int)response.StatusCode));
                return response;
            }
            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (request.Method != HttpMethod.Post || parts.Length != 4 || parts[0] != "orderstatuses"
                || parts[1] != "histories" || parts[3] != "accepted" || !int.TryParse(parts[2], out var order))
            {
                Interlocked.Increment(ref owner.Unmatched);
                throw new InvalidOperationException("Unapproved controlled Order route.");
            }
            Assert.Equal("service:legacy-quotation", new JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter).Subject);
            var status = order == owner.FailedOrder ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Created;
            owner.Orders.Enqueue(new(order, Assert.Single(request.Headers.GetValues("Idempotency-Key")), (int)status));
            return Json(new { }, status);
        }
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private static string FinancialScalars(Quotation row)
        => JsonSerializer.Serialize(new
        {
            row.CustomerId,
            row.EmployeeId,
            row.Period,
            row.ExpirationDate,
            row.Subtotal,
            row.Vat,
            row.Total,
            row.WithholdingTax,
            row.QuotedAmount,
            row.CurrencyId,
            row.Fob,
            row.ShippedVia,
            row.Terms,
            row.SourceRequestId,
            row.SourceJourneyId,
            row.CreatedDate
        });

    private static void AssertWorkload(HttpRequestMessage request, string subject)
    {
        Assert.Equal("https", request.RequestUri!.Scheme);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(request.Headers.Authorization!.Parameter);
        Assert.Equal(subject, jwt.Subject);
        Assert.Equal("https://quotation95-auth.invalid", jwt.Issuer);
    }
}

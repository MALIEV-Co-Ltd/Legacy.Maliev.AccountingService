using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Api.Controllers.Invoice;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Application.Services;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Domain.Receipt;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace Legacy.Maliev.AccountingService.Tests;

// Real workflow/controller/store/admission boundary with strict, inactive external clients.
public sealed class MasterStringSourceStoreTests(InvoiceNotificationPhaseFencePostgresFixture fixture)
    : IClassFixture<InvoiceNotificationPhaseFencePostgresFixture>
{
    [Theory]
    [InlineData("long-company")]
    [InlineData("long-number")]
    [InlineData("long-currency")]
    [InlineData("long-address")]
    public async Task InvalidInvoiceAfterAdmissionRollsBackFinancesAndSameKeyFailsClosed(string variant)
    {
        await using var database = await fixture.NewDatabaseAsync();
        using var rsa = RSA.Create(2048);
        var operation = Guid.NewGuid();
        var request = Request();
        if (variant == "long-number") request = request with { InvoiceNumber = new string('ก', 101) };
        if (variant == "long-address") request = request with { BillingAddress = request.BillingAddress with { Line1 = new string('ก', 257) } };
        if (variant == "long-company") request = request with { BillingAddress = request.BillingAddress with { Company = new string('ก', 257) } };
        var snapshot = new InvoiceCreationSourceSnapshot(
            new(84, 42, 7, 1, 100m, 7m, 107m, null, null, null, null, null, null,
                ModifiedDate: new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Unspecified)),
            new(42, "Synthetic customer", "synthetic@example.invalid", null, null, null, null, null, null),
            new(7, "Synthetic employee"),
            new(1, variant == "long-currency" ? string.Concat(Enumerable.Repeat("😀", 26)) : "THB", "Synthetic currency"),
            [new(1, 84, 51, "Synthetic item", 1, 100m, 100m)]);
        var source = new Mock<IInvoiceCreationSource>(MockBehavior.Strict);
        source.Setup(value => value.GetAsync(84, It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
        var quotation = new Mock<IInvoiceQuotationCompletionClient>(MockBehavior.Strict);
        var documents = new Mock<IInvoiceCreationDocumentClient>(MockBehavior.Strict);
        var files = new Mock<IInvoiceCreationFileClient>(MockBehavior.Strict);
        var notifications = new Mock<IInvoiceCreationNotificationClient>(MockBehavior.Strict);
        var journal = new Mock<IInvoiceCreationJournal>(MockBehavior.Strict);
        journal.Setup(value => value.GetAsync("create:84", operation, It.IsAny<CancellationToken>())).ReturnsAsync((InvoiceCreationResult?)null);
        var workflow = new InvoiceCreationWorkflowService(source.Object, new InvoiceCreationStore(database, TimeProvider.System),
            quotation.Object, documents.Object, files.Object, notifications.Object, journal.Object, new NoopLock(), TimeProvider.System);
        async Task<ActionResult<InvoiceCreationResult>> Call()
        {
            await using var fresh = new InvoiceDbContext(new DbContextOptionsBuilder<InvoiceDbContext>()
                .UseNpgsql(database.Database.GetConnectionString()).Options);
            var verifier = new InvoiceCreationDelegationVerifier(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:Issuer"] = "legacy-auth",
                    ["Jwt:PublicKey"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())),
                }).Build(), TimeProvider.System);
            var controller = new InvoiceCreationController(workflow, verifier, new InvoiceCreationAdmissionStore(fresh))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("sub", InvoiceCreationDelegationVerifier.IntranetSubject), new Claim("identity_kind", "service"),
            ], "test"));
            controller.HttpContext.Request.Headers[InvoiceCreationDelegationVerifier.HeaderName] = "Bearer " + Delegation(rsa, operation);
            return await controller.CreateAsync(84, request, operation.ToString("D"), CancellationToken.None);
        }
        var failure = Assert.IsType<BadRequestObjectResult>((await Call()).Result);
        var problem = Assert.IsType<ObjectResult>(failure.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        var details = Assert.IsType<ProblemDetails>(problem.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, details.Status);
        Assert.Equal("The request is invalid. (Parameter 'item')", details.Title);
        var retained = await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync();
        Assert.Equal(operation, retained.OperationId);
        Assert.Equal("NeedsReconciliation", retained.State);
        Assert.Null(retained.ResultJson);
        Assert.Null(retained.FinancialResultJson);
        var retainedBefore = System.Text.Json.JsonSerializer.Serialize(retained);
        Assert.IsType<ConflictObjectResult>((await Call()).Result);
        Assert.Equal(retainedBefore, System.Text.Json.JsonSerializer.Serialize(await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync()));
        source.Verify(value => value.GetAsync(84, It.IsAny<CancellationToken>()), Times.Once);
        source.VerifyNoOtherCalls();
        quotation.VerifyNoOtherCalls();
        documents.VerifyNoOtherCalls();
        files.VerifyNoOtherCalls();
        notifications.VerifyNoOtherCalls();
        journal.Verify(value => value.GetAsync("create:84", operation, It.IsAny<CancellationToken>()), Times.Exactly(2));
        journal.VerifyNoOtherCalls();
        Assert.Empty(await database.Invoices.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Items.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Files.AsNoTracking().ToListAsync());
        Assert.Empty(await database.InvoiceNotificationCorrelations.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task RetainedOriginValidationPrecedesInvalidInvoiceStrings()
    {
        await using var database = await fixture.NewDatabaseAsync();
        var origin = new InvoiceNotificationOrigin("https://auth.example.invalid", "employee:42", "service:legacy-intranet");
        var operation = Guid.NewGuid();
        var admissions = new InvoiceCreationAdmissionStore(database);
        Assert.True((await admissions.AdmitAsync(operation, 84, origin, new string('A', 64), CancellationToken.None)).IsNew);
        var before = System.Text.Json.JsonSerializer.Serialize(await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync());
        var invalid = new Invoice { Number = null, CustomerId = 42 };
        var authority = new InvoiceFinancialCommitContext(operation, 84,
            origin with { EmployeeSubject = "employee:other" }, new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Unspecified));
        await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => new InvoiceCreationStore(database, TimeProvider.System)
            .CreateAsync(invalid, [], authority, CancellationToken.None));
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(await database.InvoiceCreationAdmissions.AsNoTracking().SingleAsync()));
        Assert.Empty(await database.Invoices.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Items.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Files.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task NullInvoiceCurrencyCannotCreateReceiptOrMutateInvoice()
    {
        await using var invoiceDatabase = await fixture.NewDatabaseAsync();
        await using var receiptDatabase = await ReceiptDatabaseAsync();
        var invoice = new Invoice { Number = "SOURCE-RECEIPT", Currency = null, CustomerId = 42 };
        invoiceDatabase.Invoices.Add(invoice);
        await invoiceDatabase.SaveChangesAsync();
        var before = System.Text.Json.JsonSerializer.Serialize(await invoiceDatabase.Invoices.AsNoTracking().SingleAsync());
        var cache = new Mock<IAccountingCache>(MockBehavior.Strict);
        var store = new ReceiptWorkflowStore(invoiceDatabase, receiptDatabase, TimeProvider.System, cache.Object);
        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateReceiptAsync(invoice,
            [new InvoiceOrderItem { Description = "Synthetic item", Quantity = 1, UnitPrice = 1m }], null, CancellationToken.None));
        Assert.Empty(await receiptDatabase.Receipts.AsNoTracking().ToListAsync());
        Assert.Empty(await receiptDatabase.Items.AsNoTracking().ToListAsync());
        Assert.Empty(await receiptDatabase.Files.AsNoTracking().ToListAsync());
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(await invoiceDatabase.Invoices.AsNoTracking().SingleAsync()));
        cache.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ReceiptRequiredCurrencyRetainsEmptyAndPaddedLiteral(string currency)
    {
        await using var invoiceDatabase = await fixture.NewDatabaseAsync();
        await using var receiptDatabase = await ReceiptDatabaseAsync();
        var cache = new Mock<IAccountingCache>(MockBehavior.Strict);
        var store = new ReceiptWorkflowStore(invoiceDatabase, receiptDatabase, TimeProvider.System, cache.Object);
        var invoice = new Invoice { Number = "SOURCE-RECEIPT", Currency = currency, CustomerId = 42 };
        var result = await store.CreateReceiptAsync(invoice, [], null, CancellationToken.None);
        Assert.Equal(currency, result.Currency);
        Assert.Equal(currency, (await receiptDatabase.Receipts.AsNoTracking().SingleAsync()).Currency);
        // An existing receipt replay remains authoritative even if the supplied invoice is invalid.
        invoice.Currency = null;
        Assert.Equal(result.Id, (await store.CreateReceiptAsync(invoice, [], null, CancellationToken.None)).Id);
        Assert.Single(await receiptDatabase.Receipts.AsNoTracking().ToListAsync());
        cache.VerifyNoOtherCalls();
    }

    private async Task<ReceiptDbContext> ReceiptDatabaseAsync()
    {
        await using var empty = await fixture.NewDatabaseAsync("0");
        var database = new ReceiptDbContext(new DbContextOptionsBuilder<ReceiptDbContext>().UseNpgsql(empty.Database.GetConnectionString()).Options);
        try
        {
            database.Database.SetCommandTimeout(40);
            using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await database.Database.MigrateAsync(lifetime.Token);
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    private static string Delegation(RSA rsa, Guid operation)
    {
        var now = DateTimeOffset.UtcNow;
        var token = new JwtSecurityToken("legacy-auth", InvoiceCreationDelegationVerifier.Audience,
        [
            new(JwtRegisteredClaimNames.Sub, "employee:42"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("azp", InvoiceCreationDelegationVerifier.IntranetSubject), new("scope", InvoiceCreationDelegationVerifier.Scope),
            new("quotation_id", "84"), new("operation_id", operation.ToString("D")),
        ], now.UtcDateTime, now.AddSeconds(120).UtcDateTime,
        new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static CreateInvoiceFromQuotationRequest Request() => new("SOURCE-STORE", null, null, null, null, null, null,
        new(null, null, null, null, null, null, null, null, null),
        new(null, null, null, null, null, null, null, null, null), null, null, false, false);

    private sealed class NoopLock : IInvoiceCreationLock
    {
        public ValueTask<IAsyncDisposable> AcquireAsync(int quotationId, CancellationToken cancellationToken) => ValueTask.FromResult<IAsyncDisposable>(new Lease());
        private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
}

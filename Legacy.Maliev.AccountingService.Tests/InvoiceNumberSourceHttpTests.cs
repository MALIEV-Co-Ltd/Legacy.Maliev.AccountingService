using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class InvoiceNumberSourceHttpTests(AccountingBoundaryHttpFixture fixture)
    : IClassFixture<AccountingBoundaryHttpFixture>
{
    private const string Thai = "\u0e0a\u0e34\u0e49\u0e19\u0e07\u0e32\u0e19";

    [Fact]
    public async Task ExactNumber_BeyondEarlierSubstringRows_IsFoundWithoutPagingOrCacheMutation()
    {
        await SeedAsync(Row(11, "prefix-INV-EXACT"), Row(22, "INV-EXACT-suffix"), Row(33, "INV-EXACT"));
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        await AssertInvoiceAsync(client, "INV-EXACT", 33, "INV-EXACT");
        await AssertInvoiceAsync(client, "INV-EXACT", 33, "INV-EXACT");
        Assert.Equal(before, await SnapshotAsync());
        Assert.Contains(fixture.LiveChecks, check => check.Permission == AccountingPermissions.Read);
    }

    [Theory]
    [InlineData("INV-MISSING")]
    [InlineData("purchase-only")]
    [InlineData("part")]
    public async Task MissingWholeNumber_SubstringAndPurchaseOrderMatchesDoNotSubstitute(string requested)
    {
        var purchase = Row(33, "other");
        purchase.PurchaseOrderNumber = "purchase-only";
        await SeedAsync(Row(11, "prefix-part"), Row(22, "part-suffix"), purchase);
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        using var response = await client.GetAsync("/invoices/" + Uri.EscapeDataString(requested));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 2)]
    public async Task DuplicateWholeNumber_BeyondEarlierSubstringRows_RefusesOpaqueWithoutMutation(bool varyCase, int count)
    {
        var rows = new List<Invoice> { Row(11, "prefix-DUPLICATE"), Row(22, "DUPLICATE-suffix") };
        rows.AddRange(Enumerable.Range(0, count).Select(index =>
            Row(33 + index, varyCase && index > 0 ? "duplicate" : "DUPLICATE")));
        await SeedAsync(rows.ToArray());
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        using var response = await client.GetAsync("/invoices/DUPLICATE");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var error = JsonNode.Parse(body)!.AsObject();
        Assert.Equal("The request cannot be processed.", error["error"]!.GetValue<string>());
        Assert.Equal(400, error["statusCode"]!.GetValue<int>());
        Assert.Null(error["details"]);
        Assert.DoesNotContain("DUPLICATE", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.Contains(fixture.FailureMetadata, value => value.Contains("InvalidOperationException", StringComparison.Ordinal));
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("case-number")]
    [InlineData("CASE-NUMBER")]
    public async Task FiniteCaseAdaptation_PreservesExistingPostgresCaseInsensitiveRead(string requested)
    {
        await SeedAsync(Row(11, "Case-Number"));
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        await AssertInvoiceAsync(client, requested, 11, "Case-Number");
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("INV%part")]
    [InlineData("INV_part")]
    [InlineData("INV\\part")]
    [InlineData(Thai)]
    [InlineData(" INV padded ")]
    public async Task WholeNumber_KeepsLiteralCharactersThaiAndSignificantPadding(string requested)
    {
        await SeedAsync(Row(11, requested), Row(22, "INVXpart"), Row(33, "INV padded"));
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        await AssertInvoiceAsync(client, requested, 11, requested);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("1101")]
    [InlineData("+1101")]
    public async Task NumericRoute_PreservesIdentifierPrecedenceOverAnotherInvoicesNumber(string requested)
    {
        await SeedAsync(Row(1101, "ID-FIRST"), Row(2202, requested));
        // Existing numeric GetAsync is allowed to prime its configured ID cache.
        var before = await SnapshotAsync(observeCache: false);
        using var client = fixture.Client([AccountingPermissions.Read], summaryClockHost: true);
        await AssertInvoiceAsync(client, requested, 1101, "ID-FIRST");
        Assert.Equal(before, await SnapshotAsync(observeCache: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnonymousOrLiveDenied_NumberReadCannotDiscloseOrMutate(bool anonymous)
    {
        await SeedAsync(Row(11, "PRIVATE-NUMBER"));
        var before = await SnapshotAsync();
        using var client = fixture.Client(anonymous ? null : [AccountingPermissions.Read], allowLive: false, summaryClockHost: true);
        using var response = await client.GetAsync("/invoices/PRIVATE-NUMBER");
        Assert.Equal(anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("PRIVATE-NUMBER", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync());
    }

    private async Task SeedAsync(params Invoice[] rows)
    {
        await fixture.ResetAsync();
        await using var database = fixture.InvoiceDatabase();
        await database.Files.ExecuteDeleteAsync();
        await database.Items.ExecuteDeleteAsync();
        await database.Invoices.ExecuteDeleteAsync();
        database.Invoices.AddRange(rows);
        await database.SaveChangesAsync();
    }

    private static Invoice Row(int id, string number) => new()
    {
        Id = id,
        Number = number,
        CustomerId = 71,
        Currency = "THB",
        Subtotal = 100m,
        Total = 107m,
        Vat = 7m,
        CreatedDate = new DateTime(2026, 10, 6),
        ModifiedDate = new DateTime(2026, 10, 6),
    };

    private static async Task AssertInvoiceAsync(HttpClient client, string requested, int expectedId, string expectedNumber)
    {
        using var response = await client.GetAsync("/invoices/" + Uri.EscapeDataString(requested));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedId, document.RootElement.GetProperty("Id").GetInt32());
        Assert.Equal(expectedNumber, document.RootElement.GetProperty("Number").GetString());
        Assert.False(document.RootElement.TryGetProperty("number", out _));
    }

    private async Task<(string Payment, string Invoice, string Receipt, string Cache)> SnapshotAsync(bool observeCache = true)
    {
        await using var payment = fixture.Database();
        await using var invoice = fixture.InvoiceDatabase();
        await using var receipt = fixture.ReceiptDatabase();
        return (JsonSerializer.Serialize(await payment.Payments.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()),
            JsonSerializer.Serialize(await invoice.Invoices.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()),
            JsonSerializer.Serialize(await receipt.Receipts.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()),
            observeCache ? await fixture.SummaryCacheSnapshotAsync() : "Existing numeric ID cache behavior excluded");
    }
}

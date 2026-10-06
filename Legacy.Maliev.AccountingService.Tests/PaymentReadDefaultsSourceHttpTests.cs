using System.Net;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using PaymentRecord = Legacy.Maliev.AccountingService.Domain.Payment.Payment;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class PaymentReadDefaultsSourceHttpTests(AccountingBoundaryHttpFixture fixture)
    : IClassFixture<AccountingBoundaryHttpFixture>
{
    [Theory]
    [InlineData("accounts")]
    [InlineData("directions")]
    [InlineData("methods")]
    [InlineData("types")]
    public async Task EmptyLookup_Source404AndNoFinancialMutation(string resource)
    {
        await SeedAsync(false);
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read]);
        using var response = await client.GetAsync($"/payments/{resource}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("[]", await response.Content.ReadAsStringAsync());
        Assert.Contains(fixture.LiveChecks, check => check.Permission == AccountingPermissions.Read);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData(7)]
    [InlineData(1234)]
    [InlineData(0)]
    public async Task EmptyFilesOrMissingParent_Source404WithOpaqueModernBody(int paymentId)
    {
        await SeedAsync(true, false);
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.FilesRead]);
        using var response = await client.GetAsync($"/payments/{paymentId}/files");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.NotEqual("[]", body);
        Assert.DoesNotContain("SYNTHETIC-PRIVATE", body, StringComparison.Ordinal);
        Assert.Contains(fixture.LiveChecks, check => check.Permission == AccountingPermissions.FilesRead);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("/payments/accounts", "Bank", false)]
    [InlineData("/payments/directions", "Name", false)]
    [InlineData("/payments/methods", "Name", false)]
    [InlineData("/payments/types", "Name", false)]
    [InlineData("/payments/7/files", "ObjectName", true)]
    public async Task PopulatedRead_KeepsPascalWireOwnedRowsAndNoMutation(string path, string field, bool files)
    {
        await SeedAsync(true);
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read, AccountingPermissions.FilesRead]);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(files ? new[] { 71, 72 } : new[] { 1, 2 }, rows.Select(row => row.GetProperty("Id").GetInt32()));
        Assert.All(rows, row =>
        {
            Assert.StartsWith("SYNTHETIC-PRIVATE", row.GetProperty(field).GetString(), StringComparison.Ordinal);
            Assert.False(row.TryGetProperty("id", out _));
            if (files) Assert.Equal(7, row.GetProperty("PaymentId").GetInt32());
        });
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("/payments/accounts", true)]
    [InlineData("/payments/accounts", false)]
    [InlineData("/payments/directions", true)]
    [InlineData("/payments/directions", false)]
    [InlineData("/payments/methods", true)]
    [InlineData("/payments/methods", false)]
    [InlineData("/payments/types", true)]
    [InlineData("/payments/types", false)]
    [InlineData("/payments/7/files", true)]
    [InlineData("/payments/7/files", false)]
    public async Task AnonymousOrLiveDenied_RefusesBeforeDisclosureWithoutMutation(string path, bool anonymous)
    {
        await SeedAsync(true);
        var before = await SnapshotAsync();
        using var client = fixture.Client(anonymous ? null : [AccountingPermissions.Read, AccountingPermissions.FilesRead], allowLive: false);
        using var response = await client.GetAsync(path);
        Assert.Equal(anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("SYNTHETIC-PRIVATE", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("/payments/methods", AccountingPermissions.Read)]
    [InlineData("/payments/7/files", AccountingPermissions.FilesRead)]
    public async Task SuccessfulRead_DoesNotAuthorizeFreshLiveDeniedIdentity(string path, string permission)
    {
        await SeedAsync(true);
        var before = await SnapshotAsync();
        using var allowed = fixture.Client([permission]);
        using var first = await allowed.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var checks = fixture.LiveChecks.Count;
        using var denied = fixture.Client([permission], allowLive: false);
        using var second = await denied.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
        Assert.True(fixture.LiveChecks.Count > checks);
        Assert.DoesNotContain("SYNTHETIC-PRIVATE", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync());
    }

    private async Task SeedAsync(bool populated, bool files = true)
    {
        await using var database = fixture.Database();
        await database.Files.ExecuteDeleteAsync();
        await database.Payments.ExecuteDeleteAsync();
        await database.Accounts.ExecuteDeleteAsync();
        await database.Directions.ExecuteDeleteAsync();
        await database.Methods.ExecuteDeleteAsync();
        await database.Types.ExecuteDeleteAsync();
        if (!populated) return;
        database.Accounts.AddRange(new Account { Id = 1, Bank = "SYNTHETIC-PRIVATE-1", AccountNumber = "SYNTHETIC-ONLY" },
            new Account { Id = 2, Bank = "SYNTHETIC-PRIVATE-2", AccountNumber = "SYNTHETIC-ONLY" });
        database.Directions.AddRange(new PaymentDirection { Id = 1, Name = "SYNTHETIC-PRIVATE-1", Description = "Synthetic" },
            new PaymentDirection { Id = 2, Name = "SYNTHETIC-PRIVATE-2", Description = "Synthetic" });
        database.Methods.AddRange(new PaymentMethod { Id = 1, Name = "SYNTHETIC-PRIVATE-1", Description = "Synthetic" },
            new PaymentMethod { Id = 2, Name = "SYNTHETIC-PRIVATE-2", Description = "Synthetic" });
        database.Types.AddRange(new PaymentType { Id = 1, Name = "SYNTHETIC-PRIVATE-1", Description = "Synthetic" },
            new PaymentType { Id = 2, Name = "SYNTHETIC-PRIVATE-2", Description = "Synthetic" });
        foreach (var id in new[] { 7, 8 })
        {
            database.Payments.Add(new PaymentRecord
            {
                Id = id,
                PaymentDirectionId = 1,
                PaymentMethodId = 1,
                PaymentTypeId = 1,
                Description = "SYNTHETIC-PRIVATE-PAYMENT",
                Amount = 1234.56m,
                PaymentDate = DateTime.UtcNow,
            });
        }
        if (files)
        {
            database.Files.AddRange(new PaymentFile { Id = 71, PaymentId = 7, Bucket = "synthetic-bucket", ObjectName = "SYNTHETIC-PRIVATE-1" },
                new PaymentFile { Id = 72, PaymentId = 7, Bucket = "synthetic-bucket", ObjectName = "SYNTHETIC-PRIVATE-2" },
                new PaymentFile { Id = 81, PaymentId = 8, Bucket = "synthetic-bucket", ObjectName = "OTHER-OWNER" });
        }
        await database.SaveChangesAsync();
    }

    private async Task<string> SnapshotAsync()
    {
        await using var payment = fixture.Database();
        await using var invoice = fixture.InvoiceDatabase();
        await using var receipt = fixture.ReceiptDatabase();
        return JsonSerializer.Serialize(new
        {
            Accounts = await payment.Accounts.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Directions = await payment.Directions.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Methods = await payment.Methods.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Types = await payment.Types.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Payments = await payment.Payments.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Files = await payment.Files.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Invoices = await invoice.Invoices.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Receipts = await receipt.Receipts.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        });
    }
}

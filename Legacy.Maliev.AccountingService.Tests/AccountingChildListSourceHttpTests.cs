using System.Net;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Domain.Receipt;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class AccountingChildListSourceHttpTests(AccountingBoundaryHttpFixture fixture)
    : IClassFixture<AccountingBoundaryHttpFixture>
{
    [Theory]
    [InlineData("/invoices/7/orderitems")]
    [InlineData("/invoices/7/files")]
    [InlineData("/receipts/9/orderitems")]
    [InlineData("/receipts/9/files")]
    [InlineData("/invoices/1234/orderitems")]
    [InlineData("/invoices/1234/files")]
    [InlineData("/receipts/1234/orderitems")]
    [InlineData("/receipts/1234/files")]
    public async Task EmptyOrMissingParent_SourceReturns404WithoutMasterOrChildMutation(string path)
    {
        await SeedAsync(false);
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read, AccountingPermissions.FilesRead]);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("[]", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("/invoices/0/orderitems", HttpStatusCode.BadRequest)]
    [InlineData("/receipts/0/orderitems", HttpStatusCode.BadRequest)]
    [InlineData("/invoices/0/files", HttpStatusCode.NotFound)]
    [InlineData("/receipts/0/files", HttpStatusCode.NotFound)]
    public async Task ZeroParent_SourceDistinguishesItemsRequiredIdentityFromEmptyFiles(string path, HttpStatusCode expected)
    {
        await SeedAsync(true);
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read, AccountingPermissions.FilesRead]);
        using var response = await client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("/invoices/7/orderitems", 71, "InvoiceId", 7)]
    [InlineData("/invoices/7/files", 72, "InvoiceId", 7)]
    [InlineData("/receipts/9/orderitems", 91, "ReceiptId", 9)]
    [InlineData("/receipts/9/files", 92, "ReceiptId", 9)]
    public async Task PopulatedParent_PreservesOwnedRowsPascalWireAndNoReadMutation(string path, int childId, string ownerField, int ownerId)
    {
        await SeedAsync(true);
        var before = await SnapshotAsync();
        using var client = fixture.Client([AccountingPermissions.Read, AccountingPermissions.FilesRead]);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(childId, row.GetProperty("Id").GetInt32());
        Assert.Equal(ownerId, row.GetProperty(ownerField).GetInt32());
        if (path.EndsWith("files", StringComparison.Ordinal))
        {
            Assert.Equal("synthetic-bucket", row.GetProperty("Bucket").GetString());
            Assert.Equal("synthetic-document", row.GetProperty("ObjectName").GetString());
        }
        else
        {
            Assert.Equal("SYNTHETIC-PRIVATE-CHILD", row.GetProperty("Description").GetString());
            Assert.Equal(2m, row.GetProperty("Quantity").GetDecimal());
            Assert.Equal(50m, row.GetProperty("UnitPrice").GetDecimal());
            Assert.Equal(100m, row.GetProperty("Subtotal").GetDecimal());
        }
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("/invoices/7/orderitems", true)]
    [InlineData("/invoices/7/orderitems", false)]
    [InlineData("/invoices/7/files", true)]
    [InlineData("/invoices/7/files", false)]
    [InlineData("/receipts/9/orderitems", true)]
    [InlineData("/receipts/9/orderitems", false)]
    [InlineData("/receipts/9/files", true)]
    [InlineData("/receipts/9/files", false)]
    public async Task AnonymousOrLiveDenied_NoChildDisclosureOrFinancialMutation(string path, bool anonymous)
    {
        await SeedAsync(true);
        var before = await SnapshotAsync();
        using var client = fixture.Client(anonymous ? null : [AccountingPermissions.Read, AccountingPermissions.FilesRead], allowLive: false);
        using var response = await client.GetAsync(path);
        Assert.Equal(anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SYNTHETIC-PRIVATE-CHILD", body, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-document", body, StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync());
    }

    private async Task SeedAsync(bool children)
    {
        await using var invoice = fixture.InvoiceDatabase();
        await invoice.Files.ExecuteDeleteAsync();
        await invoice.Items.ExecuteDeleteAsync();
        await invoice.Invoices.ExecuteDeleteAsync();
        invoice.Invoices.AddRange(new Invoice { Id = 7, Number = "SYNTHETIC-7", Currency = "THB", Subtotal = 100m, Vat = 7m, Total = 107m },
            new Invoice { Id = 8, Number = "SYNTHETIC-8", Currency = "THB", Subtotal = 100m, Vat = 7m, Total = 107m });
        if (children)
        {
            invoice.Items.AddRange(new InvoiceOrderItem { Id = 71, InvoiceId = 7, Description = "SYNTHETIC-PRIVATE-CHILD", Quantity = 2, UnitPrice = 50m },
                new InvoiceOrderItem { Id = 81, InvoiceId = 8, Description = "OTHER-OWNER", Quantity = 1, UnitPrice = 10m });
            invoice.Files.AddRange(new InvoiceFile { Id = 72, InvoiceId = 7, Bucket = "synthetic-bucket", ObjectName = "synthetic-document" },
                new InvoiceFile { Id = 82, InvoiceId = 8, Bucket = "synthetic-bucket", ObjectName = "other-document" });
        }
        await invoice.SaveChangesAsync();
        await using var receipt = fixture.ReceiptDatabase();
        await receipt.Files.ExecuteDeleteAsync();
        await receipt.Items.ExecuteDeleteAsync();
        await receipt.Receipts.ExecuteDeleteAsync();
        receipt.Receipts.AddRange(new Receipt { Id = 9, InvoiceNumber = "SYNTHETIC-9", Currency = "THB", PaymentDate = DateTime.UtcNow, Subtotal = 100m, Vat = 7m, Total = 107m },
            new Receipt { Id = 10, InvoiceNumber = "SYNTHETIC-10", Currency = "THB", PaymentDate = DateTime.UtcNow, Subtotal = 100m, Vat = 7m, Total = 107m });
        if (children)
        {
            receipt.Items.AddRange(new ReceiptOrderItem { Id = 91, ReceiptId = 9, Description = "SYNTHETIC-PRIVATE-CHILD", Quantity = 2, UnitPrice = 50m },
                new ReceiptOrderItem { Id = 101, ReceiptId = 10, Description = "OTHER-OWNER", Quantity = 1, UnitPrice = 10m });
            receipt.Files.AddRange(new ReceiptFile { Id = 92, ReceiptId = 9, Bucket = "synthetic-bucket", ObjectName = "synthetic-document" },
                new ReceiptFile { Id = 102, ReceiptId = 10, Bucket = "synthetic-bucket", ObjectName = "other-document" });
        }
        await receipt.SaveChangesAsync();
    }

    private async Task<string> SnapshotAsync()
    {
        await using var payment = fixture.Database();
        await using var invoice = fixture.InvoiceDatabase();
        await using var receipt = fixture.ReceiptDatabase();
        return JsonSerializer.Serialize(new
        {
            Payment = await payment.Payments.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Invoice = await invoice.Invoices.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            InvoiceItems = await invoice.Items.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            InvoiceFiles = await invoice.Files.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Receipt = await receipt.Receipts.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            ReceiptItems = await receipt.Items.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            ReceiptFiles = await receipt.Files.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        });
    }
}

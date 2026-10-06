using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Domain.Receipt;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

// Private source regression draft; actual hosted build and native execution remain required.
public sealed class InvoiceReceiptPaymentDateSourceHttpTests(AccountingBoundaryHttpFixture fixture)
    : IClassFixture<AccountingBoundaryHttpFixture>
{
    [Theory]
    [InlineData(true, null)]
    [InlineData(true, "2026-10-03T00:00:00")]
    [InlineData(true, "2026-10-03T12:34:30")]
    [InlineData(true, "2026-10-03T12:34:30Z")]
    [InlineData(false, null)]
    [InlineData(false, "2026-10-03T00:00:00")]
    [InlineData(false, "2026-10-03T12:34:30")]
    [InlineData(false, "2026-10-03T12:34:30Z")]
    public async Task Create_SourceClockPersistsWithoutLocalConversion(bool invoice, string? date)
    {
        await fixture.ResetAsync();
        using var client = await fixture.ReceiptClientAsync([AccountingPermissions.Create, AccountingPermissions.Read]);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        var before = await fixture.ReceiptSnapshotAsync();
        using var created = await client.PostAsJsonAsync(Route(invoice), Body(invoice, date));
        if (!invoice && date is null)
        {
            await fixture.AssertStatusAsync(created, HttpStatusCode.BadRequest);
            Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
            return;
        }

        await fixture.AssertStatusAsync(created, HttpStatusCode.Created);
        Assert.NotNull(created.Headers.Location);
        var wire = (await client.GetFromJsonAsync<JsonObject>(created.Headers.Location))!;
        var id = wire["Id"]!.GetValue<int>();
        Assert.True(id > 0);
        Assert.Equal("THB", wire["Currency"]!.GetValue<string>());
        Assert.Equal(100m, wire["Total"]!.GetValue<decimal>());
        AssertWireDate(wire, date);
        await AssertStoredAsync(invoice, id, date);
        var after = await fixture.ReceiptSnapshotAsync();
        Assert.Equal(before.Payment, after.Payment);
        Assert.Equal(before.Journal, after.Journal);
        Assert.Equal(invoice ? before.Receipt : before.Invoice, invoice ? after.Receipt : after.Invoice);
        Assert.Contains(fixture.LiveChecks, check => check.Permission == AccountingPermissions.Create);
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(true, "2026-10-04T00:00:00")]
    [InlineData(true, "2026-10-04T12:34:30")]
    [InlineData(true, "2026-10-04T12:34:30Z")]
    [InlineData(false, null)]
    [InlineData(false, "2026-10-04T00:00:00")]
    [InlineData(false, "2026-10-04T12:34:30")]
    [InlineData(false, "2026-10-04T12:34:30Z")]
    public async Task Update_SourceClockPreservesIdentityCreatedDateAndFinancialState(bool invoice, string? date)
    {
        await fixture.ResetAsync();
        using var client = await fixture.ReceiptClientAsync([AccountingPermissions.Create, AccountingPermissions.Read, AccountingPermissions.Update]);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var created = await client.PostAsJsonAsync(Route(invoice), Body(invoice, "2026-10-03T00:00:00Z"));
        await fixture.AssertStatusAsync(created, HttpStatusCode.Created);
        Assert.NotNull(created.Headers.Location);
        var original = (await client.GetFromJsonAsync<JsonObject>(created.Headers.Location))!;
        var id = original["Id"]!.GetValue<int>();
        var before = await fixture.ReceiptSnapshotAsync();
        var body = (JsonObject)original.DeepClone();
        body["PaymentDate"] = date;
        body["Id"] = 999999;
        body["Comment"] = "Synthetic updated source clock";
        using var updated = await client.PutAsJsonAsync($"{Route(invoice)}/{id}", body);
        if (!invoice && date is null)
        {
            await fixture.AssertStatusAsync(updated, HttpStatusCode.BadRequest);
            Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
            return;
        }

        await fixture.AssertStatusAsync(updated, HttpStatusCode.NoContent);
        var wire = (await client.GetFromJsonAsync<JsonObject>($"{Route(invoice)}/{id}"))!;
        Assert.Equal(id, wire["Id"]!.GetValue<int>());
        Assert.Equal(original["CreatedDate"]!.GetValue<string>(), wire["CreatedDate"]!.GetValue<string>());
        Assert.Equal("Synthetic updated source clock", wire["Comment"]!.GetValue<string>());
        Assert.Equal(original["Total"]!.GetValue<decimal>(), wire["Total"]!.GetValue<decimal>());
        AssertWireDate(wire, date);
        await AssertStoredAsync(invoice, id, date);
        var after = await fixture.ReceiptSnapshotAsync();
        Assert.Equal(before.Payment, after.Payment);
        Assert.Equal(before.Journal, after.Journal);
        Assert.Equal(invoice ? before.Receipt : before.Invoice, invoice ? after.Receipt : after.Invoice);
        Assert.Contains(fixture.LiveChecks, check => check.Permission == AccountingPermissions.Update);
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    public async Task MissingAdmission_CannotPersistMasterDate(bool invoice, bool update, bool authenticated)
    {
        await fixture.ResetAsync();
        using var client = await fixture.ReceiptClientAsync(authenticated ? [AccountingPermissions.Create, AccountingPermissions.Update] : null, allowLive: false);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        var before = await fixture.ReceiptSnapshotAsync();
        var body = Body(invoice, "2026-10-03T12:34:30");
        using var response = update
            ? await client.PutAsJsonAsync($"{Route(invoice)}/999999", body)
            : await client.PostAsJsonAsync(Route(invoice), body);
        await fixture.AssertStatusAsync(response, authenticated ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized);
        Assert.Equal(before, await fixture.ReceiptSnapshotAsync());
        Assert.Equal(0, fixture.ReceiptOutboundCalls);
    }

    private async Task AssertStoredAsync(bool invoice, int id, string? date)
    {
        DateTime? stored;
        if (invoice)
        {
            await using var database = fixture.InvoiceDatabase();
            var row = await database.Invoices.AsNoTracking().SingleAsync(value => value.Id == id);
            stored = row.PaymentDate;
            Assert.Equal(DateTimeKind.Unspecified, row.CreatedDate.GetValueOrDefault().Kind);
            Assert.Equal(DateTimeKind.Unspecified, row.ModifiedDate.GetValueOrDefault().Kind);
            Assert.Equal(42, row.CustomerId);
        }
        else
        {
            await using var database = fixture.ReceiptDatabase();
            var row = await database.Receipts.AsNoTracking().SingleAsync(value => value.Id == id);
            stored = row.PaymentDate;
            Assert.Equal(95m, row.AmountPaid);
            Assert.Equal(DateTimeKind.Unspecified, row.CreatedDate.GetValueOrDefault().Kind);
            Assert.Equal(DateTimeKind.Unspecified, row.ModifiedDate.GetValueOrDefault().Kind);
            Assert.Equal(42, row.CustomerId);
        }

        if (date is null) Assert.Null(stored);
        else
        {
            Assert.NotNull(stored);
            Assert.Equal(DateTimeKind.Utc, stored.GetValueOrDefault().Kind);
            Assert.Equal(DateTime.Parse(date.TrimEnd('Z'), CultureInfo.InvariantCulture, DateTimeStyles.None).Ticks, stored.GetValueOrDefault().Ticks);
        }
    }

    private static string Route(bool invoice) => invoice ? "/invoices" : "/receipts";

    private static void AssertWireDate(JsonObject wire, string? date)
    {
        if (date is null) Assert.Null(wire["PaymentDate"]);
        else Assert.Equal(date.TrimEnd('Z'), wire["PaymentDate"]!.GetValue<string>()[..19]);
    }

    private static JsonObject Body(bool invoice, string? date)
    {
        var type = invoice ? typeof(Invoice) : typeof(Receipt);
        var body = new JsonObject();
        foreach (var property in type.GetProperties().Where(property => property.PropertyType == typeof(string)))
            body[property.Name] = "Synthetic source clock";
        body["Number"] = "SYNTHETIC-MASTER-CLOCK";
        body["InvoiceNumber"] = "SYNTHETIC-MASTER-CLOCK";
        body["CustomerId"] = 42;
        body["Currency"] = "THB";
        body["Total"] = 100m;
        body["Subtotal"] = 100m;
        body["Vat"] = 0m;
        body["WithholdingTax"] = 5m;
        body["PaymentDate"] = date;
        return body;
    }
}

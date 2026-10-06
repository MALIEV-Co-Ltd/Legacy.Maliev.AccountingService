using System.Net;
using System.Text.Json;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Api.Controllers.Invoice;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace Legacy.Maliev.AccountingService.Tests;

// Source-only draft: actual Accounting DTO/controller and normal Program MVC options.
// Synthetic malformed-null wire remains a negative, never a valid repository outcome.
[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class PaidInvoiceOutcomeWireTests(AccountingBoundaryHttpFixture fixture)
{
    private static readonly DateTime FromUtc = new(2026, 8, 25, 17, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ToUtc = FromUtc.AddDays(7);
    private static readonly DateTime DayUtc = new(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("empty")]
    [InlineData("zero")]
    [InlineData("mixed")]
    [InlineData("null-currency-negative")]
    [InlineData("null-days-negative")]
    public async Task ActualControllerDtoAndProgramMvcSerializerRetainSyntheticWire(string variant)
    {
        var aggregate = variant switch
        {
            "empty" => new PaidInvoiceOutcomeReadback(FromUtc, ToUtc, []),
            "zero" => new PaidInvoiceOutcomeReadback(FromUtc, ToUtc, [new(DayUtc, 0, 0, 0, [])]),
            "mixed" => new PaidInvoiceOutcomeReadback(FromUtc, ToUtc,
                [new(DayUtc, 3, 1, 2, [new("THB", 120.5000m, 2), new("USD", 25.01m, 1)])]),
            // Deliberately violate the nonnullable production record for consumer rejection fixtures.
            "null-currency-negative" => new PaidInvoiceOutcomeReadback(FromUtc, ToUtc,
                [new(DayUtc, 1, 0, 1, [new(null!, 0.0m, 1)])]),
            "null-days-negative" => new PaidInvoiceOutcomeReadback(FromUtc, ToUtc, null!),
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        var service = new Mock<IAccountingService>(MockBehavior.Strict);
        service.Setup(value => value.GetPaidInvoiceOutcomeReadbackAsync(FromUtc, ToUtc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(aggregate);
        var controller = new InvoicesController(service.Object, Mock.Of<IIdempotencyStore>());
        var result = await controller.GetPaidInvoiceOutcomeReadbackAsync(FromUtc, ToUtc, CancellationToken.None);
        Assert.Same(aggregate, result.Value);
        await using var scope = await fixture.ReceiptScopeAsync();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;
        var wire = JsonSerializer.SerializeToUtf8Bytes(result.Value, options);
        using var document = JsonDocument.Parse(wire);
        var root = document.RootElement;
        Assert.Equal("2026-08-25T17:00:00Z", root.GetProperty("FromUtc").GetString());
        Assert.Equal("2026-09-01T17:00:00Z", root.GetProperty("ToUtc").GetString());
        if (variant == "null-days-negative")
        {
            Assert.Equal(["FromUtc", "ToUtc"], Keys(root));
            Assert.False(root.TryGetProperty("Days", out _));
        }
        else
        {
            Assert.Equal(["Days", "FromUtc", "ToUtc"], Keys(root));
            var days = root.GetProperty("Days");
            Assert.Equal(variant == "empty" ? 0 : 1, days.GetArrayLength());
            if (variant != "empty")
            {
                var day = days[0];
                AssertDayShape(day);
                Assert.Equal("2026-08-26T00:00:00Z", day.GetProperty("DayUtc").GetString());
                var amounts = day.GetProperty("PaidInvoiceAmountsByCurrency");
                if (variant == "zero")
                {
                    Assert.Equal(0, amounts.GetArrayLength());
                    Assert.Equal(0, day.GetProperty("PaidInvoiceCount").GetInt32());
                    Assert.Equal(0, day.GetProperty("SourceAttributedPaidInvoiceCount").GetInt32());
                    Assert.Equal(0, day.GetProperty("UnattributedPaidInvoiceCount").GetInt32());
                }
                else if (variant == "mixed")
                {
                    Assert.Equal(2, amounts.GetArrayLength());
                    AssertCurrency(amounts[0], "THB", 120.5000m, 2);
                    AssertCurrency(amounts[1], "USD", 25.01m, 1);
                    Assert.Contains("\"PaidInvoiceTotal\":120.5000", System.Text.Encoding.UTF8.GetString(wire), StringComparison.Ordinal);
                    Assert.Equal(3, day.GetProperty("PaidInvoiceCount").GetInt32());
                    Assert.Equal(1, day.GetProperty("SourceAttributedPaidInvoiceCount").GetInt32());
                    Assert.Equal(2, day.GetProperty("UnattributedPaidInvoiceCount").GetInt32());
                }
                else
                {
                    var amount = Assert.Single(amounts.EnumerateArray());
                    Assert.Equal(["PaidInvoiceCount", "PaidInvoiceTotal"], Keys(amount));
                    Assert.False(amount.TryGetProperty("Currency", out _));
                    Assert.Equal(0.0m, amount.GetProperty("PaidInvoiceTotal").GetDecimal());
                    Assert.Equal(1, amount.GetProperty("PaidInvoiceCount").GetInt32());
                }
            }
        }
        service.VerifyAll();
        Export(variant, wire);
    }

    [Fact]
    public async Task NormalHttpRepositoryAndMvcProduceUtcPrivacySafeAggregatesAndNormalizeNullCurrency()
    {
        await fixture.ResetAsync();
        await using (var database = fixture.InvoiceDatabase())
        {
            database.Invoices.AddRange(
                Row("THB-attributed", DayUtc.AddHours(1), "THB", 100m, true),
                Row("THB-unattributed", DayUtc.AddHours(2), "THB", 20.50m, false),
                Row("USD-unattributed", DayUtc.AddHours(3), "USD", 25.01m, false),
                Row("null-currency", DayUtc.AddDays(1), null!, null, false),
                Row("before-window", FromUtc.AddTicks(-10), "THB", 999m, false),
                Row("exclusive-end", ToUtc, "THB", 999m, false));
            await database.SaveChangesAsync();
        }
        using var client = await fixture.ReceiptClientAsync([AccountingPermissions.Read]);
        var outbound = fixture.ReceiptOutboundCalls;
        var path = "/invoices/outcomes/readback?fromUtc=" + Uri.EscapeDataString(FromUtc.ToString("O"))
            + "&toUtc=" + Uri.EscapeDataString(ToUtc.ToString("O"));
        using var response = await client.GetAsync(path);
        await fixture.AssertStatusAsync(response, HttpStatusCode.OK);
        var wire = await response.Content.ReadAsByteArrayAsync();
        Assert.InRange(wire.Length, 1, 16 * 1024);
        using var document = JsonDocument.Parse(wire);
        var root = document.RootElement;
        Assert.Equal(["Days", "FromUtc", "ToUtc"], Keys(root));
        Assert.Equal("2026-08-25T17:00:00Z", root.GetProperty("FromUtc").GetString());
        Assert.Equal("2026-09-01T17:00:00Z", root.GetProperty("ToUtc").GetString());
        var days = root.GetProperty("Days");
        Assert.Equal(2, days.GetArrayLength());
        AssertDayShape(days[0]);
        AssertDayShape(days[1]);
        Assert.Equal("2026-08-26T00:00:00Z", days[0].GetProperty("DayUtc").GetString());
        Assert.Equal("2026-08-27T00:00:00Z", days[1].GetProperty("DayUtc").GetString());
        Assert.Equal(3, days[0].GetProperty("PaidInvoiceCount").GetInt32());
        Assert.Equal(1, days[0].GetProperty("SourceAttributedPaidInvoiceCount").GetInt32());
        Assert.Equal(2, days[0].GetProperty("UnattributedPaidInvoiceCount").GetInt32());
        var amounts = days[0].GetProperty("PaidInvoiceAmountsByCurrency");
        Assert.Equal(2, amounts.GetArrayLength());
        AssertCurrency(amounts[0], "THB", 120.50m, 2);
        AssertCurrency(amounts[1], "USD", 25.01m, 1);
        Assert.Equal(1, days[1].GetProperty("PaidInvoiceCount").GetInt32());
        Assert.Equal(0, days[1].GetProperty("SourceAttributedPaidInvoiceCount").GetInt32());
        Assert.Equal(1, days[1].GetProperty("UnattributedPaidInvoiceCount").GetInt32());
        AssertCurrency(Assert.Single(days[1].GetProperty("PaidInvoiceAmountsByCurrency").EnumerateArray()), "UNSPECIFIED", 0m, 1);
        Assert.Contains(fixture.LiveChecks, value => value.Permission == AccountingPermissions.Read);
        Assert.Equal(outbound, fixture.ReceiptOutboundCalls);
        Export("repository-http", wire);
    }

    private static Invoice Row(string number, DateTime paidUtc, string currency, decimal? total, bool attributed) => new()
    {
        Number = "synthetic-wire-" + number,
        CustomerId = 42,
        IsPaid = true,
        PaymentDate = paidUtc,
        Currency = currency,
        Total = total,
        SourceRequestId = attributed ? 91 : null,
        SourceJourneyId = attributed ? Guid.Parse("a3308993-39b9-41fc-bbfd-f3500de40f55") : null,
    };

    private static string[] Keys(JsonElement value) => value.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
    private static void AssertDayShape(JsonElement day) => Assert.Equal(
        ["DayUtc", "PaidInvoiceAmountsByCurrency", "PaidInvoiceCount", "SourceAttributedPaidInvoiceCount", "UnattributedPaidInvoiceCount"], Keys(day));
    private static void AssertCurrency(JsonElement amount, string currency, decimal total, int count)
    {
        Assert.Equal(["Currency", "PaidInvoiceCount", "PaidInvoiceTotal"], Keys(amount));
        Assert.Equal(currency, amount.GetProperty("Currency").GetString());
        Assert.Equal(total, amount.GetProperty("PaidInvoiceTotal").GetDecimal());
        Assert.Equal(count, amount.GetProperty("PaidInvoiceCount").GetInt32());
    }
    private static void Export(string variant, byte[] wire)
    {
        Assert.InRange(wire.Length, 1, 16 * 1024);
        var folder = Environment.GetEnvironmentVariable("PAID_INVOICE_WIRE_OUTPUT_DIRECTORY");
        if (string.IsNullOrEmpty(folder)) return;
        var root = Path.GetFullPath(folder);
        if (!Path.IsPathFullyQualified(folder)) throw new InvalidOperationException("Hosted synthetic wire output must have an absolute owned directory.");
        Directory.CreateDirectory(root);
        // Fixed variants above are the only names, and contain synthetic aggregate data only.
        File.WriteAllBytes(Path.Combine(root, "invoice-" + variant + ".json"), wire);
    }
}

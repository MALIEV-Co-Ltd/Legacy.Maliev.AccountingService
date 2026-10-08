using System.Reflection;
using Legacy.Maliev.AccountingService.Application.Models;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class BillingAmountTests
{
    [Theory]
    [InlineData(0, 200, null, 200)]
    [InlineData(200, null, null, 800)]
    [InlineData(0, null, 25, 250)]
    public void AllocatesRequestedGrossWithoutDuplicatingQuotation(decimal billed, int? amount, int? percentage, decimal expected)
    {
        var result = Allocate(900m, 100m, billed, amount, percentage, 2);
        Assert.Equal(expected, Value(result, "Gross"));
        Assert.Equal(expected, Value(result, "Base") + Value(result, "Vat"));
    }

    [Fact]
    public void FinalStageAbsorbsComponentRoundingResidual()
    {
        var first = Allocate(0.03m, 0.02m, 0m, 0.02m, null, 2);
        var final = Allocate(0.03m - Value(first, "Base"), 0.02m - Value(first, "Vat"), 0m, null, null, 2);
        Assert.Equal(0.03m, Value(first, "Base") + Value(final, "Base"));
        Assert.Equal(0.02m, Value(first, "Vat") + Value(final, "Vat"));
    }

    [Theory]
    [InlineData(900, 200, null)]
    [InlineData(0, -1, null)]
    [InlineData(0, null, 101)]
    [InlineData(0, 1, 10)]
    public void RejectsOverbillingOrInvalidIntent(decimal billed, int? amount, int? percentage)
    {
        var exception = Assert.Throws<TargetInvocationException>(() => Allocate(900m, 100m, billed, amount, percentage, 2));
        Assert.IsAssignableFrom<ArgumentException>(exception.InnerException);
    }

    private static object Allocate(decimal basis, decimal vat, decimal billed, decimal? amount, decimal? percentage, int precision)
    {
        // Reflection keeps this regression executable before the new feature type exists.
        var type = typeof(InvoiceCreationPreview).Assembly.GetType("Legacy.Maliev.AccountingService.Application.Services.BillingAmountCalculator");
        Assert.NotNull(type);
        var method = type.GetMethod("AllocateComponents", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return method.Invoke(null, [basis, vat, billed, amount, percentage, precision])!;
    }

    private static decimal Value(object result, string property) => (decimal)result.GetType().GetProperty(property)!.GetValue(result)!;
}

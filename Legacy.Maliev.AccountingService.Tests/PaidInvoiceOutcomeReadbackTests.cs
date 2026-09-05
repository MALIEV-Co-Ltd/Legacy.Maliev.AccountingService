using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Api.Controllers.Invoice;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class PaidInvoiceOutcomeReadbackTests
{
    private static readonly DateTime FromUtc = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ToUtc = new(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ReadbackRoute_RequiresLiveCheckedAccountingReadPermission()
    {
        var method = typeof(InvoicesController).GetMethod(nameof(InvoicesController.GetPaidInvoiceOutcomeReadbackAsync))!;
        var route = Assert.Single(method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: true).Cast<HttpGetAttribute>());
        Assert.Equal("outcomes/readback", route.Template);
        var permission = Assert.Single(method.GetCustomAttributes(typeof(RequirePermissionAttribute), inherit: true).Cast<RequirePermissionAttribute>());
        Assert.Equal(AccountingPermissions.Read, permission.Permission);
        Assert.True(permission.RequireLiveCheck);
    }

    [Theory]
    [MemberData(nameof(InvalidWindows))]
    public async Task ReadbackRoute_RejectsInvalidOrOversizedWindows(DateTime fromUtc, DateTime toUtc)
    {
        var service = new Mock<IAccountingService>(MockBehavior.Strict);
        var controller = new InvoicesController(service.Object, Mock.Of<IIdempotencyStore>());

        var result = await controller.GetPaidInvoiceOutcomeReadbackAsync(fromUtc, toUtc, CancellationToken.None);

        Assert.IsType<BadRequestResult>(result.Result);
    }

    [Fact]
    public async Task ReadbackRoute_ReturnsOnlyTheAggregateServiceContract()
    {
        var aggregate = new PaidInvoiceOutcomeReadback(FromUtc, ToUtc,
        [
            new(FromUtc, 2, 1, 1, [new("THB", 1250m, 2)])
        ]);
        var service = new Mock<IAccountingService>(MockBehavior.Strict);
        service.Setup(value => value.GetPaidInvoiceOutcomeReadbackAsync(FromUtc, ToUtc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(aggregate);
        var controller = new InvoicesController(service.Object, Mock.Of<IIdempotencyStore>());

        var result = await controller.GetPaidInvoiceOutcomeReadbackAsync(FromUtc, ToUtc, CancellationToken.None);

        Assert.Same(aggregate, result.Value);
        service.VerifyAll();
    }

    public static TheoryData<DateTime, DateTime> InvalidWindows() => new()
    {
        { DateTime.SpecifyKind(FromUtc, DateTimeKind.Unspecified), ToUtc },
        { FromUtc, DateTime.SpecifyKind(ToUtc, DateTimeKind.Local) },
        { ToUtc, FromUtc },
        { FromUtc, FromUtc.AddDays(31).AddTicks(1) },
    };
}

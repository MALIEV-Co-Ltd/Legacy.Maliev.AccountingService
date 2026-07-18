using System.Reflection;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Api.Controllers.Invoice;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class InvoiceCreationControllerContractTests
{
    [Fact]
    public void Routes_AreAuthenticatedCriticalAccountingCreateBoundaries()
    {
        var controller = typeof(InvoiceCreationController);
        Assert.NotNull(controller.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        Assert.Equal("invoices/from-quotation", controller.GetCustomAttribute<RouteAttribute>()?.Template);
        foreach (var methodName in new[] { nameof(InvoiceCreationController.PreviewAsync), nameof(InvoiceCreationController.CreateAsync) })
        {
            var permission = controller.GetMethod(methodName)!.GetCustomAttribute<RequirePermissionAttribute>();
            Assert.Equal(AccountingPermissions.Create, permission?.Permission);
            Assert.True(permission?.RequireLiveCheck);
        }
    }

    [Fact]
    public async Task CreateAsync_RejectsMissingStableOperationBeforeWorkflowCall()
    {
        var workflow = new Mock<IInvoiceCreationWorkflow>(MockBehavior.Strict);
        var controller = new InvoiceCreationController(workflow.Object);

        var result = await controller.CreateAsync(84, Request(), null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    private static CreateInvoiceFromQuotationRequest Request() => new("INV-84", null, null, null, null, null, null, new(null, null, null, null, null, null, null, null, null), new(null, null, null, null, null, null, null, null, null), null, null, false, true);
}

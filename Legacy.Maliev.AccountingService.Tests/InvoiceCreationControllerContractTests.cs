using System.Reflection;
using Legacy.Maliev.AccountingService.Api.Controllers.Invoice;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        var controller = Controller(workflow);

        var result = await controller.CreateAsync(84, Request(), null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateAsync_WithoutDelegation_PreservesServiceOnlyPath()
    {
        var workflow = new Mock<IInvoiceCreationWorkflow>(MockBehavior.Strict);
        var operationId = Guid.NewGuid();
        var completed = new InvoiceCreationResult(901, InvoiceCreationState.Completed,
            InvoiceCreationEmailState.NotRequested, null, new("maliev.com", "invoice.pdf"));
        workflow.Setup(value => value.CreateAsync(84, It.IsAny<CreateInvoiceFromQuotationRequest>(), operationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(completed);
        var controller = Controller(workflow);

        var response = await controller.CreateAsync(84, Request(), operationId.ToString("D"), CancellationToken.None);

        Assert.Same(completed, Assert.IsType<OkObjectResult>(response.Result).Value);
        workflow.VerifyAll();
    }

    [Fact]
    public async Task CreateAsync_InvalidPresentDelegation_DoesNotCallWorkflow()
    {
        var workflow = new Mock<IInvoiceCreationWorkflow>(MockBehavior.Strict);
        var controller = Controller(workflow);
        controller.HttpContext.Request.Headers[InvoiceCreationDelegationVerifier.HeaderName] = "Bearer invalid";

        var response = await controller.CreateAsync(84, Request(), Guid.NewGuid().ToString("D"), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(response.Result);
        workflow.VerifyNoOtherCalls();
    }

    private static InvoiceCreationController Controller(Mock<IInvoiceCreationWorkflow> workflow)
    {
        var database = new InvoiceDbContext(new DbContextOptionsBuilder<InvoiceDbContext>().UseNpgsql("Host=localhost;Database=unused").Options);
        return new InvoiceCreationController(workflow.Object,
            new InvoiceCreationDelegationVerifier(new ConfigurationBuilder().Build(), TimeProvider.System),
            new InvoiceCreationAdmissionStore(database))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private static CreateInvoiceFromQuotationRequest Request() => new("INV-84", null, null, null, null, null, null, new(null, null, null, null, null, null, null, null, null), new(null, null, null, null, null, null, null, null, null), null, null, false, true);
}

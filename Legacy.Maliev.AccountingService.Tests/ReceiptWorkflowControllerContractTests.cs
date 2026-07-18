using System.Reflection;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Api.Controllers.Invoice;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class ReceiptWorkflowControllerContractTests
{
    [Fact]
    public void Controller_UsesInvoiceOwnedRouteAndStrictLiveCheckedPermissions()
    {
        var controller = typeof(InvoiceReceiptWorkflowController);
        Assert.Equal("invoices/{invoiceId:int}/receipt", controller.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
        AssertPermission(nameof(InvoiceReceiptWorkflowController.CreateAsync), AccountingPermissions.Create);
        AssertPermission(nameof(InvoiceReceiptWorkflowController.RemoveAsync), AccountingPermissions.Delete);
        AssertPermission(nameof(InvoiceReceiptWorkflowController.SendEmailAsync), AccountingPermissions.Update);
    }

    [Fact]
    public async Task CreateAsync_RejectsMissingStableOperationIdBeforeWorkflowCall()
    {
        var workflow = new Mock<IReceiptWorkflow>(MockBehavior.Strict);
        var controller = new InvoiceReceiptWorkflowController(workflow.Object);

        var result = await controller.CreateAsync(
            42,
            new ReceiptWorkflowApiRequest("paid", false),
            12,
            null,
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        workflow.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_RejectsMissingTrustedEmployeeBeforeWorkflowCall()
    {
        var workflow = new Mock<IReceiptWorkflow>(MockBehavior.Strict);
        var controller = new InvoiceReceiptWorkflowController(workflow.Object);

        var result = await controller.CreateAsync(
            42,
            new ReceiptWorkflowApiRequest("paid", false),
            0,
            Guid.NewGuid().ToString("D"),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        workflow.VerifyNoOtherCalls();
    }

    [Fact]
    public void Program_UsesSharedTokenExchangeAndOptsInEveryReceiptDependencyClient()
    {
        var program = File.ReadAllText(FindRepositoryFile("Legacy.Maliev.AccountingService.Api", "Program.cs"));
        Assert.Contains("AddLegacyAuthServiceTokenExchange", program, StringComparison.Ordinal);
        Assert.Equal(5, Count(program, ".AddLegacyServiceAuthentication()"));
        Assert.DoesNotContain("Authorization =", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiRequest_DoesNotAcceptRecipientOrSignatureAuthority()
    {
        var properties = typeof(ReceiptWorkflowApiRequest).GetProperties().Select(value => value.Name).ToArray();
        Assert.Equal(["Comment", "SendEmail"], properties);
        Assert.DoesNotContain(
            typeof(InvoiceReceiptWorkflowController).GetMethod(nameof(InvoiceReceiptWorkflowController.SendEmailAsync))!.GetParameters(),
            value => value.GetCustomAttribute<FromBodyAttribute>() is not null);
    }

    private static void AssertPermission(string methodName, string permission)
    {
        var method = typeof(InvoiceReceiptWorkflowController).GetMethod(methodName)!;
        var attribute = Assert.Single(method.GetCustomAttributes<RequirePermissionAttribute>());
        Assert.Equal(permission, attribute.Permission);
        Assert.True(attribute.RequireLiveCheck);
        var header = method.GetParameters().Single(parameter => parameter.Name == "idempotencyKey")
            .GetCustomAttribute<FromHeaderAttribute>();
        Assert.Equal("Idempotency-Key", header?.Name);
        var employee = method.GetParameters().SingleOrDefault(parameter => parameter.Name == "employeeId");
        if (methodName != nameof(InvoiceReceiptWorkflowController.RemoveAsync))
        {
            Assert.Equal("X-Legacy-Employee-Id", employee?.GetCustomAttribute<FromHeaderAttribute>()?.Name);
        }
    }

    private static int Count(string value, string pattern)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(pattern, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += pattern.Length;
        }

        return count;
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not find repository file '{Path.Combine(segments)}'.");
    }
}

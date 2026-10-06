using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Api.Controllers.Invoice;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Legacy.Maliev.AccountingService.Domain.Invoice;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace Legacy.Maliev.AccountingService.Tests;

/// <summary>Held first writer and cancelled duplicate exercise actual controller admission with real PostgreSQL.</summary>
public sealed class InvoiceFinancialPreparationOwnershipTests(InvoiceNotificationPhaseFencePostgresFixture fixture)
    : IClassFixture<InvoiceNotificationPhaseFencePostgresFixture>
{
    [Fact]
    public async Task CancelledDuplicateCannotMarkFirstWriterUncertainOrPreventItsFinancialCommit()
    {
        await using var firstDatabase = await fixture.NewDatabaseAsync();
        await using var duplicateDatabase = new InvoiceDbContext(new DbContextOptionsBuilder<InvoiceDbContext>()
            .UseNpgsql(firstDatabase.Database.GetConnectionString()).Options);
        using var rsa = RSA.Create(2048);
        var origin = new InvoiceNotificationOrigin("legacy-auth", "employee:42", "service:legacy-intranet");
        var operation = Guid.NewGuid();
        var request = new CreateInvoiceFromQuotationRequest("SYNTHETIC-HELD-" + operation.ToString("N"), null, null, null, null, null, null,
            new(null, null, null, null, null, null, null, null, null), new(null, null, null, null, null, null, null, null, null), null, null, false, false);
        var enteredFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredDuplicate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new Mock<IInvoiceCreationWorkflow>(MockBehavior.Strict);
        workflow.Setup(value => value.PrepareFinancialAsync(84, request, operation, origin, It.IsAny<CancellationToken>()))
            .Returns(async (int _, CreateInvoiceFromQuotationRequest _, Guid _, InvoiceNotificationOrigin _, CancellationToken token) =>
            {
                enteredFirst.TrySetResult();
                await releaseFirst.Task.WaitAsync(token);
                _ = await new InvoiceCreationStore(firstDatabase, TimeProvider.System).CreateAsync(
                    new Invoice { Number = request.InvoiceNumber, CustomerId = 42, Total = 12m },
                    [new() { Description = "owned", Quantity = 3, UnitPrice = 4m }],
                    new(operation, 84, origin, new DateTime(2026, 10, 6, 1, 2, 3, DateTimeKind.Unspecified)), token);
                return await new InvoiceFinancialOwnershipStore(firstDatabase).ReadAsync(operation, token);
            });
        workflow.Setup(value => value.ReadPreparedFinancialAsync(84, operation, origin, It.IsAny<CancellationToken>()))
            .Returns(async (int _, Guid _, InvoiceNotificationOrigin _, CancellationToken token) =>
            {
                enteredDuplicate.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("The bounded duplicate must be cancelled.");
            });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = origin.Issuer,
            ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())),
        }).Build();
        var issued = DateTimeOffset.UtcNow;
        var token = new JwtSecurityToken(origin.Issuer, InvoiceCreationDelegationVerifier.Audience,
            [new("sub", origin.EmployeeSubject), new("azp", origin.ServiceSubject), new("scope", InvoiceCreationDelegationVerifier.Scope),
             new("quotation_id", "84"), new("operation_id", operation.ToString("D")), new("jti", Guid.NewGuid().ToString("D")),
             new("iat", issued.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)],
            issued.UtcDateTime, issued.AddSeconds(120).UtcDateTime, new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256));
        var header = "Bearer " + new JwtSecurityTokenHandler().WriteToken(token);
        InvoiceCreationController Controller(InvoiceDbContext database)
        {
            var context = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim("sub", origin.ServiceSubject), new Claim("identity_kind", "service")], "test")) };
            context.Request.Headers[InvoiceCreationDelegationVerifier.HeaderName] = header;
            return new(workflow.Object, new(configuration, TimeProvider.System), new(database))
            { ControllerContext = new() { HttpContext = context } };
        }
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var first = Controller(firstDatabase).PrepareAsync(84, request, operation.ToString("D"), budget.Token);
        try
        {
            await enteredFirst.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var duplicateCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var duplicate = Controller(duplicateDatabase).PrepareAsync(84, request, operation.ToString("D"), duplicateCancellation.Token);
            await enteredDuplicate.Task.WaitAsync(TimeSpan.FromSeconds(5));
            duplicateCancellation.Cancel();
            Assert.Equal(503, Assert.IsType<ObjectResult>((await duplicate).Result).StatusCode);
            var pending = await duplicateDatabase.InvoiceCreationAdmissions.AsNoTracking().SingleAsync();
            Assert.Equal("Pending", pending.State);
            Assert.Null(pending.FinancialOwnershipJson);
            Assert.Empty(await duplicateDatabase.Invoices.AsNoTracking().ToListAsync());
            releaseFirst.TrySetResult();
            var result = Assert.IsType<InvoiceFinancialOwnership>(Assert.IsType<OkObjectResult>((await first).Result).Value);
            Assert.Equal(operation, result.OperationId);
            Assert.Equal(result, await new InvoiceFinancialOwnershipStore(duplicateDatabase).ReadAsync(operation, budget.Token));
            Assert.Single(await duplicateDatabase.Invoices.AsNoTracking().ToListAsync());
            Assert.Single(await duplicateDatabase.Items.AsNoTracking().ToListAsync());
            Assert.Equal("Pending", (await duplicateDatabase.InvoiceCreationAdmissions.AsNoTracking().SingleAsync()).State);
            workflow.Verify(value => value.PrepareFinancialAsync(84, request, operation, origin, It.IsAny<CancellationToken>()), Times.Once);
            workflow.Verify(value => value.ReadPreparedFinancialAsync(84, operation, origin, It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            releaseFirst.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}

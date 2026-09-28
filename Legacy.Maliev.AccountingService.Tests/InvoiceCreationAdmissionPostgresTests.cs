using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Api.Controllers.Invoice;
using Legacy.Maliev.AccountingService.Application.Models;
using Legacy.Maliev.AccountingService.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class InvoiceCreationAdmissionPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await using var context = Context();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task FirstAdmissionCompletedReplayAndActorBodyConflict_AreDurableAcrossContexts()
    {
        var operation = Guid.NewGuid();
        var fingerprint = InvoiceCreationAdmissionStore.Fingerprint(Request());
        await using (var first = Context())
        {
            var admissions = new InvoiceCreationAdmissionStore(first);
            Assert.True((await admissions.AdmitAsync(operation, 84, "employee:42", "service:legacy-intranet", fingerprint, CancellationToken.None)).IsNew);
            await admissions.CompleteAsync(operation, new InvoiceCreationResult(901, InvoiceCreationState.Completed,
                InvoiceCreationEmailState.NotRequested, null, new("maliev.com", "invoice.pdf")), CancellationToken.None);
        }
        await using (var replay = Context())
        {
            var admissions = new InvoiceCreationAdmissionStore(replay);
            Assert.Equal(901, (await admissions.AdmitAsync(operation, 84, "employee:42", "service:legacy-intranet", fingerprint, CancellationToken.None)).Completed?.InvoiceId);
            await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => admissions.AdmitAsync(operation, 84, "employee:99", "service:legacy-intranet", fingerprint, CancellationToken.None));
            await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => admissions.AdmitAsync(operation, 85, "employee:42", "service:legacy-intranet", fingerprint, CancellationToken.None));
            await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => admissions.AdmitAsync(operation, 84, "employee:42", "service:legacy-intranet", new string('A', 64), CancellationToken.None));
        }
    }

    [Fact]
    public async Task PendingAndFailedAdmissions_FailClosedAcrossContexts()
    {
        var pending = Guid.NewGuid();
        var failed = Guid.NewGuid();
        var fingerprint = InvoiceCreationAdmissionStore.Fingerprint(Request());
        await using (var first = Context())
        {
            var admissions = new InvoiceCreationAdmissionStore(first);
            await admissions.AdmitAsync(pending, 84, "employee:42", "service:legacy-intranet", fingerprint, CancellationToken.None);
            await admissions.AdmitAsync(failed, 84, "employee:42", "service:legacy-intranet", fingerprint, CancellationToken.None);
            await admissions.MarkUncertainAsync(failed, CancellationToken.None);
        }
        await using (var second = Context())
        {
            var admissions = new InvoiceCreationAdmissionStore(second);
            await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => admissions.AdmitAsync(pending, 84, "employee:42", "service:legacy-intranet", fingerprint, CancellationToken.None));
            await Assert.ThrowsAsync<InvoiceCreationConflictException>(() => admissions.AdmitAsync(failed, 84, "employee:42", "service:legacy-intranet", fingerprint, CancellationToken.None));
        }
    }

    [Fact]
    public async Task ConcurrentSameOperation_AdmitsOnlyOneCaller()
    {
        var operation = Guid.NewGuid();
        var fingerprint = InvoiceCreationAdmissionStore.Fingerprint(Request());
        async Task<bool> Attempt()
        {
            await using var context = Context();
            try
            {
                return (await new InvoiceCreationAdmissionStore(context).AdmitAsync(operation, 84,
                    "employee:42", "service:legacy-intranet", fingerprint, CancellationToken.None)).IsNew;
            }
            catch (InvoiceCreationConflictException) { return false; }
        }

        var results = await Task.WhenAll(Attempt(), Attempt());

        Assert.Single(results, value => value);
    }

    [Fact]
    public async Task Controller_ValidDelegationCallsWorkflowOnce_ThenReplaysBoundResult()
    {
        using var rsa = RSA.Create(2048);
        var operation = Guid.NewGuid();
        var workflow = new Mock<Legacy.Maliev.AccountingService.Application.Interfaces.IInvoiceCreationWorkflow>(MockBehavior.Strict);
        var completed = new InvoiceCreationResult(902, InvoiceCreationState.Completed,
            InvoiceCreationEmailState.NotRequested, null, new("maliev.com", "invoice.pdf"));
        workflow.Setup(value => value.CreateAsync(84, It.IsAny<CreateInvoiceFromQuotationRequest>(), operation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(completed);

        async Task<ActionResult<InvoiceCreationResult>> Call()
        {
            await using var database = Context();
            var verifier = new InvoiceCreationDelegationVerifier(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:Issuer"] = "legacy-auth",
                    ["Jwt:PublicKey"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())),
                }).Build(), TimeProvider.System);
            var controller = new InvoiceCreationController(workflow.Object, verifier, new InvoiceCreationAdmissionStore(database))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("sub", InvoiceCreationDelegationVerifier.IntranetSubject), new Claim("identity_kind", "service"),
            ], "test"));
            controller.HttpContext.Request.Headers[InvoiceCreationDelegationVerifier.HeaderName] = "Bearer " + Delegation(rsa, operation);
            return await controller.CreateAsync(84, Request(), operation.ToString("D"), CancellationToken.None);
        }

        Assert.Equal(902, Assert.IsType<InvoiceCreationResult>(Assert.IsType<OkObjectResult>((await Call()).Result).Value).InvoiceId);
        Assert.Equal(902, Assert.IsType<InvoiceCreationResult>(Assert.IsType<OkObjectResult>((await Call()).Result).Value).InvoiceId);
        workflow.Verify(value => value.CreateAsync(84, It.IsAny<CreateInvoiceFromQuotationRequest>(), operation, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static string Delegation(RSA rsa, Guid operation)
    {
        var now = DateTimeOffset.UtcNow;
        var token = new JwtSecurityToken("legacy-auth", InvoiceCreationDelegationVerifier.Audience,
        [
            new(JwtRegisteredClaimNames.Sub, "employee:42"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("azp", InvoiceCreationDelegationVerifier.IntranetSubject),
            new("scope", InvoiceCreationDelegationVerifier.Scope),
            new("quotation_id", "84"), new("operation_id", operation.ToString("D")),
        ], now.UtcDateTime, now.AddSeconds(120).UtcDateTime,
        new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private InvoiceDbContext Context() => new(new DbContextOptionsBuilder<InvoiceDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
    private static CreateInvoiceFromQuotationRequest Request() => new("INV-84", null, null, null, null, null, null,
        new(null, null, null, null, null, null, null, null, null),
        new(null, null, null, null, null, null, null, null, null), null, null, false, false);
}

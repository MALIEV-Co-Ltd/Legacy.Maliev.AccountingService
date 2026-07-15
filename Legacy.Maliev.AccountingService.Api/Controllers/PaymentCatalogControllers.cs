using Legacy.Maliev.AccountingService.Api.Authorization;
using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Domain.Payment;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.AccountingService.Api.Controllers.Payment;

[ApiController, Route("payments/[controller]"), Authorize]
public sealed class AccountsController(IAccountingService service, IIdempotencyStore idempotency) : CrudController<Account>(service, idempotency)
{
    [HttpPost, RequirePermission(AccountingPermissions.Create, RequireLiveCheck = true)]
    public Task<ActionResult<Account>> CreateAccountAsync(Account item, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken cancellationToken) => Create("accounts", item, "GetAccount", new { accountId = 0 }, key, cancellationToken);
    [HttpDelete("{accountId:int}"), RequirePermission(AccountingPermissions.Delete, RequireLiveCheck = true)]
    public Task<IActionResult> DeleteAccountAsync(int accountId, CancellationToken cancellationToken) => Delete(accountId, cancellationToken);
    [HttpGet("{accountId:int}", Name = "GetAccount"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public Task<ActionResult<Account>> GetAccountAsync(int accountId, CancellationToken cancellationToken) => Get(accountId, cancellationToken);
    [HttpGet, RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public Task<ActionResult<IReadOnlyList<Account>>> GetAllAccountsAsync(CancellationToken cancellationToken) => List(cancellationToken);
    [HttpPut("{accountId:int}"), RequirePermission(AccountingPermissions.Update, RequireLiveCheck = true)]
    public Task<IActionResult> UpdateAccountAsync(int accountId, Account item, [FromHeader(Name = "If-Unmodified-Since")] DateTimeOffset? expected, CancellationToken cancellationToken) => Update(accountId, item, expected, cancellationToken);
}

[ApiController, Route("payments/[controller]"), Authorize]
public sealed class DirectionsController(IAccountingService service, IIdempotencyStore idempotency) : CrudController<PaymentDirection>(service, idempotency)
{
    [HttpPost, RequirePermission(AccountingPermissions.Create, RequireLiveCheck = true)]
    public Task<ActionResult<PaymentDirection>> CreatePaymentDirectionAsync(PaymentDirection item, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken cancellationToken) => Create("directions", item, "GetPaymentDirection", new { paymentDirectionId = 0 }, key, cancellationToken);
    [HttpDelete("{paymentDirectionId:int}"), RequirePermission(AccountingPermissions.Delete, RequireLiveCheck = true)]
    public Task<IActionResult> DeletePaymentDirectionAsync(int paymentDirectionId, CancellationToken cancellationToken) => Delete(paymentDirectionId, cancellationToken);
    [HttpGet("{paymentDirectionId:int}", Name = "GetPaymentDirection"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public Task<ActionResult<PaymentDirection>> GetPaymentDirectionAsync(int paymentDirectionId, CancellationToken cancellationToken) => Get(paymentDirectionId, cancellationToken);
    [HttpGet, RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public Task<ActionResult<IReadOnlyList<PaymentDirection>>> GetPaymentDirectionsAsync(CancellationToken cancellationToken) => List(cancellationToken);
    [HttpPut("{paymentDirectionId:int}"), RequirePermission(AccountingPermissions.Update, RequireLiveCheck = true)]
    public Task<IActionResult> UpdatePaymentDirectionAsync(int paymentDirectionId, PaymentDirection item, [FromHeader(Name = "If-Unmodified-Since")] DateTimeOffset? expected, CancellationToken cancellationToken) => Update(paymentDirectionId, item, expected, cancellationToken);
}

[ApiController, Route("payments/[controller]"), Authorize]
public sealed class MethodsController(IAccountingService service, IIdempotencyStore idempotency) : CrudController<PaymentMethod>(service, idempotency)
{
    [HttpPost, RequirePermission(AccountingPermissions.Create, RequireLiveCheck = true)]
    public Task<ActionResult<PaymentMethod>> CreatePaymentMethodAsync(PaymentMethod item, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken cancellationToken) => Create("methods", item, "GetPaymentMethod", new { paymentMethodId = 0 }, key, cancellationToken);
    [HttpDelete("{paymentMethodId:int}"), RequirePermission(AccountingPermissions.Delete, RequireLiveCheck = true)]
    public Task<IActionResult> DeletePaymentMethodAsync(int paymentMethodId, CancellationToken cancellationToken) => Delete(paymentMethodId, cancellationToken);
    [HttpGet("{paymentMethodId:int}", Name = "GetPaymentMethod"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public Task<ActionResult<PaymentMethod>> GetPaymentMethodAsync(int paymentMethodId, CancellationToken cancellationToken) => Get(paymentMethodId, cancellationToken);
    [HttpGet, RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public Task<ActionResult<IReadOnlyList<PaymentMethod>>> GetPaymentMethodsAsync(CancellationToken cancellationToken) => List(cancellationToken);
    [HttpPut("{paymentMethodId:int}"), RequirePermission(AccountingPermissions.Update, RequireLiveCheck = true)]
    public Task<IActionResult> UpdatePaymentMethodAsync(int paymentMethodId, PaymentMethod item, [FromHeader(Name = "If-Unmodified-Since")] DateTimeOffset? expected, CancellationToken cancellationToken) => Update(paymentMethodId, item, expected, cancellationToken);
}

[ApiController, Route("payments/[controller]"), Authorize]
public sealed class TypesController(IAccountingService service, IIdempotencyStore idempotency) : CrudController<PaymentType>(service, idempotency)
{
    [HttpPost, RequirePermission(AccountingPermissions.Create, RequireLiveCheck = true)]
    public Task<ActionResult<PaymentType>> CreatePaymentTypeAsync(PaymentType item, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken cancellationToken) => Create("types", item, "GetPaymentType", new { paymentTypeId = 0 }, key, cancellationToken);
    [HttpDelete("{paymentTypeId:int}"), RequirePermission(AccountingPermissions.Delete, RequireLiveCheck = true)]
    public Task<IActionResult> DeletePaymentTypeAsync(int paymentTypeId, CancellationToken cancellationToken) => Delete(paymentTypeId, cancellationToken);
    [HttpGet("{paymentTypeId:int}", Name = "GetPaymentType"), RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public Task<ActionResult<PaymentType>> GetPaymentTypeAsync(int paymentTypeId, CancellationToken cancellationToken) => Get(paymentTypeId, cancellationToken);
    [HttpGet, RequirePermission(AccountingPermissions.Read, RequireLiveCheck = true)]
    public Task<ActionResult<IReadOnlyList<PaymentType>>> GetPaymentTypesAsync(CancellationToken cancellationToken) => List(cancellationToken);
    [HttpPut("{paymentTypeId:int}"), RequirePermission(AccountingPermissions.Update, RequireLiveCheck = true)]
    public Task<IActionResult> UpdatePaymentTypeAsync(int paymentTypeId, PaymentType item, [FromHeader(Name = "If-Unmodified-Since")] DateTimeOffset? expected, CancellationToken cancellationToken) => Update(paymentTypeId, item, expected, cancellationToken);
}

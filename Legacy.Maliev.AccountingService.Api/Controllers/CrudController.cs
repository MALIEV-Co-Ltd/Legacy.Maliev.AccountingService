using Legacy.Maliev.AccountingService.Application.Interfaces;
using Legacy.Maliev.AccountingService.Application.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Legacy.Maliev.AccountingService.Api.Controllers;

public abstract class CrudController<T>(IAccountingService service, IIdempotencyStore idempotency) : ControllerBase where T : class
{
    protected IAccountingService Service { get; } = service;

    protected async Task<ActionResult<T>> Create(string scope, T item, string route, object routeValues, string? key, CancellationToken cancellationToken)
    {
        var value = await IdempotentCreates.GetOrCreateAsync(idempotency, scope, key,
            () => Service.CreateAsync(item, cancellationToken), cancellationToken);
        var values = new RouteValueDictionary(routeValues);
        var routeParameter = values.Keys.Single();
        values[routeParameter] = typeof(T).GetProperty("Id")?.GetValue(value);
        return CreatedAtRoute(route, values, value);
    }

    protected async Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        await Service.DeleteAsync<T>(id, cancellationToken) ? NoContent() : NotFound();

    protected async Task<ActionResult<T>> Get(int id, CancellationToken cancellationToken)
    {
        var value = await Service.GetAsync<T>(id, cancellationToken);
        return value is null ? NotFound() : value;
    }

    protected async Task<ActionResult<IReadOnlyList<T>>> List(CancellationToken cancellationToken) =>
        Ok(await Service.ListAsync<T>(cancellationToken));

    protected async Task<IActionResult> Update(int id, T item, DateTimeOffset? expected, CancellationToken cancellationToken) =>
        await Service.UpdateAsync(id, item, expected, cancellationToken) switch
        {
            UpdateResult.Updated => NoContent(),
            UpdateResult.NotFound => NotFound(),
            _ => Conflict(),
        };
}

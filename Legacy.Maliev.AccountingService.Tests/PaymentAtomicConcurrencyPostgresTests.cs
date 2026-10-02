using Legacy.Maliev.AccountingService.Domain.Payment;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Legacy.Maliev.AccountingService.Tests;

[Collection(AccountingBoundaryHttpCollection.Name)]
public sealed class PaymentAtomicConcurrencyPostgresTests(AccountingBoundaryHttpFixture fixture)
{
    [Fact]
    public async Task Payment_TwoContextsRejectLostUpdateAndRetainFirstFinancialWrite()
    {
        await fixture.ResetAsync();
        int id;
        await using (var seed = fixture.Database())
        {
            var payment = new Payment
            {
                PaymentDirectionId = 100000,
                PaymentMethodId = 100000,
                PaymentTypeId = 100000,
                Description = "Atomic synthetic payment",
                Recipient = "Synthetic",
                TransactionNumber = "ATOMIC-ONLY",
                Amount = 10m,
                ModifiedDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Unspecified),
            };
            seed.Payments.Add(payment);
            await seed.SaveChangesAsync();
            id = payment.Id;
        }

        await using var first = fixture.Database();
        await using var second = fixture.Database();
        // Both real connections materialize the same version BEFORE either write.
        var winner = await first.Payments.SingleAsync(row => row.Id == id);
        var loser = await second.Payments.SingleAsync(row => row.Id == id);
        Assert.Equal(winner.ModifiedDate, loser.ModifiedDate);
        winner.Amount = 111.25m;
        winner.ModifiedDate = new DateTime(2026, 10, 1, 0, 1, 0, DateTimeKind.Unspecified);
        await first.SaveChangesAsync();
        await using (var readback = fixture.Database())
            Assert.Equal(111.25m, (await readback.Payments.AsNoTracking().SingleAsync(row => row.Id == id)).Amount);

        loser.Amount = 999999m;
        loser.ModifiedDate = new DateTime(2026, 10, 1, 0, 2, 0, DateTimeKind.Unspecified);
        var failure = await Record.ExceptionAsync(() => second.SaveChangesAsync());
        await using var final = fixture.Database();
        var persisted = await final.Payments.AsNoTracking().SingleAsync(row => row.Id == id);
        Assert.Equal(111.25m, persisted.Amount);
        Assert.Equal(winner.ModifiedDate, persisted.ModifiedDate);
        Assert.IsType<DbUpdateConcurrencyException>(failure);
    }

    [Fact]
    public async Task Payment_ConcurrencyMetadataMatchesSnapshotWithoutRelationalDdl()
    {
        await fixture.ResetAsync();
        await using var database = fixture.Database();
        var model = database.GetService<IDesignTimeModel>().Model;
        var payment = model.FindEntityType(typeof(Payment))!;
        Assert.True(payment.FindProperty(nameof(Payment.ModifiedDate))!.IsConcurrencyToken);
        Assert.Equal("timestamp without time zone", payment.FindProperty(nameof(Payment.ModifiedDate))!.GetColumnType());
        Assert.DoesNotContain(payment.GetProperties(), property => property.Name is "xmin" or "Version" or "RowVersion");
        var snapshot = database.GetService<IMigrationsAssembly>().ModelSnapshot
            ?? throw new InvalidOperationException("The Payment snapshot must exist.");
        var snapshotModel = database.GetService<IModelRuntimeInitializer>().Initialize(snapshot.Model, designTime: true);
        var snapshotPayment = snapshotModel.FindEntityType(payment.Name)!;
        Assert.True(snapshotPayment.FindProperty(nameof(Payment.ModifiedDate))!.IsConcurrencyToken);
        var changes = database.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshotModel.GetRelationalModel(), model.GetRelationalModel());
        Assert.Empty(changes);
        Assert.False(database.Database.HasPendingModelChanges());
        Assert.NotEmpty(await database.Database.GetAppliedMigrationsAsync());
    }
}

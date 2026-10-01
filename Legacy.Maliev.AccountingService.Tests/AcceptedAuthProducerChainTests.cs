using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Legacy.Maliev.AccountingService.Tests.Fixtures;
using Npgsql;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.AccountingService.Tests;

public sealed class AcceptedAuthProducerChainTests(InvoiceDelegationChainFixture fixture) : IClassFixture<InvoiceDelegationChainFixture>
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("database", 1)]
    [InlineData("host", 1)]
    [InlineData("pooling", 1)]
    [InlineData("authority", 1)]
    public async Task OwnerUtility_ExactContainerGuardRunsBeforeAnyMigration(string? mutation, int exitCode)
    {
        Assert.Equal(exitCode, await fixture.ValidateUtilityGuard(mutation));
        await using var employee = fixture.OpenProofConnection("employee");
        await employee.OpenAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM \"AspNetUsers\"", employee);
        Assert.Equal(2L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task FourOwnerDatabases_AreDistinctAndHaveActualMigrationHistory()
    {
        var databases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in new[] { "invoice", "customer", "employee", "state" })
        {
            await using var connection = fixture.OpenProofConnection(name);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT current_database(), (SELECT count(*) FROM \"__EFMigrationsHistory\")", connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(databases.Add(reader.GetString(0)));
            Assert.True(reader.GetInt64(1) > 0);
        }
        Assert.Equal(4, databases.Count);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("99")]
    public async Task NormalEmployeeLogin_HasPersistedSessionOwnerFamilyAndMatchingIdentityStamp(string id)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(fixture.EmployeeAccessToken("employee:" + id));
        Assert.Equal("employee:" + id, Assert.Single(jwt.Claims, c => c.Type == "sub").Value);
        Assert.Equal("employee", Assert.Single(jwt.Claims, c => c.Type == "identity_kind").Value);
        var sid = Guid.ParseExact(Assert.Single(jwt.Claims, c => c.Type == "sid").Value, "D");
        await using var state = fixture.OpenProofConnection("state");
        await state.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT \"IdentityId\", \"IdentityKind\", \"SecurityStamp\", \"FamilyId\", \"RevokedAt\", \"ExpiresAt\", \"TokenHash\" FROM refresh_sessions WHERE \"Id\"=@sid", state);
        command.Parameters.AddWithValue("sid", sid);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("employee:" + id, reader.GetString(0));
        Assert.Equal(1, reader.GetInt32(1));
        var stamp = reader.GetString(2);
        Assert.NotEqual(Guid.Empty, reader.GetGuid(3));
        Assert.True(reader.IsDBNull(4));
        Assert.True(reader.GetDateTime(5) > DateTime.UtcNow);
        Assert.NotEmpty(reader.GetString(6));
        Assert.False(await reader.ReadAsync());
        await using var employee = fixture.OpenProofConnection("employee");
        await employee.OpenAsync();
        await using var identity = new NpgsqlCommand("SELECT \"SecurityStamp\", \"EmailConfirmed\" FROM \"AspNetUsers\" WHERE \"Id\"=@id", employee);
        identity.Parameters.AddWithValue("id", "employee:" + id);
        await using var row = await identity.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal(stamp, row.GetString(0));
        Assert.True(row.GetBoolean(1));
    }

    [Fact]
    public async Task ActualServiceLogin_AndEmployeeSessionProduceNarrowInvoiceDelegation()
    {
        var service = new JwtSecurityTokenHandler().ReadJwtToken(fixture.Ordinary(InvoiceDelegationChainFixture.Service, "service", InvoiceDelegationChainFixture.Permission));
        Assert.Equal(InvoiceDelegationChainFixture.Service, Assert.Single(service.Claims, c => c.Type == "sub").Value);
        Assert.Equal("service", Assert.Single(service.Claims, c => c.Type == "identity_kind").Value);
        Assert.Contains(service.Claims, c => c.Type == "permissions" && c.Value == "legacy-auth.invoice-delegation.issue");
        var response = await fixture.Exchange(Guid.NewGuid());
        var delegation = new JwtSecurityTokenHandler().ReadJwtToken(response.GetProperty("accessToken").GetString());
        Assert.Equal("legacy-accounting:invoice-create", Assert.Single(delegation.Audiences));
        Assert.Equal("legacy.accounting.create", Assert.Single(delegation.Claims, c => c.Type == "scope").Value);
        Assert.DoesNotContain(delegation.Claims, c => c.Type == "permissions" && c.Value.Contains("decision", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RealRevocation_BlocksFreshExchangeBeforeAccountingAdmissionOrEffects()
    {
        // Separate fixture: no mutation of shared positive or original 17-test lineage.
        var isolated = new InvoiceDelegationChainFixture();
        try
        {
            await isolated.InitializeAsync();
            var token = new JwtSecurityTokenHandler().ReadJwtToken(isolated.EmployeeAccessToken("employee:42"));
            var sid = Guid.ParseExact(Assert.Single(token.Claims, c => c.Type == "sid").Value, "D");
            await using var state = isolated.OpenProofConnection("state");
            await state.OpenAsync();
            await using var revoke = new NpgsqlCommand("UPDATE refresh_sessions SET \"RevokedAt\"=CURRENT_TIMESTAMP WHERE \"FamilyId\"=(SELECT \"FamilyId\" FROM refresh_sessions WHERE \"Id\"=@sid)", state);
            revoke.Parameters.AddWithValue("sid", sid);
            Assert.Equal(1, await revoke.ExecuteNonQueryAsync());
            Assert.Equal(HttpStatusCode.Unauthorized, await isolated.ExchangeStatus(Guid.NewGuid(), "employee:42"));
            Assert.Empty(isolated.LiveChecks);
            Assert.Equal(0, isolated.Effects);
            await using var invoice = isolated.Database();
            Assert.Equal(0, await invoice.InvoiceCreationAdmissions.CountAsync());
        }
        finally { await isolated.DisposeAsync(); }
    }
}

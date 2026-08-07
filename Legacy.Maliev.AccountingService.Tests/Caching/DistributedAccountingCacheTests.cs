using Legacy.Maliev.AccountingService.Data;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Legacy.Maliev.AccountingService.Tests.Caching;

public sealed class DistributedAccountingCacheTests
{
    [Fact]
    public async Task GetAsync_does_not_swallow_cancellation()
    {
        var cancellationToken = new CancellationToken(canceled: true);
        var distributed = new Mock<IDistributedCache>();
        distributed.Setup(value => value.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancellationToken));
        var cache = new DistributedAccountingCache(distributed.Object, NullLogger<DistributedAccountingCache>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => cache.GetAsync<TestValue>("key", cancellationToken));
    }

    [Fact]
    public async Task SetAsync_does_not_swallow_cancellation()
    {
        var cancellationToken = new CancellationToken(canceled: true);
        var distributed = new Mock<IDistributedCache>();
        distributed.Setup(value => value.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancellationToken));
        var cache = new DistributedAccountingCache(distributed.Object, NullLogger<DistributedAccountingCache>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => cache.SetAsync("key", new TestValue("value"), TimeSpan.FromMinutes(1), cancellationToken));
    }

    [Fact]
    public async Task RemoveAsync_does_not_swallow_cancellation()
    {
        var cancellationToken = new CancellationToken(canceled: true);
        var distributed = new Mock<IDistributedCache>();
        distributed.Setup(value => value.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancellationToken));
        var cache = new DistributedAccountingCache(distributed.Object, NullLogger<DistributedAccountingCache>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() => cache.RemoveAsync("key", cancellationToken));
    }

    private sealed record TestValue(string Value);
}

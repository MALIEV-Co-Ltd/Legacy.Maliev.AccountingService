using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Legacy.Maliev.AccountingService.Data")]

namespace Legacy.Maliev.AccountingService.Application.Models;

/// <summary>Single-use admission capability constructed only by acknowledged durable authority.</summary>
public sealed class InvoiceNotificationAdmissionPermit
{
    private int consumed;
    internal InvoiceNotificationAdmissionPermit(InvoiceNotificationCorrelation correlation, string payloadDigest)
    {
        Correlation = correlation;
        PayloadDigest = payloadDigest;
    }
    /// <summary>Immutable identity and phase bound to this capability.</summary>
    public InvoiceNotificationCorrelation Correlation { get; }
    /// <summary>Original validated payload digest bound to this capability.</summary>
    public string PayloadDigest { get; }
    /// <summary>Claims this instance once; a retained snapshot cannot recreate it.</summary>
    public bool TryConsume() => Interlocked.CompareExchange(ref consumed, 1, 0) == 0;
}

/// <summary>Single-use execution capability constructed only by acknowledged durable authority.</summary>
public sealed class InvoiceNotificationExecutionPermit
{
    private int consumed;
    internal InvoiceNotificationExecutionPermit(InvoiceNotificationCorrelation correlation, string payloadDigest)
    {
        Correlation = correlation;
        PayloadDigest = payloadDigest;
    }
    /// <summary>Immutable identity and phase bound to this capability.</summary>
    public InvoiceNotificationCorrelation Correlation { get; }
    /// <summary>Original validated payload digest bound to this capability.</summary>
    public string PayloadDigest { get; }
    /// <summary>Claims this instance once; reads and remote receipt replay cannot recreate it.</summary>
    public bool TryConsume() => Interlocked.CompareExchange(ref consumed, 1, 0) == 0;
}

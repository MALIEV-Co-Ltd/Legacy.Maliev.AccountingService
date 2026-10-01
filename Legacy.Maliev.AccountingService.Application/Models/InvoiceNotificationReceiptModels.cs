namespace Legacy.Maliev.AccountingService.Application.Models;

/// <summary>Producer wire observation; this type does not authenticate its transport or originating actor.</summary>
public sealed record InvoiceNotificationReceiptObservation(
    string IntentId, string Purpose, string ResourceType, string ResourceId,
    string WorkflowOperationId, string State, long Version,
    DateTimeOffset? AdmittedAt, DateTimeOffset? UpdatedAt, string? ProviderMessageId);

/// <summary>Continuity classification only; never an admission or execution permit.</summary>
public enum InvoiceNotificationReceiptProgression { First, Duplicate, Advance, Conflict }

/// <summary>Independently owned retained receipt continuity evidence, without raw provider identifiers.</summary>
public sealed class InvoiceNotificationRetainedReceipt
{
    private readonly byte[] binding;

    public InvoiceNotificationRetainedReceipt(string state, long version, DateTimeOffset admittedAt,
        DateTimeOffset updatedAt, byte[] receiptBinding)
    {
        State = state;
        Version = version;
        AdmittedAt = admittedAt;
        UpdatedAt = updatedAt;
        binding = receiptBinding.ToArray();
    }

    public string State { get; }
    public long Version { get; }
    public DateTimeOffset AdmittedAt { get; }
    public DateTimeOffset UpdatedAt { get; }
    public byte[] Binding => binding.ToArray();
}

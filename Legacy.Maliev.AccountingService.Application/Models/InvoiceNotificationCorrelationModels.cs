namespace Legacy.Maliev.AccountingService.Application.Models;

/// <summary>Validated originating delegation; never recipient or document payload.</summary>
public sealed record InvoiceNotificationOrigin(string Issuer, string EmployeeSubject, string ServiceSubject);

/// <summary>Immutable invoice-purpose correlation identity, independent of financial values.</summary>
public sealed record InvoiceNotificationCorrelationIdentity(
    Guid IntentId, int InvoiceId, string Purpose, int QuotationId, Guid WorkflowOperationId,
    InvoiceNotificationOrigin Origin, string SenderIssuer, string SenderServiceSubject,
    string PayloadFrameVersion, string BindingVersion);

/// <summary>A retained authority snapshot; observing it never grants an execution permit.</summary>
public sealed record InvoiceNotificationCorrelation
{
    private readonly byte[] payloadBinding;

    public InvoiceNotificationCorrelation(InvoiceNotificationCorrelationIdentity identity, string bindingKeyId,
        byte[] binding, string phase, long version, long? remoteVersion, DateTimeOffset createdAt,
        DateTimeOffset updatedAt, DateTimeOffset? admissionIssuedAt, DateTimeOffset? executionIssuedAt)
    {
        Identity = identity;
        BindingKeyId = bindingKeyId;
        payloadBinding = binding.ToArray();
        Phase = phase;
        Version = version;
        RemoteVersion = remoteVersion;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        AdmissionIssuedAt = admissionIssuedAt;
        ExecutionIssuedAt = executionIssuedAt;
    }

    public InvoiceNotificationCorrelationIdentity Identity { get; }
    public string BindingKeyId { get; }
    public byte[] PayloadBinding => payloadBinding.ToArray();
    public string Phase { get; }
    public long Version { get; }
    public long? RemoteVersion { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }
    public DateTimeOffset? AdmissionIssuedAt { get; }
    public DateTimeOffset? ExecutionIssuedAt { get; }
}

/// <summary>Fixed local unavailable category; internal causes must not be logged or serialized.</summary>
public sealed class InvoiceNotificationCorrelationUnavailableException(Exception? innerException = null)
    : Exception("Invoice notification correlation is unavailable.", innerException);

/// <summary>Fixed immutable identity or stale state conflict, without disclosing the winner.</summary>
public sealed class InvoiceNotificationCorrelationConflictException()
    : Exception("Invoice notification correlation conflicts with retained authority.");

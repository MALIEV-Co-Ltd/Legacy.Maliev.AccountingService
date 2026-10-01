namespace Legacy.Maliev.AccountingService.Data;

/// <summary>Non-expiring scalar authority; no invoice cascade or persisted notification payload.</summary>
public sealed class InvoiceNotificationCorrelationRow
{
    public Guid IntentId { get; set; }
    public int InvoiceId { get; set; }
    public required string Purpose { get; set; }
    public int QuotationId { get; set; }
    public Guid WorkflowOperationId { get; set; }
    public required string OriginIssuer { get; set; }
    public required string OriginEmployeeSubject { get; set; }
    public required string OriginServiceSubject { get; set; }
    public required string SenderIssuer { get; set; }
    public required string SenderServiceSubject { get; set; }
    public required string PayloadFrameVersion { get; set; }
    public required string BindingVersion { get; set; }
    public required string BindingKeyId { get; set; }
    public required byte[] PayloadBinding { get; set; }
    public required string Phase { get; set; }
    public long Version { get; set; }
    public long? RemoteVersion { get; set; }
    public string? RemoteState { get; set; }
    public DateTimeOffset? RemoteAdmittedAt { get; set; }
    public DateTimeOffset? RemoteUpdatedAt { get; set; }
    public byte[]? RemoteReceiptBinding { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? AdmissionIssuedAt { get; set; }
    public DateTimeOffset? ExecutionIssuedAt { get; set; }
}

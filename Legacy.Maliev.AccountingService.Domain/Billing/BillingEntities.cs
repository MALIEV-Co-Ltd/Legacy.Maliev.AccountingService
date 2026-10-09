namespace Legacy.Maliev.AccountingService.Domain.Billing;

public sealed class BillingAccountRow
{
    public Guid Id { get; set; }
    public int QuotationId { get; set; }
    public int CustomerId { get; set; }
    public long Revision { get; set; }
    public string StateJson { get; set; } = string.Empty;
}
public sealed class BillingOperationRow
{
    public Guid OperationId { get; set; }
    public Guid AccountId { get; set; }
    public int EmployeeId { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public string CommandJson { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}
public sealed class BillingIntentRow
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid OperationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string State { get; set; } = "Pending";
}

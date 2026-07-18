# Legacy.Maliev.AccountingService

Public, sanitized .NET 10 extraction of the private legacy Payment, Invoice, and Receipt APIs. It
preserves the extracted controller contracts across `/payments`, `/invoices`, and
`/receipts`, while deliberately excluding payment-provider execution. The future Omise/Opn
implementation remains owned by the separate new `Maliev.PaymentService`.

The service uses Scalar/OpenAPI, JWT permission checks, Redis read caching, SHA-256-keyed create
idempotency, conditional updates, and local .NET logging focused on warnings and failures. Financial
amounts retain decimal precision; invoice/receipt line subtotals and receipt amount paid remain
database-generated values.

## Architecture

The service uses clean dependency direction: `Api` calls `Application`, domain rules live in
`Domain`, and PostgreSQL/Redis adapters live in `Data`. It depends only on the public
`Legacy.Maliev.ServiceDefaults` and `Legacy.Maliev.CompatibilityContracts` source repositories
during CI and image builds, so the legacy runtime does not consume new-platform shared-library
source or private package credentials.

Receipt creation, reconciliation, removal, and explicit email delivery are owned by Accounting at
`/invoices/{invoiceId}/receipt`. Mutations require a caller-stable UUID `Idempotency-Key`; create
and email also require the BFF-derived `X-Legacy-Employee-Id`. Recipient and signature data are
never accepted from WASM: Accounting resolves the invoice customer from CustomerService and the
employee signature from EmployeeService, then downloads only the malware-scanned object through a
bounded Google Cloud Storage signed URL. Workload authentication uses the shared ServiceDefaults
token exchange and never forwards an inbound user bearer token.

Quotation-to-invoice creation is likewise server owned. The preview route derives customer,
employee, currency, addresses, totals, withholding tax, and immutable line items from their source
services. Creation accepts only editable invoice fields plus a caller-stable UUID, atomically writes
the existing Invoice and OrderItem tables, accepts and links the quotation, renders the QuestPDF
invoice, stores it through FileService, and optionally sends it through NotificationService. Redis
replay protection and a PostgreSQL advisory lock reconcile interrupted requests without a database
schema change; a reconciled invoice is not automatically re-emailed when delivery outcome is unknown.

## Data boundaries

- Planned `legacy-postgres-accounting` cluster in namespace `maliev-legacy`, using the existing
  CloudNativePG operator and existing GKE resources only.
- Database `Payment`: historical payments, accounts, directions, methods, types, and file metadata.
- Database `Invoice`: invoices, invoice order items, and invoice file metadata.
- Database `Receipt`: receipts, receipt order items, and receipt file metadata.
- Customer, Employee, Currency, Order, and other external identifiers remain scalar references. No
  cross-service database reads or writes are introduced.
- File records contain Google Cloud Storage bucket/object identities; workloads use ADC/Workload
  Identity and never store service-account keys in this repository.

Source SQL Server databases remain untouched. Extraction does not deploy. Cutover requires a
repeatable copy, row/financial parity, GCS reconciliation, application workflow tests, backup and
rollback evidence, dedicated Workload Identity, and GitOps manifests. No new node pool, Cloud SQL,
or paid GitHub feature is required.

## Validate

```powershell
dotnet build Legacy.Maliev.AccountingService.slnx -c Release
dotnet test Legacy.Maliev.AccountingService.slnx -c Release --no-build
dotnet format Legacy.Maliev.AccountingService.slnx --verify-no-changes --no-restore
dotnet list Legacy.Maliev.AccountingService.slnx package --vulnerable --include-transitive
gitleaks git . --redact=100 --exit-code 1 --no-banner --no-color
```

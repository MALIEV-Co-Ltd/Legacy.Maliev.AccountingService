# Split Billing Implementation Plan

**Goal:** Deliver usable full, deposit, installment and remaining billing with explicit settlement allocations and independent tax documents.
**Architecture:** Accounting owns the billing ledger beside Invoice persistence. Versioned commands preserve legacy routes, coherent quotation snapshots and independently verified immutable evidence. Payment, Receipt and file effects use durable acknowledgment and reconciliation.
**Technology:** .NET 10, PostgreSQL 18, Npgsql, Redis, existing Blazor and same-origin BFF, QuestPDF.
**Approved specification:** [Immutable staged-billing specification](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.AccountingService/blob/0ef14380273e1ab32197f678d2534d875f8cb0f4/docs/superpowers/specs/2026-10-08-staged-billing-design.md).
**Tracking:** [Accounting #79](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.AccountingService/issues/79), documentation review [PR #80](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.AccountingService/pull/80).
**Status:** Plan for review. The written specification is approved; this plan and product execution are not yet approved.

## Global constraints

- Full billing remains the default; no fixed split percentage.
- Same legal debtor and tax ID for head-office routing; change recipient/branch/address only.
- Verified shipment and release evidence is required for balance billing; customer-specific requirements also block issuance until verified. No MVP override.
- Capture factual tax points independently of commercial evidence gates; accountant-confirmed effective-dated classification/rates/policy before legal issuance.
- Preserve legacy routes, PascalCase wires, retained CLR/message identities and old PDF oracles.
- Other service identifiers are scalar references, never cross-domain foreign keys/DbContexts.
- No new payment providers, whole general ledger, whole NDA lifecycle, alternate ACL/storage system or new infrastructure.
- Product execution, persistent migration, deployment and activation require their respective reviews. A merged software slice does not authorize live issuance.

## Review focus

1. A required evidence version becomes unavailable after preview: issuance must fail closed while tax-event capture still works (Task 2/3).
2. A legacy paid checkbox or payment delete bypasses allocation: reject for onboarded records and preserve historical behavior (Task 4/7).
3. Crediting a paid stage leaves negative debt: release affected allocations to unapplied credit with explicit reversals (Task 4).
4. A lost document acknowledgment causes reissue with another legal number: retain the same operation and reconcile; never blindly rerender/reissue/send (Task 5).
5. A customer edits address or tax policy changes between draft and issuance: retain the same legal debtor and snapshot the confirmed current policy/version, without altering issued history (Task 3/5/6).

## Ownership and shared-file reservation

One Accounting owner coordinates the complete billing feature; each producer owner reviews its own contracts. Execute Tasks 1–7 sequentially at shared boundaries. Separate branches/PRs may contain independent renderer and UI work only after their consumed contracts are frozen.

The financial migration owner retains existing IInvoiceCreationBoundaries.cs, IReceiptWorkflowBoundaries.cs, InvoiceCreationWorkflowService.cs, InvoiceCreationStore.cs, financial ownership/delegation/completion files and their current acceptance work. Reserve coordinated edits to AccountingDbContexts.cs, Api/Program.cs, InvoiceControllers.cs, PaymentRecordControllers.cs and ReceiptControllers.cs before changing them. A coordination message to financial task 01a1166a-3d29-776b-90a4-b5e2a215a0a7 was successfully delivered during planning; ownership acknowledgment is pending. No competing writer starts until it arrives.

[Intranet #160](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/issues/160) owns collections and consumes the allocation ledger; [#159](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/issues/159) owns reports. Do not build separate allocation engines.
[Intranet #277](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/issues/277) owns document-registry design; [Order #61](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.OrderService/issues/61) coordinates release/replacement evidence. Their whole future features are not billing dependencies.

### Minimum evidence dependency

Freeze only this read contract with those owners: immutable DocumentId/VersionId, canonical CustomerId, applicable QuotationId/OrderIds, kind (Shipment, Release, Acceptance, BillingInstruction), content SHA-256, verified status/actor/time and verification revision. Billing stores association receipts and requirements, not another document registry.

Existing FileService signed-read capability already requires recorded clean-object evidence and read permission. Reuse it through an authorized server-side adapter for staff previews/downloads; URLs are transient. It does not prove customer association or shipment/release verification. The producer supplies those business facts. No member ACL or NDA approval is necessary for staff billing MVP.

The required producer deliverable is immutable version metadata plus authorized verification readback. Existing safe records may supply it if equivalent identity and immutability are proven; otherwise #277 must deliver this narrow metadata slice first. Its owning service and exact producer file paths require the document owner's reservation before implementation. This is an explicit placement decision, not permission for Accounting to duplicate registry ownership. Billing's exact consumer files are listed below. No specification boundary amendment is proposed.

## File map and common types

Paths use the existing project directories. Within Accounting, expand each project label exactly as follows: Application/ means Legacy.Maliev.AccountingService.Application/; Domain/ means Legacy.Maliev.AccountingService.Domain/; Data/ means Legacy.Maliev.AccountingService.Data/; Api/ means Legacy.Maliev.AccountingService.Api/; Tests/ means Legacy.Maliev.AccountingService.Tests/. For example, Application/Models/BillingModels.cs is Legacy.Maliev.AccountingService.Application/Models/BillingModels.cs. Quotation and DocumentService producer paths use the same rule with Legacy.Maliev.QuotationService or Legacy.Maliev.DocumentService respectively. Intranet prefixes are stated in Task 6. Repository-root docs/ and scripts/ are literal paths.

New type declarations belong to Application/Models/BillingModels.cs, except persistence entities in Domain/Billing/BillingEntities.cs. This map gives every proposed file a deterministic full repository path; generated migration filenames are assigned only when the migration is generated.

Contract types:
- BillingContext(int EmployeeId, Guid OperationId, long ExpectedRevision).
- BillingMoney(decimal Base, decimal Vat, decimal Gross, string Currency).
- BillingSnapshot(int QuotationId, int CustomerId, string TaxId, string Revision, string Digest, BillingMoney Cap, IReadOnlyList<BillingLine> Lines); BillingLine carries stable source line ID, base/VAT/gross and tax category.
- StageDraft(BillingStageKind Kind, decimal? Amount, decimal? Percentage, DateOnly? DueDate, BillingRecipient Recipient, IReadOnlyList<EvidenceRequirement> Requirements). Kind = Full/Deposit/Installment/Remaining. Recipient keeps legal CustomerId/TaxId and routes branch/address.
- EvidenceRequirement(EvidenceKind Kind, IReadOnlyList<int> OrderIds); EvidenceReference(Guid DocumentId, Guid VersionId); EvidenceReceipt adds canonical associations, digest, verification revision/status/actor/time.
- BillingAccountView contains account/revision, immutable snapshot, stages and separate billed/cash/withholding/outstanding/unbilled/VAT-recognized totals.
- BillingResult(Guid OperationId, Guid AccountId, Guid? StageId, long Revision, BillingOperationState State); states Completed/NeedsReconciliation.
- AllocationRequest(int PaymentId, string PaymentRevision, Guid StageId, decimal Cash, decimal Withholding, EvidenceReference? Certificate).
- BillingAdjustment(BillingAdjustmentKind Kind, decimal Amount, Guid? StageId, Guid? AllocationId, string Reason, string? ApprovedQuotationRevision); explicit kinds DraftCancellation, RebillCredit, CommercialReduction, AllocationReversal, Refund, CapAmendment.
- TaxPolicySnapshot captures effective dates, classification, approved rates/base/trigger/rounding/withholding rules and approval reference. TaxTrigger(Guid EventId, Guid AccountId, TaxTriggerKind Kind, DateTimeOffset OccurredAt, IReadOnlyList<TaxPortion> Portions); portions reference commercial lines/amounts.
- BillingDocumentRequest(Guid AccountId, Guid? StageId, BillingDocumentKind Kind, IReadOnlyList<Guid> AllocationIds, IReadOnlyList<Guid> TaxEventIds); kinds CommercialRequest/TaxInvoice/Receipt/CreditNote. IssuanceView carries immutable number, operation, snapshot/PDF digest and acknowledged phase.
- PaymentReservation(int PaymentId, string Revision, Guid OperationId, decimal Cash, string Currency, string EvidenceDigest, PaymentReservationState State).

These are proposed implementation contracts for plan review. Reject malformed/unknown enums and missing source identities; do not infer historical facts.

## Task 1: Billing ledger and deterministic financial rules

**Create:** Domain/Billing/BillingEntities.cs; Application/Models/BillingModels.cs; Application/Services/BillingAmountCalculator.cs; Application/Interfaces/IBillingLedger.cs; Data/BillingLedger.cs; Tests/BillingAmountTests.cs; Tests/BillingLedgerPostgresTests.cs.
**Modify by reservation:** Data/AccountingDbContexts.cs; Data/Migrations/Invoice/InvoiceDbContextModelSnapshot.cs. Generate one additive Invoice migration after model review; use generated timestamp/name, never manually invent a migration ID.
**Interfaces:** IBillingLedger.GetAsync(Guid accountId, CancellationToken) -> Task<BillingAccountView?>; ExecuteAsync(Guid accountId, BillingContext context, BillingLedgerCommand command, CancellationToken) -> Task<BillingResult>. BillingLedgerCommand is an internal discriminated command family for stage issue, allocation, adjustment and tax capture, defined in BillingModels.cs. BillingAmountCalculator.Allocate(BillingSnapshot snapshot, StageDraft draft, BillingAccountView current) -> BillingMoney.

- [ ] Write failing amount tests: gross1000, deposit200, issued remainder800; percentage25 yields250; sum of stage components equals approved cap after final residual. No production tax rates appear in fixtures.
- [ ] Run focused BillingAmountTests; confirm the intended assertion fails.
- [ ] Implement amount allocation and ledger persistence with database cap constraints, account-row serialization, unique operation/fingerprint receipts, append-only adjustments and optimistic revision.
- [ ] Write/run PostgreSQL tests: two concurrent remaining commands permit one winner, same-operation replay returns same result, changed fingerprint conflicts; failure rolls back stage/cap/outbox together.
- [ ] Run focused and affected suites; commit/review this default-off additive slice.

## Task 2: Coherent quotation snapshots and minimum verified evidence

**Create in Accounting:** Application/Interfaces/IBillingSource.cs; Application/Interfaces/IBillingEvidenceReader.cs; Data/BillingSourceClient.cs; Data/BillingEvidenceClient.cs; Tests/BillingSourceContractTests.cs; Tests/BillingEvidenceHttpTests.cs.
**Quotation producer:** create Application/Models/QuotationBillingSnapshot.cs, Api/Controllers/QuotationBillingSnapshotController.cs and Tests/Controllers/QuotationBillingSnapshotHttpTests.cs; modify Application/Interfaces/IQuotationBoundaries.cs and Data/QuotationRepositories.cs by owner reservation only.
**Document producer:** exact paths are an owner-confirmed prerequisite described above; no Accounting registry implementation.
**Interfaces:** IBillingSource.ReadAsync(int quotationId, CancellationToken) -> Task<BillingSnapshot>; IBillingEvidenceReader.VerifyAsync(int customerId, int quotationId, IReadOnlyList<EvidenceReference> references, CancellationToken) -> Task<IReadOnlyList<EvidenceReceipt>>.

- [ ] Write failing actual-host tests: coherent parent-and-line snapshot, child edit changes revision/digest, acceptance identity retained; missing parent version/line/customer rejects before account creation.
- [ ] Confirm focused failures; implement one producer transaction/snapshot read and consumer identity checks. Do not reuse a parent-only timestamp as aggregate proof.
- [ ] Write failing evidence tests for wrong customer/order, mutable/replaced version, unverified record, denied access and unavailable producer.
- [ ] Implement the narrow consumer adapter against the frozen producer wire; capture immutable association receipts, no signed URL as identity.
- [ ] Verify real producer/consumer HTTP fixtures including unavailable-after-preview; commit owner-specific PRs and record exact accepted contract versions before Task 3.

## Task 3: Full/deposit/remaining commercial requests

**Create:** Application/Interfaces/IBillingWorkflow.cs; Application/Services/BillingWorkflow.cs; Api/Controllers/BillingController.cs; Data/BillingCompatibilityGuard.cs; Tests/BillingIssuanceHttpTests.cs; Tests/BillingEvidenceIssuanceTests.cs.
**Modify by reservation:** Api/Program.cs and Api/Authorization/AccountingPermissions.cs; existing invoice creation entrypoints only to route/guard onboarded accounts, preserving financial-owner completion.
**Interfaces:** IBillingWorkflow.OpenAsync(int quotationId, BillingContext, CancellationToken) -> Task<BillingResult>; PreviewAsync(Guid accountId, StageDraft, CancellationToken) -> Task<BillingAccountView>; IssueAsync(Guid accountId, StageDraft, IReadOnlyList<EvidenceReference>, BillingContext, CancellationToken) -> Task<BillingResult>; ReadAsync(Guid accountId, CancellationToken) -> Task<BillingAccountView?>.
**New API:** /billing/v1 accounts/read/preview/stages commands. Final exact route/OpenAPI table is reviewed with producer/consumer fixtures in this task; no legacy route replacement.

- [ ] Write failing full/deposit/remaining tests using authoritative server amounts, typed due dates and same-debtor head-office routing; reject different CustomerId/TaxId and invalid percentage.
- [ ] Confirm failures; implement stage command and one-time legacy commercial acceptance association. Later stages never rebind Quotation.InvoiceId or restart order creation.
- [ ] Write failing tests: missing shipment/release or customer-specific evidence blocks balance issuance; privileged actor also blocked; unavailable-after-preview blocks; draft requirement edits cannot remove an unmet agreed milestone.
- [ ] Implement server evidence checks and durable request/document intent; retain default full flow and immutable issued snapshots.
- [ ] Verify legacy and new concurrent creation cannot both consume the same quotation; commit/review with financial owner before shared entrypoint merge.

## Task 4: Verified settlement, credits and refunds

**Create:** Application/Interfaces/IBillingPaymentVerifier.cs; Application/Services/BillingSettlementService.cs; Data/BillingPaymentReservationStore.cs; Api/Controllers/BillingPaymentsController.cs; Tests/BillingSettlementHttpTests.cs; Tests/BillingAdjustmentPostgresTests.cs; Tests/BillingLegacyWriterGuardTests.cs.
**Modify by reservation:** PaymentRecordControllers.cs, InvoiceControllers.cs, ReceiptControllers.cs and their owning service mutation path; guard onboarded references before legacy updates/deletes. Add an independently reviewed Payment reservation migration.
**Interfaces:** IBillingPaymentVerifier.ReserveAsync(AllocationRequest, Guid operationId, CancellationToken) -> Task<PaymentReservation>; ReadAsync(int paymentId, Guid operationId, CancellationToken) -> Task<PaymentReservation?>; AcknowledgeAsync(PaymentReservation, Guid allocationId, CancellationToken) -> Task.
BillingSettlementService.AllocateAsync(Guid accountId, AllocationRequest, BillingContext, CancellationToken) -> Task<BillingResult>; AdjustAsync(Guid accountId, BillingAdjustment, BillingContext, CancellationToken) -> Task<BillingResult>.

- [ ] Write failing tests: verified cash100 allocated60/40, duplicate reservation, concurrent allocations, wrong currency, changed Payment revision, withholding claim versus accepted settlement/certificate.
- [ ] Confirm failures; implement Payment-database reservation receipts and Invoice-ledger acknowledgment phases. Unknown outcomes remain unsettled/NeedsReconciliation until exact evidence converges.
- [ ] Write failing tests: paid-stage credit releases excess allocations to unapplied credit; refund with unchanged debt reopens outstanding; commercial reduction plus refund is not subtracted twice; cap below commitments rejects without coordinated corrections.
- [ ] Implement append-only adjustments and unapplied cash handling; guard paid-checkbox/delete/Amount mutation on onboarded records. Preserve historical nononboarded behavior.
- [ ] Verify reconciled totals and failure injection across both databases; commit/review without new collection UI or payment providers.

## Task 5: Tax events, document issuance and recovery

**Create:** Application/Interfaces/IBillingTaxPolicyReader.cs; Application/Interfaces/IBillingDocumentWorkflow.cs; Application/Services/BillingTaxService.cs; Application/Services/BillingDocumentWorkflow.cs; Data/BillingDocumentPhaseStore.cs; Data/BillingDocumentClients.cs; Api/Controllers/BillingDocumentsController.cs; Tests/BillingTaxPolicyTests.cs; Tests/BillingDocumentRecoveryHttpTests.cs.
**DocumentService create:** Domain/Billing/BillingDocument.cs; Rendering/Documents/BillingDocumentComposer.cs; Tests/BillingDocumentRasterTests.cs.
**DocumentService modify:** Api/Controllers/PdfsController.cs for additive versioned rendering; preserve old routes/oracles.
**Interfaces:** IBillingTaxPolicyReader.ReadConfirmedAsync(string classification, DateTimeOffset effectiveAt, CancellationToken) -> Task<TaxPolicySnapshot?>; BillingTaxService.CaptureAsync(TaxTrigger, BillingContext, CancellationToken) -> Task<BillingResult>; IBillingDocumentWorkflow.IssueAsync(BillingDocumentRequest, BillingContext, CancellationToken) -> Task<IssuanceView>; ReconcileAsync(Guid operationId, CancellationToken) -> Task<IssuanceView>.

- [ ] Write failing tax tests with explicitly synthetic effective-dated policies: deposit then delivered remaining, unpaid delivery, service advance/early use/early issue, mixed/partial delivery and duplicate event; cumulative taxable obligation minus previously recognized portion yields exact delta.
- [ ] Confirm failures; implement factual trigger capture independent of commercial evidence and confirmed-policy validation. Absent/unconfirmed policy queues review and blocks automatic tax issuance without deleting trigger time.
- [ ] Write failing document tests for receipt from actual allocations, separate cash/withholding, customer edit and policy rollover, issued history immutability and missing release evidence not blocking an otherwise required tax document.
- [ ] Implement unique issuance number/operation, immutable snapshot, phase receipts and existing FileService adapters; use distinct document kinds and no full-quotation copying per stage.
- [ ] Verify lost response after number reservation/render/upload/receipt persist; reconcile exact operation/digest without blind issuance or notification. Run 150-DPI raster regressions and old PDF oracles; commit/review owner slices.

## Task 6: Usable staff billing timeline and reports

**Intranet create:** Contracts/BillingContracts.cs; Bff/Accounting/BillingEndpointMapper.cs; Bff/Accounting/BillingProxy.cs; Client.Features.Accounting/Pages/QuotationBilling.razor, QuotationBilling.razor.css, QuotationBilling.resx, QuotationBilling.th.resx; Tests/BillingBffContractTests.cs; BrowserTests/QuotationBillingBrowserTests.cs.
**Intranet modify:** Bff/Program.cs; Client.Features.Accounting/Pages/InvoiceCreate.razor and InvoiceView.razor plus English/Thai resources. Prefix each listed path with Legacy.Maliev.Intranet. Existing financial report pages consume a shared projection under #159; no separate calculation engine.
**Interfaces:** BillingContracts maps the approved producer DTOs to browser-safe views; BillingProxy.ReadAsync/PreviewAsync/IssueAsync/AllocateAsync delegate exact producer commands and trusted employee/operation/revision, never browser identity authority.

- [ ] Write failing BFF tests for live permissions, CSRF, wrong customer, stable operation on retry and unchanged legacy invoice view.
- [ ] Confirm failures; implement typed same-origin proxy and feature-gated navigation.
- [ ] Write browser tests: Full default; Amount/Percentage/Remaining; deposit200/remaining800; partial cash; same-debtor head-office snapshot; missing evidence disables issue with visible reason; verified evidence permits; tax review independent; conflict/uncertain replay and Bangkok due-date boundaries.
- [ ] Implement accessible bilingual timeline, verified evidence selection and authorized transient download preview using existing FileService capabilities. Do not add member access or NDA gates.
- [ ] Verify report and CSV projection reconcile gross billed, cash, accepted withholding, credits, outstanding/unbilled and separate VAT totals with no duplicate joins; commit/review UI and #159/#160 consumer contracts.

## Task 7: Compatibility, joined acceptance and controlled rollout

**Create:** Tests/BillingCompatibilityMigrationTests.cs; Tests/BillingJoinedAcceptanceTests.cs; docs/staged-billing-operations.md; scripts/verify-billing-reconciliation.py.
**Modify:** applicable existing invoice/payment/receipt regressions, CI workflow and owner migration documentation. Generate migrations in Tasks 1/4 only after owner model review; no persistent apply here.

- [ ] Write failing historical tests: old wire/PDF readability, null/unknown allocations, ambiguous quotation binding stays unreconciled, no imported paid-checkbox settlement or automatic historical tax issuance.
- [ ] Confirm failures; implement explicit reviewed import mapping and old-writer guards. Imported full invoices consume their reconciled cap once; no dual active billing models.
- [ ] Run joined HTTP/PostgreSQL/browser matrix for Tasks 1–6 and injected recovery failures; verify every row reconciles to currency precision and no consumer counts tax documents as another commercial invoice.
- [ ] Validate .NET10 Release zero warnings/errors, focused/full affected suites, existing unexcluded coverage gates, formatting, vulnerability/secret checks and document oracles. Require protected review/merge and exact-main CI for each owner; retain raw failure evidence.
- [ ] Record exact issue/PR/main/CI receipts under #79 and related scopes. Keep #79 open until usable joined acceptance and rollout criteria are satisfied.

### Activation sequence

1. Code/default-off additive migrations accepted through protected review; no deployment implied.
2. Separate environment-specific schema review and authorization; production-derived historical mapping dry run with ambiguous/unmatched coverage displayed.
3. Shadow read/reconciliation; confirm legal classification/policy approvals and producer availability. Test software uses synthetic policies; live values are never guessed.
4. Separately authorized limited staff activation only after old-writer guards and required evidence producer are accepted. No full NDA lifecycle prerequisite.
5. Verify deployed versions, actual operation receipts and report reconciliation before broadening access. Customer messages/real documents require normal explicit permissions.
6. Rollback disables new issuance, preserves committed ledger/documents and finishes acknowledged reconciliation; no destructive rollback. Monitor unreconciled phases, unmatched payments, cap checks, evidence failures and tax delays.

## Verification commands and commit gates

From each owner's repository use its solution and AGENTS-required commands:
- dotnet build Legacy.Maliev.AccountingService.slnx -c Release
- dotnet test Legacy.Maliev.AccountingService.slnx -c Release --filter FullyQualifiedName~Billing
- dotnet test Legacy.Maliev.AccountingService.slnx -c Release
- dotnet format Legacy.Maliev.AccountingService.slnx --verify-no-changes
- git diff --check

Use corresponding Quotation/Document/Intranet solutions for their slices and existing browser/raster runners. Each task starts by observing its intended failing assertions, ends with focused and affected green tests plus a coherent commit, then independent owner review. Do not lower coverage, weaken legacy assertions or silently classify unexecuted tests as passing.

## Self-review and decisions before execution

Checked specification coverage across Tasks 1–7, type/signature consistency, cap/correction formulas, independently testable task boundaries and all five Review Focus risks. Document evidence is a narrow verified-association dependency; whole NDA lifecycle and alternate storage/access systems are excluded. The approved no-override and same-debtor rules remain intact. No boundary amendment is needed; producer placement/paths remain an explicit owner reservation before implementation, not an implicit assignment.

Required before execution: user review of this plan and execution structure; financial-owner acknowledgment for shared files; document-owner confirmation of narrow producer placement/contract. Accountant approval is required before live legal issuance, while software construction and disposable synthetic tests can proceed after plan approval.

Recommendation: sequential implementation with one accountable Accounting owner, independently reviewed owner-specific slices, and a joined review after Tasks 5–7. The ledger, evidence, allocation and issuance interfaces depend tightly on each other; parallel shared-file implementation would add coordination cost and financial risk.

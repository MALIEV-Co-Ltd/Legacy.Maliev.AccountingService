# Receipt creation under the normal PostgreSQL retry strategy

Status: unpublished design and regression draft. Base is Accounting protected main `1427db3151a0124c0db2cc3eefb7f413b8b3aa1b`; its fresh-main validation is running and remains separate from this draft. PR #62 added only manual full-validation dispatch and one workflow contract case; all 99 selected production/build inputs remain those reviewed for PR #61.

## Problem and existing boundaries

ReceiptWorkflowStore.CreateReceiptAsync opens a manual transaction for a generated-ID Receipt and its lines. Normal Program uses the pinned Defaults PostgreSQL retry strategy. Existing plain-UseNpgsql migration tests do not exercise this combination. Creation itself has not been reproduced as failing in a fresh hosted run; source structure identifies the gap requiring normal configured-retry regression evidence.

Source snapshot `135e526d0dab85c415b3afdcefd7b70fe2c82e2f`, `Maliev.ReceiptService.Api/Controllers/ReceiptsController.cs` blob `4016f3ab4923ed5d664e5c364038ece41a309232`, creates a receipt using server UTC timestamps and the legacy financial, billing, customer and invoice fields. The migrated server-owned invoice workflow derives its receipt/lines from persisted Invoice state. Preserve that existing migrated mapping, PostgreSQL computed amounts, scalar references, response types and routes; this transaction fix is not a new literal source field-parity claim.

The workflow checks the Redis operation journal, obtains the invoice lock, queries/reconciles existing receipts, creates only when no receipt exists, links the invoice, then renders/stores a PDF and optionally sends email before recording completion. A sole existing receipt can be reconciled; multiple InvoiceNumber matches fail closed. Receipt storage has no operation-UUID admission row or unique InvoiceNumber constraint. Creation cannot be blindly replayed after an uncertain commit or with previously tracked generated-ID entities.

## Bounded implementation choice

Use the existing InvoiceCreationStore's one-attempt principle, scoped to receipt creation. Run the transaction inside the normal execution strategy, but capture failures inside its delegate and propagate after leaving the strategy so transient insertion/commit errors do not repeat the write. Pass caller cancellation to the outer strategy and transaction commands. Create a fresh owned ReceiptDbContext from the normal configured options so failed tracked insertion state cannot contaminate the caller's scoped context or a later reconciliation.

Keep one transaction for the Receipt plus all mapped lines. Use bounded cleanup independent of caller cancellation before commit submission. After commit submission, or when rollback/disposal outcome is uncertain, return ReceiptWorkflowUnavailableException; do not claim rollback or retry insertion. Reload computed values only after successful commit. Existing-row reconciliation continues to return its persisted receipt without inserting rows. Preserve workflow link/cache/provider/journal ordering.

Only rollback receives an independent five-second budget. Transaction and owned-context DisposeAsync are awaited without deadline bounds; disposal latency remains a residual risk. Disposal errors produce an unavailable outcome, but this design does not promise deadline-bounded disposal or arbitrary ambient-transaction/concurrency uniqueness guarantees.

No new constraint, schema, source database, provider write, dependency revision, retry-disable setting or production logging detail is authorized. The deletion retry and invoice-cache changes already accepted in PR #61 are retained without broadening their guarantees.

## Required regression evidence

Use the normal Program's owned PostgreSQL/Redis fixture and assert its actual configured public NpgsqlRetryingExecutionStrategy. Add normal configured-retry creation evidence for:

1. Fresh creation persists exactly one receipt and its lines, preserves invoice-derived fields/computed amounts and returns the generated ID.
2. Existing-row reconciliation returns the same ID with no duplicate rows or cache/journal/provider effects.
3. Failure after receipt insertion but before line completion rolls back receipt/line rows; caller context retains no failed tracked insert.
4. Pre-cancellation performs no writes; cancellation after insertion before commit cleans up without replay.
5. Injected loss of acknowledgment after an actual commit surfaces an unavailable outcome, with exactly one persisted receipt/line set and one insertion attempt; a later persisted-state reconciliation returns that set.

Direct store fault controls retain the normal configured options, actual database and actual cache. Two positive internal-workflow cases use the actual store, PostgreSQL lock and Redis journal with dedicated observable synthetic signature/render/storage/customer/optional-email adapters, then verify the linked invoice through the normal HTTP read endpoint. They exercise internal orchestration and real HTTP readback; they do not claim a new HTTP creation-write authorization witness. Existing controller and authorization-denial gates remain mandatory. The existing rejection fixture's egress denial is unchanged. Synthetic responses do not prove a live provider or deploy readiness.

Draft tests remain uncommitted until paired with a coherent reviewed fix; do not push intentionally broken tests-only RED. Review the complete runtime/test/workflow/verifier diff and exact selected-input graph before publication. Hosted build first, focused new cases, full Accounting suite, all existing financial/atomic/prospective/Defaults gates, raw generated-inclusive coverage and static checks remain required. Local SDK execution and deployment remain prohibited.

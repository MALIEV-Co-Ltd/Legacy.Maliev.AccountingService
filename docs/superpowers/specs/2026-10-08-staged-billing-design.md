# Quotation deposits and staged billing — architectural specification

Date: 2026-10-08
Status: written specification for user review; implementation is not authorized.
Umbrella: [Accounting #79](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.AccountingService/issues/79).

## 1. Approval and intended outcome

The user approved the recommended Accounting-owned approach and the same-legal-debtor default. That approval permits this written specification. It does not approve this artifact, interfaces, schemas, migrations, deployment or document issuance. After written-spec approval, create a separately reviewed implementation plan and obtain the execution-method selection before implementing.

A quotation normally produces one request for its full commercial amount. Staff can instead request a deposit by amount or percentage, then arbitrary installments or the remaining amount. Stages support head-office routing, payment due dates and shipment/release/customer-acceptance evidence. Each stage and quotation rollup must explain billed, cash received, recognized withholding, outstanding and remaining unbilled amounts without counting the quotation or VAT twice. THB1,000 is an illustrative total, not a policy or fixed split.

MVP covers manual verified payments, partial allocations, issued-document history, credits, refunds, cancellation, revisions and concurrency. It excludes new payment providers, general-ledger replacement, automated collection reminders, automatic financing, unrelated refactoring and new infrastructure.

## 2. Verified baseline and dependencies

Static inspection used Accounting9cc22da4e64f47ec79a03833af53dfe5c8ae7530, Quotation6700c65e6ec6eab7fe6932afed1a8d7bf943053c and Intranetb374ef520863404e9c53ef10b017a5bd77da45b8. R: was unavailable; original source mirror60c341c3993354f4423b812611e20d74692d9d09 was read instead. This is not runtime parity proof.

[InvoiceCreationWorkflowService](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.AccountingService/blob/9cc22da4e64f47ec79a03833af53dfe5c8ae7530/Legacy.Maliev.AccountingService.Application/Services/InvoiceCreationWorkflowService.cs) copies full quotation amounts. Quotation retains scalar InvoiceId. Invoice retains one ReceiptId, IsPaid, PaymentDate and Outstanding. Payment has amount/currency/date/reference, with no invoice allocation relation. ReceiptWorkflowStore copies invoice totals. Terms is free text, not a typed due date.

Reuse [Accounting#23](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.AccountingService/issues/23), [#48](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.AccountingService/issues/48), [#53](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.AccountingService/issues/53) and [#4](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.AccountingService/issues/4) for existing admission/completion/receipt ownership.
[Intranet#160](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/issues/160) retains collections/partial-payment scope; [#159](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/issues/159) retains reports; [#164](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/issues/164) retains QA; [#262](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/issues/262) retains company/address editing.
Document/NDA registry discovery is [Intranet#277](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet/issues/277); replacement/release work is [Order#61](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.OrderService/issues/61).

Financial migration task01a1166a-3d29-776b-90a4-b5e2a215a0a7 acknowledgment remains required before planning shared changes; earlier message/read attempts timed out. Related documents task confirmed registry boundaries and references. These issues are dependencies, not implementations to recreate or close.

## 3. Chosen architecture and ownership

Add billing functionality within Accounting, without a new service. Accounting is authoritative for the commercial billing cap, issued stages, settlement allocation ledger, tax events and issuance history. Keep existing Invoice, Payment and Receipt stores and retained wire identities. Other service identifiers are scalar references, never cross-domain foreign keys/DbContexts.

The new local billing ledger resides alongside Invoice persistence so the account cap, stage issuance, ledger revision and issuance intent can commit in one local transaction. Payment and Receipt databases remain separate. Their changes use durable intents, stable operation identities, verified acknowledgments and recovery; no cross-database atomicity is claimed. Redis can serialize admission but cannot be the sole durable correctness mechanism.

Quotation owns commercial acceptance and approved revision evidence. Accounting consumes a coherent scalar-and-line snapshot with a revision/digest; existing parent ModifiedDate alone is insufficient where child edits do not advance it. Before issuing from such a quotation, require a producer-owned coherent snapshot/version extension or reviewed frozen-snapshot attestation. Do not silently invent aggregate consistency.

Order owns shipment/release/acceptance facts. Customer owns legal identity/address defaults. The business document registry owns classification, associations, immutable versions and verification. FileService owns private bytes/scan state and metadata. DocumentService stays a stateless renderer; Accounting owns legal issuance identity and persisted snapshots. NotificationService handles separately authorized delivery intents.

Alternatives rejected: multiple legacy full invoices risk repeated acceptance and inflated totals; a separate Billing service adds unnecessary distributed ownership and deployment costs.

## 4. Conceptual records and associations

These are logical requirements, not approved DTOs/table definitions.

- BillingAccount: canonical QuotationId, frozen approved revision/digest, CustomerId/legal company/tax ID, currency, commercial base/VAT/gross cap, policy version and ledger revision.
- BillingStage: account, stable stage identity/sequence, kind Full/Deposit/Installment/Remaining, entered amount or percentage/basis, component allocation, purpose, due date, debtor/recipient snapshot, evidence references and lifecycle.
- BillingDocument: stage associations, document kind, unique number, issuance operation, immutable content/PDF digest and version, issued time, supersession/credit references and storage metadata.
- PaymentEvidence: owning PaymentId, verified amount/currency/date/reference/revision and evidence identity. Verification does not equate a generic Payment row with customer settlement.
- SettlementAllocation: payment evidence to account/stage/document, cash and recognized withholding components, allocation operation, certificate references, actor/time and reversal links.
- TaxEvent: affected commercial portions, tax classification/policy version, taxable base/VAT delta, trigger event/time, recognition and tax-invoice issuance state; allows one event across multiple stages.
- Adjustment: approved cap amendment, commercial credit, payment-allocation reversal or refund, each with its own semantic kind and linked immutable originals.
- EvidenceReference: conceptual DocumentId, immutable VersionId, canonical CustomerId and related quotation/order/invoice IDs plus verification provenance.

A commercial request may map to an installment-sized legacy Invoice projection; its total equals that request's gross, never the full quotation. Several requests share one account. A tax invoice can cover amounts across requests without creating another commercial charge. Multiple payments/receipts may relate to a stage; one payment may allocate across stages. A receipt references actual verified settlement allocations, not IsPaid. Cash and withholding are displayed separately.

Head-office routing retains the quotation's canonical legal debtor and tax ID. Staff may change recipient/branch/address in an issuance snapshot, with validation against the same legal entity. A different debtor is rejected and requires a separately approved commercial requotation/authorization flow. Never derive debtor change from free text.

## 5. Amount invariants and correction semantics

All comparisons use the same currency and VAT-inclusive commercial gross basis. Store decimal values with currency precision and an explicit rounding policy. Percentage applies to approved commercial gross before withholding. Allocate base/VAT components deterministically; the last eligible stage receives the residual so component totals reconcile exactly. Different tax categories require per-line allocation.

Draft stages express intent and do not count as billed; any reservations are separately visible. Issuance serializes on account revision and checks the authoritative cap in the local database. Stable operation ID plus request fingerprint yields same-result replay or conflict for changed intent.

Net billed = issued commercial requests minus commercial credits.
Net settled = retained cash allocations plus accepted withholding settlements minus settlement reversals.
Outstanding = net billed minus net settled.
Unbilled = approved current commercial cap minus net billed.

Reject issuance beyond available cap and allocation beyond both stage debt and payment availability. Reject currency mismatches. Real excess cash is recorded as unapplied customer credit and flagged for authorized handling; do not hide it or reject the factual existence of money received.

A credit carries an explicit disposition: correction/rebill restores eligible cap, while cancellation/reduction reduces the commercial cap. A refund is a cash event, not automatically a commercial credit. Reverse settlement when appropriate; if debt remains, outstanding reopens. Never subtract the same refund as both a revenue credit and payment reduction without the linked commercial facts.

If credit or cap reduction affects already-settled debt, atomically release the affected allocations to unapplied credit with reversal links; stage outstanding does not become negative. Refund or reallocation is a separate authorized action. Amendment below issued/settled commitments is rejected unless coordinated credits and allocation reversals accompany it.

Do not mutate issued snapshots after quotation/customer edits. Revision increases/decreases create approved account amendments with prior/new revision evidence; issued stages remain immutable. Drafts may be revised using optimistic concurrency. Issued corrections use linked credit/reversal/replacement documents and immutable audit facts.

## 6. Tax-point and document policy

Commercial billing, VAT recognition, tax-invoice issuance and receipts are separate lifecycles. Delaying release paperwork or a billing request cannot defer an already-triggered VAT obligation.

Official research supplied for this design:
[Revenue Code tax points](https://www.rd.go.th/5205.html), [tax invoices](https://www.rd.go.th/5208.html), [goods ruling](https://www.rd.go.th/27935.html), contrasting manufacturing classifications [A](https://www.rd.go.th/32686.html)/[B](https://www.rd.go.th/25363.html), [previously taxed advances](https://www.rd.go.th/3606.html), [receipts](https://www.rd.go.th/5203.html), [withholding](https://www.rd.go.th/5937.html).

Ordinary goods generally trigger VAT on delivery or earlier payment/ownership transfer/tax-invoice issue; an unpaid delivered balance can require a tax invoice before cash. Services generally trigger on payment/advances unless earlier issue/use applies. MALIEV transaction classification has not been determined.

Require accountant-approved, effective-dated policy configuration before tax issuance: transaction category, trigger precedence, rates, taxable bases, advance treatment, rounding, withholding recognition/certificate requirements, document numbering and correction rules. Capture policy ID/version and approval evidence on each event. No universal hard-coded goods/service classification, VAT/withholding rate or service-name inference.

The cumulative taxable obligation and already recognized advances determine each tax delta; recognize only the remaining applicable portion. Tax invoice documents never increase net commercial billing. Trigger ingestion is deduplicated by authoritative event identity; early tax-invoice issuance must itself be evaluated as a potential trigger. Mixed goods/services and partial deliveries are allocated by verified line portions.

Missing/unconfirmed policy blocks automatic tax issuance, exposes a compliance-review queue and preserves the factual trigger timestamp. Do not invent a policy or postpone the recorded tax point. Corrections append linked tax-credit events according to the confirmed policy. Withholding is settlement evidence, not an invoice discount or VAT reduction. Assessment, customer deduction claim, acceptance and certificate verification remain distinct.

## 7. Staff and customer flows

Quotation billing page initially proposes Full. Staff can select Amount, Percentage or Remaining and preview amount components, unbilled cap, recipient/head-office branch and due date. Server recomputes all authoritative values. A final issuance preview distinguishes commercial request from tax invoice and receipt.

For post-shipment/release/acceptance stages, require verified immutable evidence references selected for the configured milestone. Proposed MVP allows a separately permissioned, reasoned staff override where commercial policy permits; tax-trigger capture cannot be overridden. This explicit proposed override policy requires written-spec review.

Payment entry verifies cash evidence, then allocates to stages with an unapplied balance shown. Record claimed withholding separately and accept it only under configured finance policy; keep certificate status visible. Issue receipt against verified allocations, with gross settlement, cash and withholding clearly labeled. Do not use a paid checkbox to manufacture payments.

Timeline shows issued requests, due dates, cash, withholding, credits, tax events, receipts, outstanding and unbilled; unavailable historical allocations are labeled unknown. Show overdue commercial requests by typed due date using Asia/Bangkok dates, separate from tax-invoice due alerts. Due-date changes append audited amendments without altering historical PDFs.

Registry-backed customer documents expose only explicitly authorized document versions. Staff notes, financial audit and reconciliation history are not automatically member-visible. Document access checks CustomerId and association permissions server-side, then obtains short-lived access through FileService. Never persist NAS paths or signed URLs as identity. NDA status does not independently authorize invoice access.

English/Thai, keyboard access, accessible previews and loading/empty/conflict/uncertain/error states are required. Sending notifications is a separate explicit authorized operation with recipient preview and history.

## 8. Consistency, authorization and recovery

Authenticated commands require live finance permissions, trusted employee identity, CSRF at cookie BFF boundaries and record-level customer/account access. Append actor/time/reason, command fingerprint, source/policy versions and before/after ledger revision; logs use redacted identifiers, not documents/PII/tokens.

Local cap and allocation changes commit with a durable operation result and outbox intent. Document rendering/storage is acknowledged by operation/document identity and content digest. Lost acknowledgments enter NeedsReconciliation; inspect authoritative state and repeat only idempotent acknowledged protocols, never blindly reissue a legal number or resend email.

Payment verification reserves/acknowledges allocation ownership through a reviewed payment-owner protocol before becoming settled in the billing ledger. If payment evidence changes, fence affected allocations for reconciliation rather than overwriting history. Receipt persistence and file linkage use separate acknowledged phases. Uncertain work does not appear completed.

Quotation acceptance/order creation is one existing commercial operation. Later installment issuance does not rerun it or rebind Quotation.InvoiceId. The legacy pointer remains its reviewed compatibility reference; the new account-to-stage association supplies multiplicity.

## 9. Backward compatibility and migration

Use additive, default-off structures and versioned commands/projections. Preserve legacy routes, PascalCase wires, retained CLR/message identities and old PDF oracles. Add new rendering kinds without silently changing legacy invoice/receipt meaning. CompatibilityContracts changes occur only for a confirmed retained consumer with identity tests.

Historical invoices remain readable with their original values. Do not infer paid allocations from IsPaid/PaymentDate or generate historical tax events from copied amounts. Mark unsupported associations as legacy/unreconciled. A separately reviewed import can associate a historical full invoice to one legacy account stage only with explicit quotation linkage, coherent commercial snapshot and finance reconciliation. Ambiguous cases stay queued; never autoissue documents during backfill.

Existing mutable invoice/payment paths must not bypass new ledger invariants for onboarded accounts. Before activation, add reviewed guards or translation for those writes; untouched historical accounts retain their supported legacy flow. No account runs two active creation models. Source/schema parity and existing migration owners must approve additive rollout; do not reinterpret exact-23 legacy data or include schema changes in a row-refresh authorization.

## 10. Acceptance and phased rollout

Meaningful disposable HTTP/PostgreSQL and browser tests must prove:
1. Default one full request; arbitrary amount/percentage/remaining stages; component residuals and multiple tax categories.
2. Concurrent remaining issuance has one winner; replay/changed fingerprint; lost commit/render/upload/receipt acknowledgments reconcile without duplication.
3. Multiple partial payments, cross-stage allocations, currency mismatch, over-allocation and excess unapplied cash.
4. Withholding claim/acceptance/certificate gaps; cash-versus-gross receipt amounts; no paid-checkbox inference.
5. Deposit VAT followed by delivery balances, unpaid delivered goods, confirmed service advances, early issue/use and mixed/partial delivery; no double VAT.
6. Draft cancellation, issued corrections, paid-stage credit, refund with/without commercial reduction and cap amendments; immutable history.
7. Same-debtor head-office snapshots, rejection of another debtor, later customer edits and registry version changes.
8. Permission/CSRF/record-level controls, evidence gate/override, missing policy and uncertain-state UI, historical readability and customer access separation.
9. Reports reconcile billed, cash, withholding, outstanding, unbilled and independent VAT totals without joined-row inflation.

After spec approval: obtain owner acknowledgments, prepare implementation plan and execution selection. Implement in owner-specific reviewed slices: coherent quotation snapshot/policy prerequisites; Accounting ledger/commands; payment verification/allocation protocol; document/tax/receipt phases; Intranet UI and report/registry consumers; additive migration and compatibility guards.

Validate Release zero warnings/errors, focused/full affected suites, real cross-owner contracts, existing coverage gates, formatting, security and document raster oracles. Record exact PR/main/CI evidence under #79 and existing owner issues. Do not close an umbrella for a plan.

Activation requires separate environment-specific migration and deployment authorization. Start disabled with disposable proof, then reviewed historical reconciliation and read-only shadow comparisons, then limited staff enablement with finance approval. Disable new issuance on rollback; preserve committed ledger/documents and finish reconciliation. Never delete issued history or revert schema/data blindly. Monitor unmatched payments, cap violations, unreconciled phases, tax-trigger/issuance delays and report differences.

## 11. Review status and self-review

Reviewed for incomplete sections, contradictory ownership, monetary/correction formulas, tax-versus-commercial double counting, immutable references, scope and approval gates. No runtime proof is claimed. Same-legal-debtor routing is approved; evidence override behavior and all newly described contracts remain proposals for this written-spec review. Accountant-confirmed classification/policy and financial-owner coordination are implementation prerequisites.

Next action: user reviews this written spec. Only explicit written-spec approval permits the implementation-planning stage; it does not authorize executing an unreviewed plan.

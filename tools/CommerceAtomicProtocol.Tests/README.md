# Prospective Accounting atomic protocol diagnostics

This new assembly references the historical public fixture types without editing
their four source files or executing their historical tests. Validate this project
as the test target. Set MalievWorkspaceRoot to the absolute .joined-public directory
and preserve the local-dependency mode used for restore/build/test. Producer pins
and workflow admission belong to the separately owned public-graph.json.
The current workflow runs for affected Accounting source/test/workflow changes
and every push to main. Before executing the pinned graph, it requires all four
current Accounting production project trees and deterministic tracked root build
inputs (including file additions/removals) to equal the reviewed producer. Ordinary
full/focused validation must use the graph's exact Defaults and Contracts pins.
Mutation controls exercise matching input, root drift/addition/removal, dependency
pin drift and duplicate/missing checkouts.
Future production changes therefore fail closed until a separately reviewed graph
explicitly advances its producer; triggering the old pin cannot silently validate
new production. Historical six-case reproduction remains manual and strict.

All Accounting, Quotation and Auth Programs, JWT/IAM registrations, repositories,
issuer/session behavior and admission state machines remain real. Additional
handlers only wrap outbound transports. Quotation requests reach its TestServer;
Order responses are controlled HTTP, and PDF/file-existence responses are explicit
synthetic prerequisites for successful Accounting HTTP creation. They establish
neither live Order/provider acceptance nor deployed IAM grants. Unknown external
routes retain the historical refusal path.

The serialized collection owns one actual Accounting/Quotation host pair, preserving
normal DI token caches and the unchanged Auth service-login limiter. Each case resets
transport controls and observations and seeds distinct persisted records. Per-case
cleanup also clears hooks and failure controls after awaited operations, retaining
status receipts for xUnit diagnostics until the next case reset. Strict
financial comparisons use a database-read baseline. Safe per-case Auth path/status
receipts support diagnosis without changing authentication responses.

Exactly 17 Fact executions are declared:

1. PersistedInvoice_ActualDiCompletionUsesExactAtomicCustomerWire
2. OrdinaryAndRealDelegatedHttpCreationPersistInvoiceAndCustomerOutcome
3. SameInvoiceReplayPreservesOutcomeTimestampAndOrderKeys
4. DifferentInvoiceCannotRebindAcceptedQuotation
5. PriorDeclineRejectsCustomerInvoiceDecision
6. AcceptedUnlinkedQuotationRejectsLateCustomerInvoiceAttachment
7. ParentScalarEditAfterSourceConflictsWithOriginalVersionAndFencesDelegatedReplay
8. MissingSourceVersionReturns409BeforeInvoicePersistence
9. VersionlessCompatibilityCallFailsBeforeProducerHttp
10. LostProducerAcknowledgmentLeavesAcceptedInvoiceAndNeedsReconciliationFence
11. ConcurrentSameAndDifferentInvoiceIntentsRespectPersistedBinding
12. MalformedCompletionResponseNeverIssuesDecision
13. MissingProducerQuotationNeverIssuesDecision
14. LiveIamDenialCannotUseTokenPermissionOrWrite
15. LiveIamUnavailableCannotUseTokenPermissionOrWrite
16. LinkedOrderPartialFailureKeepsProducerPersistenceAndDelegatedFenceWithoutLaterEffects
17. ChildItemEditDoesNotAdvanceParentVersion_CharacterizesAggregateProtectionGap

Case 2 exercises ordinary and freshly Auth-delegated HTTP requests separately.
Case 11 synchronizes both decision dispatches after their lookups and exercises
same-invoice and competing-invoice concurrency. The malformed
case corrupts a real GET response in transport and does not attribute that payload
to the producer. Case 17 changes the child through the actual Quotation repository
resolved from DI: historical workload grants exclude LinesWrite, so this case
does not claim child-write HTTP authorization proof. It records the parent-version
gap rather than weakening the producer or claiming aggregate snapshot consistency.

Only quotation decision persistence is atomic. Linked-order failures or lost
acknowledgments can leave accepted quotation/invoice state committed, with delegated
Accounting admission fenced as NeedsReconciliation.
Cases 10 and 16 also exercise explicit same-intent completion reconciliation after
the controlled failure, preserving the producer scalar snapshot, single outcome
and linked Order keys. Case 16 retains actual producer standard resilience retries:
every physical attempt for each of exactly two linked Orders must keep its original
key, followed by exactly two successful requests during explicit reconciliation.
They leave the HTTP admission in NeedsReconciliation;
this is not an automatic admission retry or a global repair transaction.
Idempotency-Key is observed on the wire; Quotation does not deduplicate this header. No global transaction, saga
atomicity, provider success or runtime pass is claimed by these source drafts.

The security dependency successor is commerce-atomic-security-cache-public-20261005.
Only Defaults advances to 7edcd961024868513fd5f373cab3dcb261197f77 in the current
full, focused and 17-case graph workflows. Auth74, Accountingc1, Quotation36 and
Contracts78 remain pinned. The predecessor manifest from accepted Accounting f12
is archived byte-for-byte as public-graph-accepted-f12.json; this successor makes
no baseline-equivalence or fresh execution claim.

The hosted workflow additionally builds the pinned Defaults test project with
warnings treated as errors, then executes four cross-host cached-result theory
combinations and five independent cache/coalescing/cancellation/live-refresh
Facts (nine executions). The exact-name/outcome/counter verifier requires all nine
and a zero-warning/error build log. Results and an always-preserved artifact use
defaults-cache-results, separate from atomic-results and its one-TRX 17-case gate.
This compatibility proof does not replace Accounting full-suite/raw coverage or
the 17-case producer graph; no coverage threshold, exclusion or settings change
is introduced. All successor hosted execution remains pending.


The invoice consumer source successor is
commerce-atomic-invoice-intent-consumer-public-20261005. Accounting advances to
94376b1367173ce60eb083ebafa09d66b500a987 after actual hosted source validation:
604 full-suite, 312 invoice-focused and 21 atomic-focused executions passed with
zero skipped cases and zero build warnings/errors. Auth74, Quotation36,
Defaults7ed and Contracts78 remain pinned. The accepted da972 manifest is archived
byte-for-byte as public-graph-accepted-da972.json. Earlier source receipts above
remain historical evidence for their respective graph versions.

The original 17 scenarios/assertions, seven producer-input mutation controls and
nine Defaults cache compatibility cases remain unchanged. This version requires
fresh hosted graph execution; source validation does not prove the Notification
producer join, Intranet counterpart, production IAM, provider acceptance or
deployment. Delivery intents remain disabled by default.


The current Accounting producer pin is the compiled UTF8 origin-bound correction
68c54823853861587db866aa194adf6844af1efb. Its actual invoice-focused316 and
atomic-focused21 cases passed; the full run37303169718 executed608 with607 passed
and one failed matching workflow-pin assertion. That failure is retained rather
than called source acceptance. This successor updates the explicit workflow
contract with the producer pin and requires fresh full-suite and graph execution.
The 17 graph assertions and seven mutation controls remain unchanged.

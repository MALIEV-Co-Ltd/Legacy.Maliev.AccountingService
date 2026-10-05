# Prospective Accounting atomic protocol diagnostics

This new assembly references the historical public fixture types without editing
their four source files or executing their historical tests. Validate this project
as the test target. Set MalievWorkspaceRoot to the absolute .joined-public directory
and preserve the local-dependency mode used for restore/build/test. Producer pins
and workflow admission belong to the separately owned public-graph.json.

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

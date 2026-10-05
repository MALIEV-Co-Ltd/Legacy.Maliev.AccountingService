# Public joined authorization diagnostic

This is a fresh public-only graph, distinct from the sealed 906-input graph.
Auth51afbbd and Accountingff8fcf6 are retained; public Quotation36c4bab
includes production differences and is not original baseline equivalence.
Exact references and adapted file hashes are in public-graph.json.
Project references, three content roots and five equivalent Assert.Single
predicate overloads change in this harness to satisfy xUnit2031.
No issuer, claims, assertions, permissions, or transport behavior is changed.

The six cases use actual Auth login/session/delegation and real Accounting and
Quotation hosts with controlled ordinary IAM transports. They are synthetic
HTTP/PostgreSQL/Redis diagnostics, not deployed IAM, Web consent or provider proof.
Generated secrets remain in fixture memory. Source assertions remain intact.
Expected reconciliation controls and the desired positive-completion assertion
must be interpreted from actual responses and stored fields, never converted
into an expected-failure success. A failing desired contract remains a failing job.

Standard hosted validation retains TRX and generated-inclusive raw coverage even
on failure. Service PR validation and its unchanged per-assembly80% gates remain
separate. No acceptance, merge, deployment, or source-history closure is inferred.

The hosted graph uses one worker, disables parallel project-reference builds,
and inherits one canonical workspace property rather than creating duplicate
project instances with per-reference property overrides. This addresses the
actual GenerateDepsFile output collision, not a retry or assertion fallback.

The hosted workflow must supply the explicit absolute MalievWorkspaceRoot for
.joined-public. Any separately authorized future local invocation also requires
that value explicitly; the parent repository default targets ordinary service
dependencies and is not the joined graph. No local SDK invocation is authorized
by this document.

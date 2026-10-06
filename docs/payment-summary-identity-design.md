# Payment summary authoritative catalog IDs

Unpublished draft based on Accounting main `1427db3151a0124c0db2cc3eefb7f413b8b3aa1b`.
That main's child-list and Receipt-query hosted gates remain unexecuted because runner acquisition failed twice.
No local .NET, SDK, testhost or Docker execution is authorized. Native validation remains pending.

Source: `135e526d0dab85c415b3afdcefd7b70fe2c82e2f`,
`Maliev.PaymentService.Api/Controllers/SummariesController.cs`,
blob `f3d63cdaeecfa0bbd1607f9f747afe8b519c1388`.
Monthly, weekly and yearly summaries resolve Expense then Income through SingleOrDefault,
after returning 404 for an empty current window. Monthly Job income resolves Income then Job
after the same check. A populated summary requires these IDs; duplicate names refuse calculation.
Yearly income/expense details resolve the selected direction before querying payments;
an absent direction has no matching payments and remains 404, while duplicate directions fail.

Only AccountingRepository changes in production: resolve catalog IDs uniquely and filter by IDs.
No uniqueness migration, global collation, controller, authentication, error-payload, cache,
provider, Receipt creation or notification behavior changes. The separate unused generic
GetSummaryAsync projection is outside these six source routes and remains unchanged.

Seventeen new normal Program/JWT/live-permission/PostgreSQL HTTP cases cover duplicate same-case
Income/Expense/Job IDs with real current and previous-window payments, source lookup ordering,
an opaque non-success response, unchanged complete Directions/Types/Payments scalar snapshots,
and unchanged configured cache operation history plus a sentinel value. This summary fixture uses
the normal in-memory cache with RedisEnabled=false; these cases do not prove live Redis behavior.
The cache observer delegates to that provider; it neither supplies summary results nor bypasses storage.
One existing source-window seed adds the required unique Expense catalog row without changing its assertions.
Existing positive PascalCase, delta, currency, zero-Job and period-boundary cases remain in the focused gate.
The focused verifier resolves each result.testId against an exact owned TestMethod class/name,
rejects missing or contradictory definitions and duplicate/empty execution IDs, permits consistent
reused theory IDs, and rejects unexpected rows/counts. Sixteen offline synthetic verifier controls
exercise these evidence checks; they are not native financial acceptance. Hosted job timeout is 30 minutes.

Required candidate evidence: unfiltered full711 (694+17), focused summary47, every existing financial
and prospective gate, build zero warnings/errors, formatting, dependency and credential checks,
two identical raw full coverage copies with no exclusions and all four owned assemblies >=80 percent.
The 99-input producer graph must be advanced to an independently reviewed exact runtime witness;
98 inputs must remain identical to base1427. No candidate publication before full diff review.
ReceiptCreate and Payment producer drafts remain separate and held; no whole mixed-source closure is claimed.

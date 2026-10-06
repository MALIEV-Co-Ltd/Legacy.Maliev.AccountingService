# Invoice literal query and selected-page draft

Private source draft integrated onto accepted Accounting63 main `41e610352998352cdcba087b11018d635b2155c9` (fresh full711 and all eight focused gates verified). No publication, new producer graph adoption, or native acceptance of this draft is claimed.

## Source and mapping

Source mirror `maliev-web`, commit `135e526d0dab85c415b3afdcefd7b70fe2c82e2f`, `Maliev.InvoiceService.Api/Controllers/InvoicesController.cs`, blob `d2488ae255fe5b41b1e94b7d10c12d54c32476f9`. Initial extraction commit `5fac706a7983a6d359b39acbd670e6800afe020e` has the same relevant query behavior (controller blob `95c18c16d430c32b6c413aa102aacaac40bfffff`).

`GetPaginatedAsync` applies any nonempty literal search without trimming, selected empty Items return404, and SQL Server nullable dates sort NULL-first ASC / NULL-last DESC. These map to `AccountingRepository.GetInvoicesAsync`, the existing PascalCase page response, and the unchanged authenticated controller.

The draft preserves significant whitespace and escaped literal `%`, `_`, backslash, Thai text; explicitly orders ascending nullable dates before paging; returns null for an empty selected page so the controller emits404. Existing numeric receipt/ID matches, substring ID search, customer and paid filters remain. Stable ID ties are a deliberate deterministic paging policy, not a claim about SQL Server tie ordering.

## Consumer and deliberate exclusions

Intranet main `cda7e5f6817e9462f058eaf6638da58c27e75024`: `InvoicesProxy` forwards raw search via URI escaping; `InvoicesEndpointMapper.MapPageAsync` explicitly translates downstream404 into an empty200 page. No consumer edits were made.

Bounded default20/cap250 remains, rather than restoring the source's unbounded omitted size. The source's unused `isNumeric` could match ReceiptId0 for nonnumeric input; the guarded target safety policy remains. No schema, collation, cache, authority, DTO, provider, or global source-closure changes.

Invoice number lookup is a separate unresolved gap: the current controller inspects only two substring matches before selecting exact Number. Source uses direct unique Number lookup. This draft does not resolve or claim closure of that route; exact comparison/collation policy needs its own reviewed boundary change.

## Validation

21 authored normal Program/JWT/live-permission/owned PostgreSQL HTTP executions cover literal and numeric fields10, nullable date orders4, empty pages2, customer/paid2, auth denial2, Thai/nonnumeric receipt-zero exclusion1. Every read compares before/after full Payment/Invoice/Receipt stored-row snapshots. Explicit postinsert stored NULL assertions prevent default substitution from masking ordering.

Read-only focused workflow `invoice-master-query-source.yml` uses the existing immutable Defaults, Contracts and shared validator pins. `verify-invoice-query.py` requires all21 individual passed results, exact class/method identities and counts, unique executions/names, consistent definitions, all16 standard counters present (21 total/executed/passed; other13 zero), and executable raw inventories for all four assemblies. Exact fixture encoding passed;18 offline synthetic verifier controls passed, including every missing standard counter and every nonzero other-state counter, and are not native evidence.

Native build, tests, formatting, dependencies, coverage and immutable producer graph reconciliation remain pending. No local .NET/SDK/testhost/Docker or heavy runtime was run. The actual accepted predecessor has711 cases; the21 authored additions make732 expected for this slice alone, not native proof. Combining a separately reviewed number-lookup slice requires fresh inventory and graph reconciliation.

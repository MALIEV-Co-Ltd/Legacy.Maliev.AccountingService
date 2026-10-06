# Direct unique Invoice.Number draft

Reviewed source slice based on accepted Accounting63 main `41e610352998352cdcba087b11018d635b2155c9`; native proof for this new slice is pending. No whole-source closure is claimed.

Source135e `Maliev.InvoiceService.Api/Controllers/InvoicesController.cs` blob `d2488ae255fe5b41b1e94b7d10c12d54c32476f9`, GetInvoiceAsync: numeric values FindAsync(id), other values direct SingleOrDefaultAsync(Number == invoice). No substring paging, no explicit source collation or Number unique index. The current target's first-two substring page can hide later exact matches and duplicates.

The private service interface, repository and controller now use direct AsNoTracking SingleOrDefaultAsync with escaped literal ILike and no surrounding wildcards or trimming. Numeric ID precedence and its existing cache behavior remain. Nonnumeric number reads do not contact the generic ID cache, write rows or invoke providers. No route, DTO, permission, schema, migration or global collation change.

The coordinator explicitly reviewed the finite PostgreSQL case-insensitive adaptation. It is not global SQL Server collation or .NET OrdinalIgnoreCase equivalence. Duplicate exact or finite case-equivalent numbers refuse through the existing opaque400 exception boundary; never select an arbitrary matching invoice.

18 authored normal HTTP/JWT/live-permission/owned PostgreSQL cases cover hidden exact1, substring/purchase-order/missing3, duplicates3, finite case2, literal/Thai/padding5, numeric precedence2, auth denial2. Stored Payment/Invoice/Receipt rows are compared before/after. Number reads additionally compare the configured in-memory cache observer's actual operations and sentinel; no live Redis claim. Numeric ID reads deliberately retain cache priming and compare database rows only.

The approved Invoice query21 fixture remains byte-identical in the combined candidate. One combined hosted cohort will verify the 39 authored query/number cases and the full suite, expected750 from accepted711+21+18. These counts are inventory expectations, not native results. The immutable producer graph and current workflow expected pin must be reconciled together before publication.

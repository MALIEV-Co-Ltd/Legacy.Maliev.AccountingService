# Payment date and currency boundary

This source-backed runtime slice addresses the remaining owned portions of Accounting issues 49 and 50. It does not close those issues or prove production data, live Auth/IAM, or an Intranet runtime join.

## Date provenance and representation

Immutable source snapshot `135e526d0dab85c415b3afdcefd7b70fe2c82e2f` Payment controllers copy `PaymentDate` to SQL Server `datetime`. `Startup.cs` configures Newtonsoft MVC 8.0.28 and changes only null omission and reference-loop handling. The committed Docker/deployment files use the ASP.NET 8 Linux image and contain no timezone override.

Newtonsoft's [default date handling](https://github.com/JamesNK/Newtonsoft.Json/blob/13.0.3/Src/Newtonsoft.Json/JsonSerializerSettings.cs) is RoundtripKind. Its [ISO date parser](https://github.com/JamesNK/Newtonsoft.Json/blob/13.0.3/Src/Newtonsoft.Json/Utilities/DateTimeUtils.cs) converts explicit offsets to a local DateTime representing the same instant. Under the committed default UTC deployment, the copied SQL clock therefore represents that UTC instant. Historical live server timezone and SQL Server execution remain unverified; this is a committed-configuration inference, not a production parity receipt.

Accounting's existing `timestamptz` mapping accepts UTC DateTime values. The new Payment-only repository branch converts Local values to UTC, preserving their instant. Unspecified values retain the already reviewed tick-preserving UTC storage representation; UTC and nullable values keep their behavior. It does not interpret an offset-free Thailand clock as a physical instant, alter Invoice/Receipt date handling, change schemas, or set a global timezone.

Intranet's committed Finance contracts use nullable DateTime, date inputs produce clock values, and `FinancesProxy` forwards them through JsonContent. That consumer was read, not edited or executed as a joined application in this proof.

## Currency provenance and deliberate adaptation

Source `SummariesController.cs` blob `f3d63cdaeecfa0bbd1607f9f747afe8b519c1388` uses final-day midnight inclusive windows and omits monthly Job currency groups whose previous Job income is zero. Existing tests already cover those boundaries. New cases characterize missing, zero, and null previous currency groups across weekly/monthly/yearly income and expense, monthly Job filtering, selected year/currency daily totals, and populated-storage empty 404 results.

The source can throw when previous currency groups are missing or null. Accounting deliberately retains its existing safe zero sums and null key `-`; this is an explicit resilience adaptation, not preservation of accidental source exceptions. Exact daily UTC dictionary keys and decimal values are asserted independently. Payment/catalog/cache hashes remain unchanged during summary reads.

## Validation boundary

Nineteen new date cases plus 27 currency cases add 46 meaningful tests, forecasting a full suite of 1,178. The focused lane executes 56 cases including the existing four offset-free producer and six source-window controls in each isolated test-host timezone, UTC and Asia/Bangkok. Each job has a 30-minute timeout and retains original native TRX, raw coverage, exact source bindings, and timezone provenance. No native acceptance is claimed by source review or offline parser controls.

The source-only runtime witness and explicit own graph advance must retain all peer pins. Builds, focused/full suites, all existing native gates, unexcluded coverage, static/security checks, protected exact-head merge, and fresh-main readback are required. Issue 22 still requires DataMigration's protected SCB reconciliation; issue 24's production generated-column proof belongs to its data owner. This bundle supplies neither persistent DDL/data writes nor deployment authority.

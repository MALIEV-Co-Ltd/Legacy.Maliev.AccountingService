# Payment listing acceptance, issue 39

Current acceptance: fresh Release build0warnings/0errors; unfiltered446/446PASS,
0failed/skipped/abnormal results. Raw owned API770/95280.882%, Application89.510%,
Data97.325%, Domain95.977%; generated entries retained. Latest static checks and
protected-main PR acceptance remain pending. Historical checkpoints below describe
their then-current state, not the current coverage result.

Historical implementation status: genuine RED observed, root reviewed and approved the bounded four-runtime-file
implementation. The initial candidate was freshly built and run:49 cases,41PASS and eight
genuine nullable-string assertion failures. Root reviewed that RED and approved the minimal
repository-only NULL-rank fix. Fresh mandatory private Release builds reported zero
warnings/errors, then all49 focused cases passed. Root independently verified the GREEN
TRX and executed the light static gates below. Subsequent reviewed HTTP and atomic concurrency
regressions pass33/33; the latest unfiltered suite passed400/400 after real XML docs repair.
Raw API coverage715/952 (75.105%) still fails80. Whole-solution formatting and five transitive vulnerability audits
passed after scoped NEW-test whitespace correction. No completion or coverage80 compliance
is claimed; the expanded content secret scan is recorded below.

## Frozen provenance and ownership

Accounting base: `9759a9e5e2f593ddcf62dc17745f55450a64dbbf`, canonical clean and equal
to observed remote main. Tracking inspected at `02f6cf774fed8a17f20bacade0e5772e3b5dc5a0`:
checkpoint `a1df0b7b771a79242c6fda7f078d893640efe1e3`.
Source objects read only from `B:\maliev-legacy\.source-mirrors\maliev-web.git`.

Source `5fac706a7983a6d359b39acbd670e6800afe020e` (Initial commit, no parent) owns
`Maliev.PaymentService.Api/Controllers/PaymentsController.cs`,
`Maliev.PaymentService.Common/Enumerations/PaymentSortType.cs` and the Payment model/context.
These controller/enum paths have no later modifying or reverting commits through the checkpoint.
The pending Accounting owner remains partial in any eventual evidence report: this bundle
cannot resolve all initial Invoice/Receipt/Payment extraction paths.

Later invoice/SCB/provider changes are separate, not claims earned by this suite:

| Source SHA | Behavior and separate owner |
| --- | --- |
| `72eb9f1949176392141951d35e6e06f7c30af4c2` | Receipt model documentation follow-up; no payment-list change |
| `487d06df2f4614c53bead4b19d53d2613a46fd64` | Accounting paid-invoice attribution, DataMigration/Web consumers; PR19 evidence already present |
| `c821605b7ecde5f79d01966888b116defb10650d` | Intranet/Quotation invoice-link changes; Accounting issue48 active consumer work excluded |
| `283832f6c99fdf4445113be6f9ebb985b958c458` | Intranet nullable invoice view |
| `89ddd5a49ae7e781ca29225b8eae50fb9c2b1d0e` | DocumentService SCB footer |
| `7387d9e88e4f1b1b254c48f5ab0933ec3bbf20d3` | DataMigration SCB row script/Web localization; Accounting22 reconciliation remains open |
| `eaffed2a95a9a322855872e6a8478a6e64f9300a` | Web/Document PayPal removal supersedes old provider behavior |
| `f79657bdd06b878dc99fe45fbf26c40eeffb25f0` | Retired-provider inventory removal; never restore provider execution |

Existing registered API baseline is 15 controllers, 72 actions, 73 route templates according
to unchanged AccountingContractTests. There are 27 existing tracked test-project files.
No fresh executable baseline case count is available under the execution hold; do not
reinterpret these structural counts as passed tests. Tracking stays 1107 total/169 resolved/
938 unresolved. No ledger edits or new ledger-string tests belong to this lane.
Copied Order/OrderStatus instructions do not authorize Order changes.

## Real boundary and controls

NEW PaymentListHttpFixture migrates only Payment in its own loopback PostgreSQL18
Testcontainer. It creates separate disposable Invoice/Receipt databases to supply the actual
registration's DbContext dependencies; their schemas are not migrated or accessed by this suite.
Connection strings come exclusively from that container, never ambient source/production config.
Database writes happen only when a future explicitly authorized test run initializes the fixture.

The factory runs actual Accounting Program, controller, repository, EF SQL, serialization,
RS256 JWT validation, RequirePermission live enforcement and middleware. Ephemeral RSA signs
ordinary test service tokens. External IIamServiceClient is the only replaced dependency;
this is not real Auth-issued identity proof, workload grant proof or live IAM acceptance.
Cache is disabled because GetPaymentsAsync does not use it; no Redis claim. The suite does not
launch Auth, call providers, send notification, touch GCS or activate any invoice consumer.

The draft now has49 xUnit cases: the41 original cases plus eight nullable-string sort cases
added after root's source review. Original coverage:16 named sorts, default ascending ID, numeric exact-ID search,
three literal escape/wildcard cases (percent, underscore, backslash), case-insensitive recipient
and transaction-number searches, Thai Unicode description search, two stable-tie cases,
six nullable date cases, malformed-name/undefined-numeric-sort400, anonymous401/live-denied403
and five bounded/empty-page controls.
Additional coverage: Recipient and Direction/Method/Type.Name NULL first ascending/last descending,
with literal expected IDs for NULL/Alpha/Charlie. The real EF fixture resets all three lookup
tables' names before each test seed, then updates only the selected lookup names to controlled
NULL/Alpha/Charlie values. No schema change or persistent data is involved.
All expected IDs are hand-derived literals independent of production query helpers.
Opposing ID/date/lookup orders expose silently ignored sort choices. Decimal amount and
sanitized navigation wire controls accompany successful pages.

Intranet named forwarding was read fully: FinancesProxy emits enum names, not integers;
its 14 FinancePaymentSort members omit ModifiedDate variants and differ in numeric order.
Its existing BffFinancesProxyContractTests assert a recording-handler query, not this API.
No Intranet source is copied or linked into these tests. Joined real Intranet acceptance is
NOT implemented or claimed; separate ownership and tests would be required.

## Approved bounded runtime implementation

After the actual RED and root approval, the draft carries an Accounting-owned typed 16-name sort from
PaymentRecordControllers through IAccountingService into AccountingRepository. Preserve
all routes, permission/live enforcement, separate contexts, decimal shape and provider exclusion.
Parse numeric search as exact ID; escape PostgreSQL LIKE metacharacters for literal text
substring search. Keep the existing case-insensitive text behavior.

Root subsequently verified that Recipient and Direction/Method/Type.Name are nullable in
the Payment snapshot. Eight new regressions established genuine8RED in the fresh49-case
run, independently reviewed by root. The repository fix adds explicit
boolean null keys for these four string sort keys: NULL first ascending/last descending,
then the existing name direction and stable ascending ID. No collation, provider, schema
or pagination change.

Nullable date draft expectations follow SQL Server ordering: NULL first ascending, last descending.
PostgreSQL defaults differ, so explicit null ordering is required if these expectations are accepted.
The original source has no explicit tie-breaker: ascending ID for equal primary sort keys is
a proposed deterministic pagination clarification, not an already-proven original contract.
Root explicitly approved this clarification before the source-only runtime edit.

Intentional bounds preserved: size omitted20, max250, nonpositive index/size clamp to1;
empty match404, beyond-last page empty200 with totals. Original omitted size meant all matches
and beyond-last empty pages returned404; those divergences are documented, NOT silently
restored. Intranet separately caps size100. Do not expand response bounds as part of sorting.
Malformed names and undefined numeric enum values must return400 rather than silently fall
back. The named-sort contract stays independent of the Intranet enum ordinal mismatch.

## Private preparation and historical RED commands

Prerequisites: explicit exclusive .NET and disposable-container release; private dependency
checkouts under this worktree's `.dependencies`, pinned to existing CI Defaults
`8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3` and CompatibilityContracts
`78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. Do not point builds at canonical siblings:
their bin/obj would be shared writes. Existing PrepareInvoiceDelegationAuthProof build target
also prepares private pinned Auth/owner utilities under this worktree; no skip/waiver is proposed.
Root reviewed and approved that unchanged build-only preparation. It executed for both
the original41 RED and subsequent49 RED builds; future execution still requires exclusive
.NET release. Disposable PostgreSQL release is separately required for tests.

Offline Git preparation is now independently read back: root created detached dependency
worktrees from existing canonical committed Git objects with `GIT_NO_LAZY_FETCH=1`.
This agent verified all five exact HEADs, empty porcelain status, empty branch name and
ignored `.git` paths with lazy fetch disabled. Origins are the expected public HTTPS
`https://github.com/MALIEV-Co-Ltd/{repository}.git` URLs, with no embedded credentials.
The two Accounting dependencies are `.dependencies/Legacy.Maliev.ServiceDefaults` at
`8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3` and
`.dependencies/Legacy.Maliev.CompatibilityContracts` at
`78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`; the three additional paths/pins are listed below.
No clone retry, restore, build, helper, service, database or test execution occurred during
this readback. The earlier network-clone invocation's pre-execution policy rejection remains
recorded; offline preparation does not reinterpret it or establish build/test success.

The entire existing `tooling/prepare-delegation-auth-proof.ps1` was read before execution;
it subsequently executed unchanged during both authorized RED builds.
It is invoked by the unchanged test project's BeforeTargets=PrepareForBuild target. Under
this worktree's `.dependencies/delegation-chain`, it prepares these exact additional checkouts:

| Private directory | Repository and full pin |
| --- | --- |
| `auth-51afbbd` | AuthService `51afbbd6e2829382a3431338abedccf339de33b1` |
| `auth-runtime/Legacy.Maliev.ServiceDefaults` | ServiceDefaults `5c5f9479313710fa576f83d3b396442997a2fcf4` |
| `auth-runtime/Legacy.Maliev.CompatibilityContracts` | CompatibilityContracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7` |

Absent checkouts trigger public HTTPS git clone --no-checkout --filter=blob:none, fetch
--depth=1 of the exact pin and detached checkout. Existing dirty/wrong-pin/wrong-origin
directories cause failure and are preserved. Child processes use a cleared, OS-runtime-only
environment and disabled interactive credentials. Commands default to a bounded300sec timeout.
It builds the pinned Auth API Release, no-incremental, warnings-as-errors with the private
auth-runtime dependencies. It then generates only identical-or-absent `seed-51afbbd-v5/Seed.csproj`
and `Program.cs`, and builds that net10.0 utility Release/warnings-as-errors. The utility's EF
Relational dependency is10.0.12; its project references pinned Auth Infrastructure.

Preparation launches git/dotnet build subprocesses and may restore packages; it does NOT run
the resulting Auth API or Seed utility, start containers, or connect/migrate/seed any database.
Only later existing InvoiceDelegationChainFixture test execution starts its own container/Auth
process and invokes the generated utility. When invoked, Seed validates exact loopback mapped
container authority, four distinct accounting_chain run databases and matching credentials,
then migrates CustomerIdentity/EmployeeIdentity/RefreshSessions and writes two synthetic
employee identities. PaymentListHttpContractTests never calls that fixture or utility.
All required Git checkouts exist. Mandatory Auth API/guarded Seedv5 build preparation and
Accounting build were executed unchanged for the RED baseline; all three builds reported
zero warnings/errors. Future builds must still execute the helper's checkout/generated-byte
guards; do not skip it. The corrected candidate's subsequent build/focused GREEN is
recorded below; later full-suite validation is recorded chronologically below.

The following commands were run from this worktree sequentially for RED, with private
MalievWorkspaceRoot, GITHUB_ACTIONS=false and private DOTNET_CLI_HOME:

```powershell
$env:MalievWorkspaceRoot = Join-Path $PWD '.dependencies'
dotnet restore Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -p:UseLocalMalievDependencies=true
dotnet build Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -c Release --no-restore --no-incremental -warnaserror -p:UseLocalMalievDependencies=true
dotnet test Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PaymentListHttpContractTests --logger 'trx;LogFileName=payment-list-red.trx' --results-directory .artifacts/payment-list-red
```

Build must report 0 warnings/errors before test output counts. Inspect dependency Release
outputs and TRX; fixture/setup failures are not genuine RED. Some existing-behavior controls
are expected to pass; counts and failure assertions must be observed, not inferred.
Freeze for root review before implementing. No commit/closure until complete validation.
Issue39 remains open; existing mandatory unexcluded coverage denominator/gates remain unchanged.

## Actual RED evidence and cleanup

Restore exit0. Unchanged Auth API helper build0warnings/0errors; generated Seedv5
build0warnings/0errors; Accounting test-project Release build0warnings/0errors, exit0.
Focused test exit1:41executed,13PASS,28assertionFAIL,0skipped. TRX errors, timeouts,
aborted, notRunnable and notExecuted counters are all0. Fixture initialized successfully;
failures are assertions on actual HTTP result order/status, not parser/setup failures.

| Genuine assertion group | Failures |
| --- | ---: |
| Named sorting (two existing descending-ID-equivalent cases pass) | 14 |
| Nullable Payment/Created/Modified dates | 6 |
| Malformed/undefined sort returns200 instead of400 | 2 |
| Numeric search returns text-reference row33 instead of exact row22 | 1 |
| Percent/underscore literals include nonmatching row22 | 2 |
| Omitted sort returns descending IDs | 1 |
| Stable ascending-ID ties across pages | 2 |

The13controls passing include401/403, all five bounds/empty-page controls, Thai Unicode,
recipient and transaction-number search, literal backslash, and two named sort coincidences.
Backslash passing is recorded honestly; it does not erase the percent/underscore failures.

Raw TRX preserved at `.artifacts/payment-list-red/payment-list-red.trx`, SHA256
`FE29359D61EFDC4C7B446F445BA4EB425834999B0927E7BC471CB0656D8E9A37`.
No full suite was run. Docker readback after test completion found no own PostgreSQL/Ryuk;
unrelated Created Redis7.4.5 container `deed2d180683`, created2026-09-30T23:31:20Z with
Testcontainers4.13.0 session3040462b-8097-45d5-86ee-925d3d886789, was preserved unchanged.
No worktree-bound dotnet process remained. Index empty; raw artifacts are excluded from staging.

The first private DOTNET_CLI_HOME bootstrap reported installation of an ASP.NET Core HTTPS
development certificate. No trust command was invoked and no certificate manipulation was
performed. Subsequent commands set DOTNET_GENERATE_ASPNET_CERTIFICATE=false.

Root independently reviewed the actual TRX/counters/cleanup before approving only Models,
IAccountingBoundaries, PaymentRecordControllers and AccountingRepository source edits.
Routes, permissions, navigation serialization, existing Invoice/Receipt queries and PageAsync
bounds remain unchanged. Fresh49 focused validation subsequently passed as recorded below;
affected full-suite validation still requires explicit runtime release. No commit/PR/deployment
or persistent database operation is authorized.

The subsequent focused49 RED run verified the existing41 candidate cases and observed the
eight newly identified nullable-string failures before their runtime fix. Its fresh build used
the unchanged mandatory helper/private dependencies and preserved the retained41RED:

```powershell
dotnet test Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PaymentListHttpContractTests --logger 'trx;LogFileName=payment-list-null-strings-red.trx' --results-directory .artifacts/payment-list-null-strings-red
```

This49-case command subsequently ran against the unchanged initial runtime candidate.
Fresh private Release build exit0: Auth API0warnings/0errors (7.92sec), guarded Seedv5
0warnings/0errors (1.97sec), Accounting test project0warnings/0errors (17.33sec).
Focused test exit1:49executed,41PASS,8Assert.Equal failures,0skipped; error/timeout/aborted/
notRunnable/notExecuted counters all0. All original41 candidate cases passed.
For each of Recipient/Direction/Method/Type, ascending expected `[11,33,22]` but actual
`[33,22,11]`; descending expected `[22,33,11]` but actual `[11,22,33]`.
TRX SHA256 `F72202B3DC5DBE7FBBA728A64B90A23B0429DA3FA4A6A177E712CB5F2F0AEE6A`.
Original41 RED/TRX remained unchanged. Owned PostgreSQL/Ryuk and worktree-bound test
processes were absent afterward; unrelated Created Redis was preserved. Free physical
memory was3692964KiB before build and3012440KiB after validation.
Root independently verified the fresh49 RED before approving the repository-only fix.
The subsequently corrected runtime's focused GREEN is recorded below. Full-suite
authorization remains separate.

## Actual focused GREEN and root light gates

Fresh mandatory private Release build exit0: Auth API0warnings/0errors (3.69sec),
guarded Seedv5 0warnings/0errors (2.05sec), Accounting test project0warnings/0errors
(10.69sec). The private dependency environment and helper were unchanged.
Focused command:

```powershell
dotnet test Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~PaymentListHttpContractTests --logger 'trx;LogFileName=payment-list-green.trx' --results-directory .artifacts/payment-list-green
```

Exit0:49executed/49PASS/0FAIL/0skipped; all abnormal TRX counters0. Root independently
checked XPath that all49 results are Passed. Raw TRX SHA256
`FF34B69F1C2B678EB07803BF805B9D6CD2B42AB4D2A66AE01BBFED07027A32A9`.
Both earlier RED TRXs remain unchanged. Fresh Data/API/Tests Release binaries were written
at2026-10-02T11:30:32/33/34UTC, respectively; SHA256:

- Data: `1E8C388C608A857D3D46624BE4F63E26C95F0F522F939B8966B54181C3243202`
- Web executable binary SHA256: `750F5156B30F0059BD33535F961D2188CAF826C492B6B2B457ED0B236CEB086C`
- Tests: `25EBEB627D91F1A8BF8F7F7A584C0C29D8AB52EEFEC40CE5B9D6206460D4EE90`

Owned PostgreSQL/Ryuk and worktree-bound dotnet/testhost were absent afterward; unrelated
Created Redis was preserved. Free physical memory was2006092KiB before build and648764KiB
after validation. The exclusive slot was returned immediately; no wider workloads launched.

Root executed and reported these light gates, not rerun by this agent:

- Exact Workflows `73dd7304ffe85ec504389fd7664cc39070b9f148` JWT wrapper/module fully
  read and archived into unique `.artifacts/jwt-scan-73dd7304-20261002`; scan exit0.
- Gitleaks history:51commits, approximately1.10MB, exit0.
- All seven owned files' content:approximately74902bytes, stdin scan with redact100, exit0.
- `git diff --check`:exit0.

## Actual unfiltered suite and unresolved coverage gate

Fresh whole-solution private Release build with --no-incremental/-warnaserror exit0,
0warnings/0errors (12.30sec); unchanged mandatory Auth API/Seed builds also0W/E
(3.85sec/2.05sec). All five Accounting projects emitted Release outputs. The solution lists
only those five projects with no configuration mapping; external Accounting Defaults and
Contracts emitted private Debug outputs during that solution build. A subsequent serialized
direct Defaults project Release build (including Contracts) reported0W/E, exit0 (1.12sec).
That separate build does not imply the preceding full suite consumed shared Release outputs.
No graph, dependency pin, runtime or solution edits were made to conceal this observation.

Unfiltered command, unchanged private environment and no runsettings/exclusions:

```powershell
dotnet test Legacy.Maliev.AccountingService.slnx -c Release --no-build --no-restore --collect 'XPlat Code Coverage' --logger 'trx;LogFileName=payment-list-full.trx' --results-directory .artifacts/payment-list-full
```

Exit0:362executed/362PASS/0FAIL/0skipped, all abnormal counters0; duration2m30sec.
TRX SHA256 `A6CD0789A5BDF100B8167008AEF261487E29AC154F09859D98E60AC75F3FFF3A`.
Raw coverage `.artifacts/payment-list-full/d491dfbc-4e1f-48c8-b211-1f26618512dc/coverage.cobertura.xml`,
SHA256 `10EA3DED1B500828C9D7312B86B35D53ED444F9380C9760CB33DE01BE1B3A0A4`:

| Assembly | Covered/valid lines | Coverage |
| --- | ---: | ---: |
| Accounting API | 214/617 | 34.68% |
| Accounting Application | 501/572 | 87.59% |
| Accounting Data | 6012/6240 | 96.35% |
| Accounting Domain | 157/174 | 90.23% |
| CompatibilityContracts | 0/57 | 0% |
| ServiceDefaults | 1058/4441 | 23.82% |

API80 gate is NOT satisfied. Its actual raw denominator includes219 generated OpenAPI
lines at0 and398 handwritten lines. At least494 covered lines are needed for80%,280 more
than214; even100% handwritten coverage requires at least96 generated lines. Real mapped
documentation HTTP/schema contracts are the appropriate next probe, not direct calls into
generated helpers, exclusions or denominator manipulation. No promised80 result.

No owned PG/Redis/Ryuk or worktree-bound dotnet/testhost remained; unrelated Created Redis
was preserved. Free physical memory before build4064060KiB, after build4199720KiB,
after full3473400KiB. Runtime slot returned. All prior RED/GREEN artifacts preserved.
Still pending: whole-solution formatting and transitive vulnerability audits; meaningful
additional HTTP acceptance coverage needs reviewed new test ownership and runtime release.
No quality waiver, issue39 closure, commit/push or persistent/Aspire operation.

## Approved source-only HTTP coverage expansion

Four additional NEW files were initially drafted, then built/executed as recorded below: dedicated
`Fixtures/AccountingBoundaryHttpFixture.cs`, `AccountingOpenApiHttpContractTests.cs`,
`PaymentCatalogHttpContractTests.cs` and `PaymentRecordHttpBoundaryTests.cs`.
They declare31 cases (3 documentation,20 catalogs,8 payment/file boundaries).
The previous362 suite count remains historical evidence. The31 HTTP cases plus two atomic
Payment concurrency cases produced the latest actual395 suite denominator. Existing49-case
fixture/tests remain unchanged. Runtime ownership was explicitly extended by root review to
the shared update identity correction, truthful Http.Json metadata options, and Payment-only
concurrency model/current snapshot metadata. No Invoice/Receipt query expansion is included.

The separate collection shares ONE owned PG18 container and three unique context databases,
all three actual migrations, separate Production/Development factories, and ephemeral RSA.
Its per-client JWT grants and strict live IAM stub match exact case permissions; read callers
never receive write grants. Requests without identity remain anonymous. Disposable row reset
and nested host/RSA/container cleanup cannot touch another fixture or ambient database.
Cache remains disabled and creates omit Idempotency-Key. Existing registered idempotency
adapter was read: response replay uses distributed cache with hashed keys; no Redis replay
case/claim is added merely for coverage. Separate test collection serialization does not
change the old tests' fixture, Production environment, read permission or assertions.

Catalog coverage is real201 Location/read/list/update/delete/readback, missing404,
anonymous401, live-denied403/no mutation, and malformed JSON400/no mutation for each of
Accounts/Directions/Methods/Types. Account unknown Branch/Swift are setNULL through owned
EF rows BEFORE the first entity GET/cache population and tested on that first wire response;
no out-of-band cache invalidation is invented. Denied/malformed no-effect controls also
compare independently materialized AsNoTracking SQL row snapshots before/after per resource.
Generated identities do not collide with
the fixture's reserved lookup IDs. Payment coverage preserves PascalCase decimals/Thai
text/scalar FK/no navigations, server identity, concurrency409/no financial mutation,
missing404, malformed400, anonymous401, denied403, metadata-only file roundtrip and
separate files permissions. Committed Intranet FinancesProxy request paths/body/header
were inspected; this is not joined Intranet acceptance.

Documentation coverage requests actual `/accounting/openapi/v1.json`: Production404,
Development title/version and36 Payment-family operations across19 paths, actual query and
registered header parameters, PascalCase money/scalar/schema navigation exclusions, and
real401/403. No unregistered size maximum/default or JWT security transformer is invented.
The installed Defaults AddStandardOpenApi source only adds its information transformer;
security scheme metadata is checked only if actually present, not claimed as registered.
No generated registration `.cs` was persisted in the current API/Defaults obj tree.
API Program calls AddStandardOpenApi compiled in Defaults; source-generator interception
may therefore attach Defaults-generated XML rather than API-generated XML. Actual HTTP
coverage must establish reachability; no direct generated helper calls, reflection forcing,
excluded lines or promised API219 generated-line coverage. Required wire contract failures
must be reported as actual RED for root review before any production correction.

All commands require runtime coordination. The latest direct Tests.csproj Release build
preserved external dependency Release configuration and unchanged mandatory Auth/Seed helper.
Focused33 and unfiltered395 have executed; API80 remains unresolved.

## HTTP and atomic acceptance execution, retained failures

Initial31 HTTP run:22PASS/9FAIL; TRX SHA256
`6F2EAB2660D793644653E32DB89C0E878332B731073951F43631474B4394FC71`.
The four naive PaymentDate500 cases are retained separately in Accounting issue49, not waived
or fixed by39. Valid money/file roundtrip arrangements now use UTCZ, matching existing49
and actual timestamptz storage. Documentation parsing is explicitly case-sensitive; Web
JsonObject options cannot prove casing. Corrected31 then24PASS/7genuineFAIL, SHA256
`B00BE5C8DA0049A8606B3E978A63479112D0EB814AE8E9F0FDF6FD7110CA1A8F`.
Root reviewed actual catalog/payment omitted-ID PUT failures and real Pascal wire/camel
OpenAPI mismatch before approving SetIdentity(item,id) BEFORE tracked SetValues, and
ConfigureHttpJsonOptions matching existing MVC naming/null options. Subsequent31 was
30PASS/1stale-payment409-vs204FAIL, SHA256
`A7B78C8DD87C6F68EBA13C8536F71441CA0D4CD10C6F7855EFF2F5B747DBB5EC`.

Two new real PostgreSQL regressions observed lost update (111.25 overwritten by999999)
and absent concurrency metadata:2genuineFAIL, SHA256
`E7F3D3824B5AD42614B5BF780CF3DC159A21CCF60E0BC36D694FD76632D57F03`.
Root approved only Payment.ModifiedDate.IsConcurrencyToken and the matching CURRENT
snapshot property. No historical migration/designer, column, xmin, provider or type changed.
The subsequently passing guard verifies snapshot/model token, IMigrationsModelDiffer empty,
HasPendingModelChangesfalse, unchanged column type, and no added version columns.
No empty migration is necessary on this observed model; no production DDL was performed.

Latest fresh finite serial build (unchanged private environment/helper):

```powershell
dotnet build Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -c Release --no-restore --no-incremental -warnaserror -m:1 -p:UseSharedCompilation=false -p:UseLocalMalievDependencies=true
```

Exit0: Auth API5.70sec, guarded Seed1.45sec, Accounting25.19sec, each0warnings/0errors;
actual private dependencies emitted Release. Focused atomic2 plus HTTP31:33PASS/0FAIL/
0skip/all abnormal counters0; `.artifacts/payment-boundary33-green/payment-boundary33-green.trx`
SHA256 `ACF0DBC05B348040F16D94CED2851DF8729050D41E2C168C2E7DB3728B751DAA`.

```powershell
dotnet test Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -c Release --no-build --no-restore --collect 'XPlat Code Coverage' --logger 'trx;LogFileName=payment-final395-full.trx' --results-directory .artifacts/payment-final395-full
```

Exit0:395executed/395PASS/0FAIL/0skip/all abnormal counters0,2m39sec.
TRX SHA256 `634AF6AE88542896215F28761391580700D91EF208635FC66D62AF85F06682CC`.
Raw unexcluded Cobertura path `.artifacts/payment-final395-full/3660cb71-88ca-4756-b40a-6ec247ccb598/coverage.cobertura.xml`,
SHA256 `4AA0F67AE2FE5B51253A992419E3265E286C43EBAB54CBFD8E9901D121D3B317`.

| Assembly | Covered/valid raw line entries | Coverage |
| --- | ---: | ---: |
| Accounting API | 271/623 | 43.499% FAIL80 |
| Accounting Application | 501/572 | 87.587% |
| Accounting Data | 6059/6242 | 97.068% |
| Accounting Domain | 167/174 | 95.977% |
| CompatibilityContracts | 0/53 | 0% |
| ServiceDefaults | 940/3478 | 27.027% |

Shared denominator differences versus the historical solution run reflect observed Release
versus Debug graph outputs, not exclusions. All raw collector assemblies are reported.
API contains219 generated XML line entries at0 and404 authored entries with271covered.
At least499/623 are required for80%,228more than observed. Even every authored line alone
cannot reach80; actual documentation registration/contracts must be investigated legitimately.

Owned Accounting PG/Redis/Ryuk and worktree-bound test processes were absent after terminal;
foreign data containers and Created Redis preserved. Post-run free memory5153432KiB.
All RED/GREEN artifacts remain unchanged and excluded from staging; index empty.

## Latest static checks and remaining scope

Whole `dotnet format Legacy.Maliev.AccountingService.slnx --verify-no-changes --no-restore`
initially found whitespace only in three NEW tests. Scoped whitespace formatting corrected
those files; whole-solution verification then exit0. No behavior/assertion changes.
All five Accounting project `dotnet list <csproj> package --vulnerable --include-transitive
--no-restore` audits exit0 and report no vulnerable packages using current NuGet sources.
Gitleaks history51commits/~1.10MB exit0. Expanded15-owned-file stdin scan initially flagged
the historical API binary SHA256 evidence label as generic-api-key; it was not a credential.
The explicit Web executable binary SHA256 label and complete post-documentation content
scan subsequently passed (~169.79KB,exit0). This is a label correction, not an allowlist.
No suppression/allowlist or generated-file coverage exclusion is introduced.

Smallest uncovered authored Payment behavior: six mapped summary routes (monthly,weekly,
monthly/income/job,yearly,yearly/income,yearly/expense),14 raw controller line entries;
real SQL financial totals/filtering/empty404 and401/403 are meaningful candidate acceptance,
not reflection. Payment file PUT is one uncovered line; idempotent replay is six uncovered
entries and needs an actual isolated Redis fixture/store, never fake replay coverage.
Invoice/Receipt CRUD and workflow gaps require separate producer/consumer and ownership
review before test expansion. API generated XML registration remains a real documentation
contract investigation, not an authorization for blind cross-owner transplantation.
No commit/PR/issue closure while API80 fails.

Separate NEW `AccountingOpenApiXmlHttpContractTests.cs` now drafts five real served-document
cases: three existing receipt-workflow operation XML summaries and the existing
Payment/PaymentFile type and property descriptions. Those controllers and Domain models were
read directly; no missing comments or JWT metadata are invented. API and Domain both enable
GenerateDocumentationFile. API currently receives OpenApi10.0.12 transitively from Defaults;
the shared compiled registration cannot establish local API XML interception. Any proposed
direct same-version official reference/registration requires genuine RED and root review.
Original three documentation controls,49 listing cases,31 HTTP cases and atomic2 remain intact.

Fresh private serial direct Release build for NEW5: Auth3.49sec/Seed1.73sec/Accounting27.39sec,
all0warnings/0errors; private dependencies emitted Release. Focus command:

```powershell
dotnet test Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~AccountingOpenApiXmlHttpContractTests --logger 'trx;LogFileName=accounting-xml5-red.trx' --results-directory .artifacts/accounting-xml5-red
```

Exit1:5executed/0PASS/5genuineAssert.EqualFAIL/0skip/all abnormal counters0.
Actual served operation summaries and both type descriptions were null instead of existing
XML literals. HTTP200/path/schema assertions were reached; these are not fixture/setup failures.
TRX SHA256 `E21C97AD0F1862777E5594792A88B19F3BC21DB29DE18359145080C246DCADA3`.
Prelaunchfree3032792KiB/post2734056KiB. Owned PG/Ryuk session absent afterward; foreign
runtime/resources preserved. No production correction before root review of genuine RED.

Root independently reviewed NEW5 RED and approved the minimal local literal
`builder.Services.AddOpenApi("v1")` following standard registration, plus direct API
`Microsoft.AspNetCore.OpenApi` package10.0.12 (same existing transitive version), allowing
the official application compiler target to register its own XML documentation. These two
lines are now applied; title, mapping, JSON options, auth and schemas otherwise unchanged.
Fresh prelaunch memory1376948KiB failed the required2.5GiB guard before build/restore/test
launch. After capacity recovered, fresh direct serial Release build with restore succeeded:
Auth4.37sec/Seed1.69sec/Accounting28.04sec, each0warnings/0errors and private Release outputs.
The combined NEW5/original3 documentation focus executed8:6PASS/2FAIL/0skip/all abnormal0,
TRX SHA256 `B7868472377370F484A86373A485E8410E848775BF792C0F94CC2C26A95F5ABB`.
All three operation summaries and original three controls passed. Both type descriptions
passed; property expectations then failed because official XML transformation combines
existing summary and value text (`Gets or sets the amount.\nThe amount.` and analogous
Bucket text), rather than summary alone. Test-only exact full-XML expectation correction
is proposed, not a runtime defect or a weakened assertion. No all-GREEN or coverage claim.

Root reviewed and approved NEW5-only exact property expectations including the original
value strings after newline; retained failed8 remains an incomplete expectation, not a
runtime failure. Fresh private serial build:Auth2.73sec/Seed1.87sec/Accounting21.05sec,
all0warnings/0errors. Exact NEW5 plus original3 controls then8PASS/0FAIL/0skip/all abnormal0:
`.artifacts/accounting-docs8-final/accounting-docs8-final.trx`, SHA256
`47D264A373B8AE2AE5565C397F603D7D1D274718D814DC806D811F28418C1EEF`.
Unfiltered400 XPlat coverage executed with no filter/runsettings/exclusions:

```powershell
dotnet test Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -c Release --no-build --no-restore --collect 'XPlat Code Coverage' --logger 'trx;LogFileName=payment-final400-full.trx' --results-directory .artifacts/payment-final400-full
```

Exit0:400executed/400PASS/0FAIL/0skip/all abnormal0,3m7sec; TRX SHA256
`5D365B8EA22514F549D4B8220FB1726EF9650DF9197FFE5F42ECDDBD3238E362`.
Raw Cobertura `.artifacts/payment-final400-full/7898c4fb-b80d-4e0c-ba49-50c96d322719/coverage.cobertura.xml`,
SHA256 `DAB60A82AD7DBE74FE36523DD2DFC3F7B2FE3C408508EF181E66ED41A5F1EBDC`:

| Assembly | Covered/valid raw line entries | Coverage |
| --- | ---: | ---: |
| Accounting API | 715/952 | 75.105% FAIL80 |
| Accounting Application | 501/572 | 87.587% |
| Accounting Data | 6059/6242 | 97.068% |
| Accounting Domain | 167/174 | 95.977% |
| CompatibilityContracts | 0/53 | 0% |
| ServiceDefaults | 940/3478 | 27.027% |

Official local XML registration adds actual compiler-generated XML cache/registration code,
so the raw denominator legitimately grew. Generated443/547 and authored272/405 are observed;
80 requires762/952 (47additional covered entries if denominator stable). No denominator
waiver, generated forcing or coverage exclusion. Owned full session PG/Redis/Ryuk and
worktree-bound dotnet/testhost were absent after terminal. Pre-fullfree7463420KiB; post
1219660KiB reflects observed other workload pressure, so no further executable check launched.
Latest runtime/docs changes still require fresh formatting/audit/scans after capacity release.
No summary fixture/tests added yet and no completion/commit/PR claim.

## Next Payment summary acceptance, source-only pending review

NEW `PaymentSummaryHttpContractTests.cs` covers24cases: six mapped projections each with
independently seeded financial200, empty404, anonymous401, and live-denied403; SQL row
snapshots independently prove no mutation. Existing31/atomic hosts retain System time.
Only a separate third summary-only Production factory selects fixed2026-10-08T12:00Z;
the existing Production/Development constructor calls and Client defaults remain unchanged.
Nested disposal covers that third host even if another host fails disposal. No new container,
permissions bypass, runtime clock edit, schema change or production code is added.

Common original source and current consumer contract were read completely: original
SummariesController, FinancialSummary, SummaryDetail and DateTimeExtensions at checkpoint
a1df (summary controller last modified5fac), current committed Intranet FinancesProxy,
FinancesEndpointMapper, summary route bindings, Dashboard monthly summary consumer and
original finance chart models. Pascal Details and five decimal/string fields are asserted;
yearly consumers parse DateTime-key decimal dictionaries, pass year/currency filters, and
map APIempty404 to consumer200 empty projection. Tests are Accounting boundary proof, NOT
joined Intranet proof. Hand-derived seeds useOct6/Sep29/Jun15 interior dates and nonzero
previous amounts for both currencies; date edges and zero-previous Job divergence below
are not silently accepted. Root read the complete NEW24 tests and fixture, independently
inspected the committed source controller/date helpers and target repository windows.
Fresh pinned producer preparation and direct test-project Release build passed0W/E;
actual focused24/24 passed with0failed/skipped. TRX SHA256:
`25AB3532D644B2980E80DCA088D2A844D5B2BA8CC7F67602B04C90912BC57BB1`.
The first shell wrapper falsely read a stale parent LASTEXITCODE after the successful
helper; no tests ran in that attempt. Fresh preparation was repeated with script-success
checking before build/tests, with no source or test changes. Planned full424 is not yet
executed; the focused coverage is not a full-suite quality gate.

## Draft gap issue: decide legacy Payment summary edge semantics

Tracked as Accounting [issue50](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.AccountingService/issues/50)
in Project2 after independent source/target inspection. No waiver, closure or runtime fix.
Suggested title: Preserve or explicitly decide legacy Payment summary date edges and
zero-previous Job income currencies.

Source owner5fac706a7983a6d359b39acbd670e6800afe020e:
`Maliev.PaymentService.Api/Controllers/SummariesController.cs` and
`Maliev.PaymentService.Api/Extensions/DateTimeExtensions.cs`. Current owner:
AccountingRepository.GetFinancialSummaryAsync/SummaryWindows/GetYearlyDetailAsync.
Consumers: Intranet FinancesProxy and mapped finance summaries/trends; original chart
models consume DateTime-key decimals. Parent quality issue39 remains open; this gap is
distinct from issue49 naive PaymentDate500 and issue48 quotation notification adoption.

Observed source/code divergence (not yet runtime edge acceptance): original last-day helpers
return final-day midnight and queries use <=; target half-open next-period windows include
the remainder of that final day. Original monthly Job summary skips a currency when previous
Job income is0; target includes it with DeltaPercent0. Original missing previous currency
groups can also throw, whereas target safely sums0. Do not resurrect accidental exceptions
or silently declare these changes accepted. Required follow-up: real HTTP/PG edge matrix for
Sunday/end-month/year-final-day nonmidnight timestamps, previous0/missing/null currency,
document exact consumer-visible output; explicit owner choice before runtime modification.

## Read-only bounded receipt-workflow boundary proposal

Existing controller provenance: a7dd847b9af43a01faea12434181322413442a41, closed issue4
receipt orchestration; quality acceptance belongs issue39, not active invoice intent issue37
or quotation consumer issue48. Controller, real ReceiptWorkflowService/Store/Journal/
Postgres lock and committed Intranet InvoiceDetailProxy/InvoiceReceiptEndpointMapper read.
No open Accounting PR was listed during this readback; other worktrees remain preserved.

Smallest genuine registered HTTP candidates, not implemented yet: create/delete/email with
missing, malformed or empty-D UUID keys=>400; create/email employee0 or negative=>400;
all three routes valid headers/missing invoice=>404; each route anonymous401/live-denied403.
Exact forced-live permissions Create/Delete/Update; DELETE deliberately needs no employee.
BFF derives employee from trusted claim and canonicalizes operation UUID; it retains downstream
failure status, so these are actual contracts, not arbitrary controller branch coverage.
Invalid headers reject before workflow invocation. Missing-invoice requests read journal,
take/release real PG advisory lock, then read absent invoice BEFORE any receipt/file/link or
journal Set; no upstream call or persistent row mutation is expected. These claims require
independent disposable SQL snapshots (Invoice/Receipt/Payment rows) and bounded fail-closed
remote transport observation in tests, not source-only success claims. Valid Remove of an
existing invoice without receipt is NOT a no-mutation test (writes replay journal), excluded.
No successful render/upload/email, schema activation, provider call or production write is
authorized by this proposal. Root must review new file/context access/transport ownership
before any broader test implementation or execution.

## Executed receipt boundaries and integrated446 acceptance

Root reviewed the complete NEW22 test and fixture before execution. The receipt-only
fourth Production host uses the real registered workflow, PostgreSQL store/advisory
lock, Redis idempotency journal and authentication/forced-live permission enforcement.
Its private Redis7.4-alpine container is not shared with any service or operator run.
Independent SQL row-count and semantic row digests cover all three accounting contexts;
the private Redis observation hashes ALL keys (not just an assumed prefix). Fail-closed
HTTP transports verify zero outbound provider/render/upload/email requests. No fake
workflow, store, journal or production connection is used.

Cases: nine missing/malformed/empty UUID headers across create/delete/email; four
employee0/-1 create/email cases; three valid-header missing-invoice404 cases; six
anonymous401/live-denied403 cases. Every case asserts unchanged SQL/Redis state and
its exact permission-call count. Nested cleanup disposes hosts, multiplexer, Redis,
ephemeral RSA and PostgreSQL even on setup/teardown failure. Existing cases unchanged.

Fresh mandatory pinned producer preparation and direct Release build passed0W/E.
Focused22/22PASS,0failed/skipped/abnormal results; TRX
`.artifacts/receipt-rejection22/receipt22.trx`, SHA256
`977730954EC0FA4C8FD8A465D03523CDBC04B807F0C68FBF1146639B55C1FCCD`.
The earlier NEW24 summary focus passed independently (hash recorded above).

Fresh preparation/build again passed0W/E before the unfiltered affected suite:

```powershell
dotnet test Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -c Release --no-build --no-restore --collect 'XPlat Code Coverage' --logger 'trx;LogFileName=full446.trx' --results-directory .artifacts/payment-full446
```

Terminal exit0;446executed/446PASS/0failed/0skipped/all abnormal counters0,2m41sec.
Root independently parsed counters, raw coverage and hashes. TRX
`.artifacts/payment-full446/full446.trx`, SHA256
`6477330E7A1836B7B2072491D6B29A9A1A105DAD17F77229E691B41E8078A336`.
Raw Cobertura
`.artifacts/payment-full446/832a16ea-1c2a-4cae-a26a-bdfe891c9be5/coverage.cobertura.xml`,
SHA256 `0B5B66277F2B3638F7004BD1A7DA1CBF552698706501573351C9D5FA7C57F1E6`.

| Assembly | Covered/valid raw line entries | Coverage |
| --- | ---: | ---: |
| Accounting API | 770/952 | 80.882% |
| Accounting Application | 512/572 | 89.510% |
| Accounting Data | 6075/6242 | 97.325% |
| Accounting Domain | 167/174 | 95.977% |
| CompatibilityContracts | 0/53 | 0% |
| ServiceDefaults | 940/3478 | 27.027% |

Every owned service assembly meets80. Shared dependencies and the all-module raw
denominator8464/11471 (73.786%) remain separately reported; not an all-module80 claim.
No test filters, collector exclusions, generated helper forcing, threshold change or
runsettings waiver. Historical400/API75.105% evidence remains unchanged. Two raw
Data/Defaults line-entry differences versus400 are observed collector output, not source
exclusions; detailed comparison is recorded with final static review. Runtime payment
concurrency/model-diff no-DDL guards pass in the integrated suite. This does not close
separate summary-edge issue50, naive PaymentDate issue49, other source owners or
production/Aspire data parity. No schema DDL, deployment, provider execution or cutover.

Root's final XML-node comparison corrects the historical400 reporting error above:
direct `SelectNodes('classes/class/lines/line')` and a complete class/file/line multiset
comparison show IDENTICAL6242 Data and3478 Defaults line entries in400 and446.
The earlier6244/3480 figures counted two empty-container pseudoentries; they were not
actual executable line nodes. Raw immutable coverage files are unchanged. There was
no source/collector denominator shift for those assemblies. The current API952 nodes
and every other current owned denominator were independently checked the same way.
Root repeated whole-solution format verification successfully (exit0). An earlier
attempt supplied unsupported format `-p:` syntax and exited before analysis; the
correct environment-based private dependency command passed without modifications.
All five project transitive NuGet vulnerability audits exit0/no vulnerable packages.

Final secret/static review: independently read the exact CI-pinned Workflows73dd7304
JWT resource wrapper AND scanner, then materialized that immutable detached pin
under this worktree's private `.dependencies/workflows-jwt-73dd730` and executed its
resource scan successfully. Canonical scanner bytes differ, so they were not used
as a substitute. Gitleaks51commit history (~1.10MB) and complete19owned-file content
(~218.10KB) both exit0/no leaks; `git diff --check` exit0. No new resource files,
scanner suppression or secret allowlist. All19 files comprise one integrated
Payment/registered-HTTP acceptance slice; the full build/test/coverage gate applies
to that exact tree. Required protected-head/main CI and issue closure remain pending.

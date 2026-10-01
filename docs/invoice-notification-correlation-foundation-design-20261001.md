# Accounting37: invoice notification correlation foundation

## Independent root acceptance for bounded issue42

Root independently read the codec, immutable model, exact schema/migration,
store/readiness and new literal/concurrency/fault tests. Release build passed
with zero warnings/errors; focused89 and the unfiltered full214 passed with
zero failures/skips. Full TRX: `TestResults/root-foundation42-full/root-foundation42-full.trx`.
Raw unexcluded Cobertura: `TestResults/root-foundation42-full/9aa8b61a-557b-41a6-bf8f-1ceffc0fd21b/coverage.cobertura.xml`,
SHA256 `C1E8540C92FC414CC00F5C712CD2B301D1614C4817CE7E7BCABFA9785D4D13C1`.
API33.98%, Application85.29%, Data95.78% (includes generated migrations),
Domain85.63%; API quality39 remains open, without exclusions or waiver.
Whole-solution formatting, five transitive vulnerability audits, all15 scoped
secret scans and diff checks passed. A diagnostic used a nonexistent private
dependency-directory name before the no-build full test; the test still ran
all214 successfully, and subsequent static commands used the actual owned
`.dependencies` root. No source/runtime or test assertion was changed for that
diagnostic.

Issue42 covers only this unregistered foundation. Parent37 retains phase CAS,
validated remote-receipt continuity and workflow integration; shared authority41,
provider activation, persistent DDL, full source parity and Aspire remain open.
No deployment or persistent schema/data writes occurred. Protected-head and
post-merge-main CI remain required before bounded issue42 closure.

## Current stage — 2026-10-01

CURRENT: implemented bounded codec, reviewed additive schema and guarded Admit/Read store,
on accepted Accounting `086732f926d5d78144bbcadaafcc0371c0559975`
(exact-main CI `36847614857` SUCCESS, root-accepted 128 tests). No workflow,
adapter, controller, DI or activation edits. Root approved the exact EF mapping,
migration/snapshot and bounded store implementation. Schema execution is exclusively
fresh disposable PG18; no persistent DDL or activation. The first implemented
store focus passed 80/80, including seven actually reached fault controls.
Final private Release0W0E, focus89/89 and full214/214 zero skips pass; whole-solution
format, five package audits and unsuppressed scoped secret checks pass. Candidate
is frozen for root independent acceptance; API coverage39 remains open.

### Historical test-first stages (retained chronology, not current runtime description)
The initial PostgreSQL test is a **stronger-contract missing physical authority
proof**, not a historical source bug or proof of unimplemented CAS behavior.
Existing held37 lost-response evidence remains in ignored TestResults. No
scaffold was needed to establish the missing relation. After root exact schema
review, typed model/interface/codec/store seams were approved solely to compile
literal vector and typed admission RED. Historic Frame/Compute returned empty;
codec is now implemented and 44 controls pass. At the initial seam stage the store
always threw fixed unavailable; those tests did not reach transaction/readiness.
The current implementation supersedes that inert stage but remains unregistered;
no phase-transition/execution fence, workflow or provider capability is implemented.

Producer prerequisite: accepted Notification
`cb51aaee4a01519c82d13330a738e7cc8f698675`, default-OFF v2. Accounting41 trusted
modern IAM authority remains OPEN; this foundation neither repairs nor bypasses
that boundary. No runtime registration, resource provisioning, persistent DDL,
grant, activation, provider email, resend worker or operator repair is authorized.

## Eligibility and limits

An invoice existing in PostgreSQL does NOT prove that this workflow freshly
created it, or that nobody previously sent its notification. Future integration
must call this store only from the in-memory fresh acknowledged `CreateAsync`
branch after verified COMMIT AND transaction/context disposal. Reconciled
`FindByNumber` results, legacy rows, missing delegation/origin lineage and
uncertain invoice COMMIT stay blocked. A client-supplied eligibility boolean is
not authority. The store independently verifies the invoice exists and the
origin operation's immutable quotation/employee/service tuple in Pending with no
terminal ResultJson, but cannot
retroactively certify creation from those facts alone.

The first rollout should require the existing validated delegated-origin tuple;
ordinary historical routes remain unchanged/default-OFF, not automatically
backfilled. Wiring that admission rule is a later separately reviewed slice.
No Number uniqueness, payload regeneration, financial fields, enum additions or
cross-DB transaction is proposed. Public email states remain exactly 0/1/2;
later operation-status wire must distinguish acceptance from recipient delivery.

## Reviewed physical authority (applied only to disposable PostgreSQL)

One ordinary, explicitly qualified `public."InvoiceNotificationCorrelation"`
table in InvoiceDbContext. Exactly these 21 columns; no raw recipient, name,
subject, body, PDF, payload SHA, token, delegation or serialized result.
Internal actor subjects are authority identifiers, never response/log content.
Scalar InvoiceID is deliberate: no cascading FK destroys non-expiring recovery
authority; a restrictive FK would silently change existing invoice DELETE.
Existence is checked during admission, retained authority survives later deletion.

```sql
CREATE TABLE public."InvoiceNotificationCorrelation" (
  "IntentID" uuid PRIMARY KEY,
  "InvoiceID" integer NOT NULL,
  "Purpose" varchar(32) NOT NULL,
  "QuotationID" integer NOT NULL,
  "WorkflowOperationID" uuid NOT NULL,
  "OriginIssuer" varchar(512) NOT NULL,
  "OriginEmployeeSubject" varchar(256) NOT NULL,
  "OriginServiceSubject" varchar(128) NOT NULL,
  "SenderIssuer" varchar(512) NOT NULL,
  "SenderServiceSubject" varchar(128) NOT NULL,
  "PayloadFrameVersion" varchar(64) NOT NULL,
  "BindingVersion" varchar(64) NOT NULL,
  "BindingKeyID" varchar(64) NOT NULL,
  "PayloadBinding" bytea NOT NULL,
  "Phase" varchar(32) NOT NULL,
  "Version" bigint NOT NULL,
  "RemoteVersion" bigint NULL,
  "CreatedAt" timestamptz NOT NULL,
  "UpdatedAt" timestamptz NOT NULL,
  "AdmissionIssuedAt" timestamptz NULL,
  "ExecutionIssuedAt" timestamptz NULL,
  CONSTRAINT "UQ_InvoiceNotificationCorrelation_InvoicePurpose"
    UNIQUE ("InvoiceID", "Purpose"),
  CONSTRAINT "CK_InvoiceNotificationCorrelation_Identity" CHECK (
    "InvoiceID" > 0 AND "QuotationID" > 0 AND "Purpose" = 'invoice-issued'
    AND "SenderServiceSubject" = 'service:legacy-accounting'
    AND "IntentID" <> '00000000-0000-0000-0000-000000000000'::uuid
    AND "WorkflowOperationID" <> '00000000-0000-0000-0000-000000000000'::uuid
    AND "IntentID" <> "WorkflowOperationID"
    AND octet_length("PayloadBinding") = 32
    AND length("OriginIssuer") > 0 AND length("OriginEmployeeSubject") > 0
    AND length("OriginServiceSubject") > 0 AND length("SenderIssuer") > 0
    AND length("BindingKeyID") > 0
    AND "PayloadFrameVersion" = 'notification-payload-v1'
    AND "BindingVersion" = 'accounting-invoice-notification-hmac-v1'),
  CONSTRAINT "CK_InvoiceNotificationCorrelation_State" CHECK (
    "Version" > 0 AND ("RemoteVersion" IS NULL OR "RemoteVersion" > 0)
    AND "UpdatedAt" >= "CreatedAt"
    AND "Phase" IN ('Prepared','AdmissionIssued','Admitted','ExecutionIssued',
                    'OutcomeUnknown','ProviderAccepted','RejectedBeforeSubmission')
    AND ("AdmissionIssuedAt" IS NULL OR "AdmissionIssuedAt" >= "CreatedAt")
    AND ("ExecutionIssuedAt" IS NULL OR
      ("AdmissionIssuedAt" IS NOT NULL AND "ExecutionIssuedAt" >= "AdmissionIssuedAt"))
    AND ("Phase" = 'Prepared' OR "AdmissionIssuedAt" IS NOT NULL)
    AND ("Phase" <> 'Prepared' OR
      ("AdmissionIssuedAt" IS NULL AND "ExecutionIssuedAt" IS NULL AND "RemoteVersion" IS NULL))
    AND ("Phase" <> 'AdmissionIssued' OR
      ("AdmissionIssuedAt" IS NOT NULL AND "ExecutionIssuedAt" IS NULL))
    AND ("Phase" <> 'Admitted' OR
      ("AdmissionIssuedAt" IS NOT NULL AND "ExecutionIssuedAt" IS NULL AND "RemoteVersion" IS NOT NULL))
    AND ("Phase" <> 'ExecutionIssued' OR
      ("ExecutionIssuedAt" IS NOT NULL AND "RemoteVersion" IS NOT NULL))
    AND ("Phase" NOT IN ('OutcomeUnknown','ProviderAccepted') OR
      ("ExecutionIssuedAt" IS NOT NULL AND "RemoteVersion" IS NOT NULL))
    AND ("Phase" NOT IN ('ProviderAccepted','RejectedBeforeSubmission') OR "RemoteVersion" IS NOT NULL))
);
```

Purpose uniqueness EXCLUDES workflow and IntentID: a different UUID/workflow for
the same invoice cannot create another send authority. Existing producer uniqueness
includes workflow and is insufficient alone. No automatic TTL, DELETE, reset or
Down: additive forward-only migration Down throws before any SQL. No startup DDL.
Immutable columns are never in UPDATE SET; no caller timestamps/phase/version.
Application validation is stricter than SQL about bounded valid UTF-8 subjects,
issuer syntax and configured allowed identity. Physical checks are not trust proof.

Readiness runs uncached before admission/fences: exact public namespace, ordinary
table/no partition/view/rule/RLS/user trigger, all live nondropped columns including
generated/identity markers, exact types/capacities/nullability/defaults, exact PK and
validated nonpartial unique scope, validated canonical checks, no unexpected checks
or rules, expected migration history plus EF model/snapshot. Missing or drifted
shape returns fixed unavailable and **zero Notification RPC**; no EnsureCreated
fallback. Keyring/frame support required. No ambient search_path lookup. Migration
tests apply real discovered Up/rerun/model, not just manually create the SQL above.

## Immutable binding / rotation

Payload digest transiently uses exact producer `notification-payload-v1`: strict
UTF-8; tagged big-endian lengths; null versus empty arrays; ordered cc/bcc and
attachments; literal Thai/culture/unpaired-surrogate controls. Capture once; no
regeneration on restart. Local HMAC-SHA256 uses a distinct owner-managed keyring,
NOT the producer secret. Domain-separated versioned tagged frame includes every
immutable field above through BindingKeyID plus transient lowercase producer
payload digest. UUIDs canonical lower D; positive ints invariant decimal. Each
field has ASCII tag + NUL, present marker + UInt64BE length + strict UTF8 bytes;
no delimiter concatenation. No nullable identity fields. Include key ID in frame.
Exact frame order/tags: `binding-version`, `intent-id`, `invoice-id`, `purpose`,
`quotation-id`, `workflow-operation-id`, `origin-issuer`, `origin-employee-subject`,
`origin-service-subject`, `sender-issuer`, `sender-service-subject`,
`payload-frame-version`, `binding-key-id`, `payload-digest`. All are required
present fields. A public synthetic byte00..1f key and literal 697-byte frame have
independently derived HMAC hex
`0AB8029E85942E82C06CC92CBF35AAF4DEA476FA49D29894FB4A07EF32F51FE7`.
The new test contains the complete independently derived frame, not a call to
the implementation to construct its expectation. Producer payload null/empty/
ordered attachment framing remains a separate consumer-codec test wave; a
digest-only local frame test does not prove how those payload bytes were captured.

On same intent replay, read existing row FIRST and compute using retained key ID;
fixed-time compare, no active-key rehash. Missing retained key = unavailable,
changed tuple/payload = conflict without row disclosure. New admission uses active
key. Retain old keys while any correlation references them; no automatic expiry.
Independent literal byte/HMAC vectors precede codec implementation; expected
values never call the implementation helper. Secrets/frames never logged.

## Internal store contract and monotonic phases

Proposed Application model: immutable `InvoiceNotificationOrigin` and binding
request, immutable correlation snapshot, typed conflict/unavailable exceptions.
Proposed store methods: `AdmitAsync`, `ReadAsync`,
`IssueAdmissionAsync(intent, expectedVersion)`,
`RecordReceiptAsync(intent, expectedVersion, validatedReceipt)`,
`IssueExecutionAsync(intent, expectedVersion)`. No arbitrary SetState/retry/reset.
No HTTP or provider dependency in this store; no public DTO or DI change here.

- Admit creates Prepared/version1 only after immutable eligibility validation.
  Same intent/same binding returns verified row, different binding conflicts.
  Different intent/workflow for occupied invoice-purpose conflicts, no winner leak.
- Prepared -> AdmissionIssued increments local version and persists timestamp
  BEFORE PUT. Only the verified fence winner may issue the first PUT. Missing,
  stale or uncertain commit cannot authorize network effects.
- Matching producer Admitted receipt advances AdmissionIssued -> Admitted;
  retained remote version positive, immutable entire receipt identity verified.
- Admitted -> ExecutionIssued CAS is the one execution-issued fence. Only its
  current winner with verified COMMIT + BOTH disposal acknowledgments may POST.
  Reading ExecutionIssued later never grants another execution, even if GET says
  Admitted. Never forward an uncommitted correlation/fence.
- Submitting/OutcomeUnknown receipts retain OutcomeUnknown; GET may later prove
  ProviderAccepted. ProviderAccepted terminal never downgraded. Duplicate older
  receipt cannot overwrite newer remote/local version; equal-version different
  state is conflict. RejectedBeforeSubmission is terminal if the producer ever
  emits it, NOT an automatic retry grant (currently no production transition).
- OutcomeUnknown/ProviderAccepted require retained admission AND execution
  timestamps plus positive remote version. An uncertain PUT keeps AdmissionIssued;
  an uncertain POST keeps ExecutionIssued until matching GET yields a receipt.
  Missing lineage is unavailable/conflict, never invented from receipt state.
  Thus GET acceptance after an uncertain POST is representable without losing the
  durable local execution fence; acceptance cannot create execution lineage.
- Unknown admission/execute response -> GET SAME stored UUID only. GET404/cache
  miss/503 never proves no send. Payload missing after restart -> blocked/readback
  only; never regenerate/re-email. Receipt states describe provider acceptance,
  not delivery. No terminal expiry or automatic unknown->Prepared transition.

Use exact registered typed InvoiceDbContext options and fresh owned contexts;
retain original scoped/advisory context. Configured strategy admits ONE guarded
attempt; catch failures inside delegate, rethrow outside, never replay generated
IDs/COMMIT. Before submission verified rollback may propagate original/OCE;
submitted COMMIT or rollback/disposal ambiguity -> fixed typed unavailable with
internal retained cause. Bounded independent rollback cleanup per configured
command budget. Return usable snapshot/fence only after COMMIT and transaction
AND context disposal; rereading a row is not an execution-issued success token.

## Test waves and exclusions

Initial executable proof (historical): actual PG18 plus registered retry-enabled options and
real discovered accepted migrations; exact public relation absent is RED. Control
shows legacy delegated admissions can independently complete different operations
with the same invoice ID; they are workflow receipts, not invoice-purpose authority.
This control does not authorize that behavior in the new store.

The original test-first plan, before implementation, required real two-context
same-purpose/different UUID+workflow races, exact replay/all-field conflicts, missing
invoice/origin refusal, retained old key/no key/changed payload, culture/null/Thai
goldens; CAS one winner/stale updates/terminal non-downgrade. Fault instrumentation
must actually reach first SQL/COMMIT/teardown, assert durable 0/1 rows and one attempt;
transient pre/post-COMMIT, caller cancel, rollback/transaction/context disposal
uncertainty. Component callback faults are NOT proof of physical network loss.
No RPC fixtures until later consumer slice. No unreachable fault counted as RED.

Source lineage (committed mirror only):
- `5fac706a7983a6d359b39acbd670e6800afe020e` root: original Intranet invoice
  Create/InvoiceService/email source owner; financial/wire compatibility retained.
- `c821605b7ecde5f79d01966888b116defb10650d`, parent
  `92e8ad50c5d33f7458e86bf9cd33eabbdb1eb220`: Intranet invoice Create/resilience
  and Quotation Accept paths. Reload fence is not durable payload recovery.
- `487d06df2f4614c53bead4b19d53d2613a46fd64`, parent
  `11c1c03995a6aa12708781dc2e7e3b760c040105`: Invoice API paid-outcome readback,
  model/tests and Web marketing/SQLServer deployment cohort. Paid/marketing/SQL
  parts separately owned/excluded; this slice does not retire the whole SHA.

Intranet202 closed interim fence is separate from durable consumer recovery.
Cross-DB invoice/quotation/PDF/File/Notification saga, original payload resume,
production PG capacity/retention/key provisioning, provider behavior, authority41,
producer/consumer activation and old-writer drain remain separate gates.

## Historical initial foundation evidence / freeze

- New branch `codex/accounting-notification-correlation-20261001`, unchanged base
  `086732f926d5d78144bbcadaafcc0371c0559975`; only this new doc and
  `Legacy.Maliev.AccountingService.Tests/InvoiceNotificationCorrelationFoundationTests.cs`.
- Each command sets `MalievWorkspaceRoot` to this worktree's absolute
  `.dependencies` and `UseLocalMalievDependencies=true`. Own clean detached pins:
  Defaults `8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3`, Contracts
  `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7` (verified Git HEAD/status).
- `dotnet build Legacy.Maliev.AccountingService.slnx -c Release --no-restore --nologo`
  terminal exit0, **0 warnings / 0 errors**. Private Auth proof helper also built
  0W/0E; no database/provider process started by the helper.
- `dotnet test Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~InvoiceNotificationCorrelationFoundationTests' --logger trx --results-directory TestResults/correlation-foundation-red`
  terminal exit1: **2 total, 1 intended missing-authority RED, 1 control GREEN,
  0 errors / 0 skipped**. TRX:
  `TestResults/correlation-foundation-red/natth_MALIEV-31USFIV_2026-10-01_17_32_59_net10.0.trx`.
- `dotnet format Legacy.Maliev.AccountingService.slnx --verify-no-changes --no-restore --include Legacy.Maliev.AccountingService.Tests/InvoiceNotificationCorrelationFoundationTests.cs`
  terminal exit0. `git diff --check` exit0; new files read back directly.
- Unchanged root-accepted baseline128/exact-main CI is reused as authorized;
  no redundant full baseline and no passing full-candidate claim while intended
  RED remains. No store/CAS/migration/provider readiness acceptance from 2 tests.
- Initial freeze before root exact schema review: no runtime, existing-file, mapping,
  migration, DI, configuration, workflow, adapter or controller change; no commit.

## Typed seam RED checkpoint (supersedes initial two-file freeze)

Root read the full schema plan and approved typed seams and eventual exact EF
mapping/additive forward-only migration/snapshot, with stricter phase checks.
Those checks are tightened above. Mapping/migration remain unedited pending the
next diff checkpoint; no new migration has been executed.

Only new owned files added beyond initial test/doc:
- Application/Models/InvoiceNotificationCorrelationModels.cs: immutable identity/
  origin/snapshot and fixed internal exceptions.
- Application/Interfaces/IInvoiceNotificationCorrelationStore.cs: Admit/Read and
  retained-keyring seam; later fence APIs follow reviewed explicit transitions.
- Application/Services/InvoiceNotificationCorrelationBinding.cs: inert Frame/Compute.
- Data/InvoiceNotificationCorrelationStore.cs: explicit dependencies, inert fail-closed
  Admit/Read. Neither query nor write implemented; no production callers/registration.
- Tests/InvoiceNotificationCorrelationBindingTests.cs: literal697-byte frame,
  independent HMAC under en-US/th-TH/ar-SA, malformed UTF-16 control.
- Initial new foundation test expanded with typed admission expected Prepared/version1
  after actual registered acknowledged invoice creation + Pending origin, deliberately
  RED against unimplemented store. Downstream CAS faults still UNEXECUTED.

Fresh identical private Release terminal **0W/0E**. Filter
`FullyQualifiedName~InvoiceNotificationCorrelation`, results directory
`TestResults/correlation-typed-seam-red`, terminal **8 total: 7 intended RED,
1 control GREEN, 0 errors/skips**. Breakdown: 5 codec assertions against empty
seam, 1 real missing physical authority, 1 typed store unimplemented assertion.
TRX `natth_MALIEV-31USFIV_2026-10-01_17_39_19_net10.0.trx`.
This is a test-first checkpoint, not an implementation/full-suite acceptance.
Initial 2-test RED artifact remains untouched. Existing128/held37 unchanged.
Scoped `dotnet format ... --verify-no-changes --no-restore --include` over the
six new C# files terminal exit0; scoped unmodified gitleaks dir scan over all seven
new files terminal exit0/no findings/no suppressions; `git diff --check` exit0.
All handles terminal. Frozen early checkpoint before store implementation; no
original tracked file changed and no migration/DDL executed beyond accepted
migrations in disposable PG18. No passing full-candidate claim or commit.

## Historical codec GREEN / schema DIFF checkpoint (supersedes inert codec phase)

Root independently parsed the literal frame (697 bytes) and computed the exact
HMAC using standard .NET HMACSHA256 over that literal, not the codec helper.
Strict local codec now implements that frame with invariant positive integer /
lower-D UUIDs, required bounded UTF-8 fields (issuer512 bytes, employee256,
service128, keyID64), fixed purpose/sender/versions, ASCII keyID, lowercase SHA64,
distinct nonzero UUIDs, no NUL/replacement encoding, and a required exactly32-byte
owner-managed HMAC key. Issuers are exact bounded strings, not URI-normalized;
configured issuer trust remains caller/authority41's concern, not this codec.
No key configuration or provisioning was added. Frame/output are separately
owned arrays; retained snapshot clones on construction and every byte access.

Fresh private Release **0W/0E**, codec focus **44/44 PASS, 0 skipped**, TRX
`TestResults/correlation-codec-green/natth_MALIEV-31USFIV_2026-10-01_17_48_24_net10.0.trx`.
Additional controls: independently derived Thai710-byte frame/HMAC
`D9FDFD4BDDAA85AFC296020B1B4243F48338E9BF38633C483158E4AD4FCDE449`,
every variable immutable field mutation, every fixed policy field refusal,
missing/wrong-length key and noncanonical digest, malformed identities/UTF-16,
all stated UTF8 bounds, three cultures and defensive byte ownership. Thai actor
is a codec fixture, not a legitimate grant/identity provisioning claim.

Constructed schema files (NOT EXECUTED): Data/InvoiceNotificationCorrelationRow.cs,
AccountingDbContexts.cs +59 new lines only, snapshot +106 new entity lines only,
`20261001105037_AddInvoiceNotificationCorrelation.cs` and .Designer.cs. Generated
using the existing design factory and synthetic unreachable loopback:9 design-only
configuration: no DB created/contacted intentionally. Installed dotnet-ef10.0.5
reports older tool than runtime10.0.12; generator exit0, target model10.0.12.
This tool diagnostic is not hidden as a build warning waiver. Postgeneration
Release also terminal **0W/0E**. Migration Up is exact public21column table,
ordinary PK, nonpartial unique CONSTRAINT InvoiceID/Purpose, two tightened
validated checks; no FK, identity/default generation, seeds or legacy alterations.
Generated Down was corrected BEFORE execution to throw NotSupportedException
without DropTable. EnsureSchema(public) is generator metadata, not startup DDL.

Root review required before any new migration execution, including disposable
tests. Store still inert; no readiness/CAS/transaction fault implementation or
acceptance. Existing128 and earlier RED artifacts preserved. Next test wave after
schema review: actual migration Up/rerun/model/exactphysical shape/refused Down
retains rows/history; typed store replay/collision across UUID/workflow/actor;
retained key and missing lineage; one-shot fences/terminal receipt ordering;
actual configured strategy cancellation, pre/postCOMMIT/rollback/teardown faults.
Those downstream scenarios are NOT executed RED at this checkpoint.

## Historical admission/read implementation proposal — subsequently approved and implemented

Root approved exact schema diff and disposable-only execution. The following is
the concrete next store boundary, still a proposal (current store remains inert).

`AdmitAsync(identity, transientDigest, ct)`:
1. Check actual caller cancellation; validate immutable codec input without
   logging/framing payload. Obtain exact typed options from the original registered
   InvoiceDbContext. No reconstructed connection string/new DI factory/retry disable.
2. Run one guarded configured-strategy delegate with a fresh owned context and
   ReadCommitted transaction. A second delegate invocation is refused BEFORE
   creating a context or any command. Keep original scoped/advisory context alive.
3. Acquire ROW EXCLUSIVE on the explicitly qualified correlation table (no schema
   or table creation). Missing table -> fixed unavailable. Check uncached exact
   physical authority under the lock before any row mutation; prevents concurrent
   conflicting table/trigger/rule DDL. No schema-readiness cache or latest-name fallback.
4. SELECT existing intent or occupied invoice-purpose row FOR UPDATE, explicitly
   public-qualified. If existing intent matches, reconstruct/validate every stored
   immutable field, resolve RETAINED BindingKeyID first, copy key bytes, compute
   HMAC using transient caller digest, fixed-time compare. Exact replay returns
   retained snapshot after transaction/context disposal, never an execution grant.
   Wrong tuple/body or occupied purpose from another UUID/workflow -> fixed conflict;
   no returned winner, active-key rehash, phase change or RPC.
5. New row only: require existing public.Invoice ID and matching public.
   InvoiceCreationAdmission OperationID/QuotationID/EmployeeSubject/ServiceSubject,
   Pending with null ResultJson. Hold those rows FOR SHARE until commit, so deletion
   or origin state/tuple mutation cannot race admission. OriginIssuer is supplied
   only by future validated delegation; original admission does not persist it and
   store cannot invent/prove issuer or fresh creation from the database. Fresh
   acknowledged caller branch remains a separate mandatory integration gate.
6. Resolve/copy active32-byte key; create Prepared/version1 with server UTC times,
   no remote or issued timestamps; exactly one SaveChanges/INSERT and one COMMIT.
   Database unique violation -> fixed conflict only after verified rollback; no
   immediate second insert, retry, Number lookup or winner-row disclosure.
7. Catch failures INSIDE strategy and return failure only outside it. Before
   submitted COMMIT, verified rollback can preserve original cause/caller OCE;
   typed validation conflict remains fixed. Missing physical authority/key or
   malformed retained row is fixed unavailable. Set commitSubmitted BEFORE
   CommitAsync: transient/arbitrary/OCE after that point, uncertain rollback, or
   transaction/context disposal failure => fixed unavailable, causes retained
   internally. Rollback uses independent bounded command-budget token (registered
  120s), not canceled caller token. BOTH async disposals acknowledged before usable
   snapshot. No automatic fresh-context verification grants a lost fence permit.

`ReadAsync(intent, ct)` uses the same exact options/one-attempt owned context,
transaction/physical lock/readiness and disposal guard, but no INSERT/UPDATE.
It returns a defensive validated snapshot only after confirmed cleanup; retained
key must exist/32 bytes, even though payload-free read cannot recompute its HMAC.
Missing intent on verified ready authority returns null: this is NOT no-send
proof. Missing/drifted authority returns unavailable. Historical invoice deletion
does not erase retained row/readback, nor require recreating/revalidating a fresh
invoice eligibility branch. Neither Read nor exact Admit replay authorizes send.

Physical admission is exact: public namespace + ordinary/nonpartition relation;
all21 nondropped user columns, ordinals, types/varchar capacities/nullability,
no generated/identity/default columns; exact PK and nonpartial unique constraint
columns; two exact canonical VALIDATED check expressions (canonical pg_get_expr
goldens obtained from reviewed disposable Up, not substring acceptance); no extra
unique/FK/check, user rewrite rule/trigger/RLS authority. PG18 NOT NULL constraints
are checked through exact required attributes, not mistaken for additional custom
check constraints. Current migration history and runtime model/snapshot agree;
no EnsureCreated/default schema/search_path fallback. New admission requires an active key;
retained read/replay resolves only the retained key, independent of active-key availability;
every accessed retained row's key checked independently. No persistent DDL.

Next executable store-test wave is separated by reach:
- Initial typed Admit, typed Read present/missing, retained-key replay, changed
  payload, two-context invoice-purpose competition and actual caller cancellation
  are direct compiled contract RED against the inert seam. They do not yet reach
  the transaction, uniqueness query, key-resolution or cancellation implementation.
- Add typed origin mismatch/missing Pending admission, missing invoice, old-key
  absence and physical drift controls before behavior. An inert always-unavailable
  guard can make unavailable controls vacuously GREEN: no readiness claim until
  successful ready-shape control + actual implementation prove both branches.
- Before implementing transaction helper add controlled EF interceptors to EXACT
  registered options for transient pre/post-COMMIT, rollback failure, caller cancel
  after submitted COMMIT, transaction/context-disposal faults. Assert fault REACHED,
  one attempt/one COMMIT, durable0/1 rows, retained exact causes, no usable snapshot.
  Missing FaultReached at inert seam is an UNEXECUTED downstream scenario, not
  genuine ambiguity proof. Callback instrumentation is not real network-loss proof.
- Only later approved explicit fence/validated-receipt APIs may mutate phase;
  generic SetState/reset/retry remains absent. No workflow/controller/DI/provider
  dependency is introduced by this foundation review.

## Historical terminal schema / typed contract checkpoint — 2026-10-01 18:05

Root reviewed all21 row fields, mapping59lines, migration Up/forward-only Down and
snapshot106line-only addition, then approved disposablePG18 only. Fresh private
Release terminal **0W/0E**. Expanded correlation filter terminal **62 total:
55 GREEN, 7 intended typed-store RED, 0 errors/skips**:
`TestResults/correlation-typed-contract-red/natth_MALIEV-31USFIV_2026-10-01_18_04_02_net10.0.trx`.
Earlier schema run58=55GREEN/3RED TRX18:00 is retained at
`TestResults/correlation-schema-and-store-red/natth_MALIEV-31USFIV_2026-10-01_17_58_32_net10.0.trx`.

55GREEN =44codec plus11 real schema/current-contract controls: actual discovered
Up/public relation, exact21 physical column types/capacities/nullable/plain markers,
validated PK/unique/two checks, ordinarytable, actual rerun/model agreement,
refused Down retains row/history, existing invoice delete retains scalar authority,
cross-context UUID/workflow uniqueconstraint winner preserved, six phase-refusal
cases and the existing per-workflow admission characterization. No source data
or provider readiness is proved by those synthetic rows.

7RED against inert store =fresh acknowledged admission, read present/missing,
retained-key rotation replay, changed-payload conflict, two-context logical
invoice-purpose winner and actual caller cancellation. The key replay fixture
seeds a reviewed complete retained row with actual codec HMAC: it is storage
fixture evidence, not a fresh-caller eligibility/grant proof. Neither existing
invoice/admission must be recreated for read-only retained replay. The two-context
test is defensive internal competition, not proof both workflows legitimately
fresh-created the same invoice. No CAS/fault/helper execution claimed reached.

Store runtime still unimplemented; next gap controls/transaction instrumentation
and precise proposal above precede its behavior gate. Existing128/held37 proofs
and all initial RED artifacts preserved. No workflow/controller/adapter/DI/public
status/activation, provider send, persistent DDL, commit or push.

## Implemented Admit/Read checkpoint — 2026-10-01 18:22

Fresh private Release 0 warnings/0 errors; correlation focus **80/80 PASS, zero
skips**, `TestResults/correlation-store-first-green/natth_MALIEV-31USFIV_2026-10-01_18_22_21_net10.0.trx`.
All seven previously masked fault scenarios now actually reached their configured
save/COMMIT/disposal observers; they assert one attempt, zero/one durable row,
zero/one COMMIT and retained original causes. Transaction/context disposal faults
are EF callback instrumentation, not proof of actual network acknowledgment loss.
Actual PostgreSQL executes the composed FOR UPDATE SingleOrDefault/Any and FOR
SHARE queries, logical invoice-purpose competition and retained read with missing
active key. The original context remains owned by its scope; no provider effects.

Before this run, the expanded inert-store filter was 80=59 PASS/21 FAIL:
14 typed assertion REDs, seven downstream fault scenarios unreached, and four
always-unavailable guard PASS controls not yet independent readiness proof.
That artifact is retained under `TestResults/correlation-gated-guard-fault-red`.

## Physical collation review — 2026-10-01 18:30

Real reviewed Up metadata: PK uses `pg_catalog.uuid_ops` with no collation;
invoice-purpose unique uses `pg_catalog.int4_ops,pg_catalog.text_ops`, with
`none,pg_catalog.default` collations. The first five-case run was 2 PASS/3 FAIL:
two **genuine REDs** wrongly accepted nondeterministic ICU collations on Purpose
and OriginEmployeeSubject; one **fixture diagnostic**, SQLSTATE42809, because
PostgreSQL refuses text_pattern_ops UNIQUE USING INDEX as nondefault sorting.
`TestResults/correlation-collation-opclass-red/natth_MALIEV-31USFIV_2026-10-01_18_30_02_net10.0.trx`
is retained unchanged. The corrected latter test explicitly asserts PostgreSQL's
refusal and store refusal of the remaining standalone index, not a fictional
accepted constraint drift.

The guard now checks deterministic pg_catalog.default column collation (or no
collation for noncollatable types) and exact ordered default B-tree operator
classes/index collations from reviewed Up. A separate reached corrupt-row control
drops the state check, writes invalid phase/version/remote/timestamps and restores
only a weak NOT VALID check: Read refuses and leaves those bytes unchanged.
Snapshot does not duplicate weaker phase rules: exact validated canonical checks
are required under the table lock before reading, and enforce all row phase/time
relations. This is drift refusal, not repair or authorization to mutate phases.

Final runtime still has only Admit/Read; monotonic execution fences, workflow,
immutable payload recovery, fresh actor/resource authorization and consumer
cutover remain outside this foundation. Existing128 and held37 evidence retained;
no whole source owner, provider, deployment or parent37 completion claim.

## Reviewed legacy inventory adaptation — 2026-10-01 18:42

Unfiltered diagnostic **213=210 PASS/3 FAIL/0 skips** at
`TestResults/correlation-final-full/natth_MALIEV-31USFIV_2026-10-01_18_37_54_net10.0.trx`.
The three failures were exclusively old expected invoice entity/table count4,
now5 after the individually reviewed additive authority. Root read and approved
only these exact assertion adaptations:

- `AccountingContractTests.EfModels_KeepThreeIndependentLegacyDatabaseBoundaries`:
  count5 plus explicit correlation public table/no FK; payment and receipt cannot
  contain the entity. Existing payment6/receipt3 and cross-domain null checks stay.
- `AccountingPostgresMigrationTests.InitialMigrations_CreateThreeIsolatedLegacyDatabasesAndComputedValues`:
  count5 plus actual ordinary public relation. Every existing financial subtotal,
  withholding/summary/delta/date assertion and other database counts stay.
- `InvoiceCreationStore_AtomicallyCreatesItemsAndReusesExistingFileLinkWithoutSchemaChange`:
  count5 plus ordinary public relation, and table count captured immediately AFTER
  migration must stay equal after Create/link/replay. Exact invoice ID/single
  item/single file assertions remain. Creation still does not alter schema.

Fresh private Release0W0E; focused **89/89 PASS, zero skips** (new86 plus these3)
at `TestResults/correlation-final-inventory-focus/natth_MALIEV-31USFIV_2026-10-01_18_42_08_net10.0.trx`.
The new86 include literal reviewed index metadata and retained replay with missing
new active key. This adaptation does not waive a regression or broaden rollout;
the failed diagnostic and all earlier RED artifacts remain intact.

## Final foundation handoff — 2026-10-01 18:48

All processes terminal. Base remains `086732f926d5d78144bbcadaafcc0371c0559975`;
15 owned files: three Application types/codec/interface, three Data row/store/
readiness files, existing DbContext mapping and snapshot, additive migration/
designer, two new tests, two precisely adapted old inventory tests and this doc.
Nothing staged/committed. No shared outputs or original source changes.

Final unfiltered **214/214 PASS, zero skips** =original128 +new86, at
`TestResults/correlation-final-full-green/natth_MALIEV-31USFIV_2026-10-01_18_44_42_net10.0.trx`.
Unexcluded coverage:
`TestResults/correlation-final-full-green/739b12ec-04c4-4bc9-b46d-a61c552147c2/coverage.cobertura.xml`,
SHA256 `0263C7C83E34592C083E279525A8E8FB4E10659757266C42D07FF9EAFE9AEAFD`.
API209/615=33.98%; Application406/476=85.29%; Data5430/5669=95.78%;
Domain149/174=85.63%. Data includes generated migrations/snapshots; this is raw
assembly coverage, not equivalent handwritten runtime coverage. Dependency
assemblies remain in collection: Contracts0/57; Defaults1055/4441. No exclusion,
denominator change, quality waiver or API80 compliance claim (Accounting39 open).
Separately inspected handwritten classes: codec54/54 lines, store93/93 and
readiness13/13; these are executed-line counts, not all-branch/fault/provider proof.

Every command uses owned absolute `MalievWorkspaceRoot` plus
`UseLocalMalievDependencies=true` as above. Exact final commands:

```powershell
dotnet build Legacy.Maliev.AccountingService.slnx -c Release --no-restore --nologo
dotnet test Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~InvoiceNotificationCorrelation|FullyQualifiedName~EfModels_KeepThreeIndependentLegacyDatabaseBoundaries|FullyQualifiedName~InitialMigrations_CreateThreeIsolatedLegacyDatabasesAndComputedValues|FullyQualifiedName~InvoiceCreationStore_AtomicallyCreatesItemsAndReusesExistingFileLinkWithoutSchemaChange' --logger trx --results-directory TestResults/correlation-final-inventory-focus
dotnet test Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -c Release --no-build --no-restore --collect 'XPlat Code Coverage' --logger trx --results-directory TestResults/correlation-final-full-green
dotnet format Legacy.Maliev.AccountingService.slnx --verify-no-changes --no-restore
```

All five service projects (Api/Application/Data/Domain/Tests):
`dotnet list <project.csproj> package --vulnerable --include-transitive --no-restore`
exit0/no vulnerable packages. Scoped whole-file secret scans:
`C:/Users/natth/go/bin/gitleaks.exe dir <each of all15 owned files> --no-banner --redact`
exit0/no findings, no new suppression markers. `git diff --check` exit0. Tools:
`C:/Program Files/dotnet/dotnet.exe`; detached private Defaults8f4/Contracts78e
remain exact/clean. Only whitespace warning is existing snapshot CRLF→LF Git
normalization; diff contains only the reviewed additive entity block.

TDD, migration-audit and verification-before-completion checklists guided the
literal RED/GREEN chronology, additive disposable schema gates and fresh final
evidence. No PR/commit, provider email, activation, persistent DDL, grant, data
parity, CAS execution-fence or complete Accounting37/23/source-owner claim.

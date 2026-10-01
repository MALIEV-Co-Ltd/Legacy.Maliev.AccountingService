# Accounting37: invoice notification phase fences / receipt continuity

## Current stage and ownership

Bounded Accounting44 typed receipt codec / additive physical schema stage of
parent37: https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.AccountingService/issues/44.
Exclusive worktree `accounting-notification-phase-fences-20261001`,
branch `codex/accounting-notification-phase-fences-20261001`, base
`1c3a4437fb52a24d9093dc2d34572fe85683bd37` (foundation43).
Exact-main CI36859963392 independently observed completed SUCCESS at this exact SHA.
Initial PG18 filter5=4 missing-field RED/1 read GREEN and typed codec24=23 assertion
RED/1 defensive-copy GREEN are preserved. Strict typed codec now exists without a
production caller; four nullable fields/two reviewed receipt checks and exact
physical readiness guard are implemented. No phase-store methods, permits,
DI/workflow/controller/provider activation or authentication proof is added.
Final gate results are recorded below; earlier test-only prose is historical.
Final owned Release0W0E, expanded97PASS, unfiltered303PASS/0skip, whole format,
five package audits, unsuppressed scoped gitleaks and whitespace checks are terminal.
This is typed/schema acceptance evidence only, not phase-store/feature readiness.

Goal: extend the unregistered foundation with one acknowledged admission permit,
one acknowledged execution permit and durable monotonic receipt observation.
It does not wire consumers, grant authorization, recover/regenerate payloads or send.
Parent37 remains OPEN; Accounting41 trusted authority is not bypassed.
Root reviewed the complete design/new test and accepted the selected shape/API;
Root approved this typed/schema stage after actual RED; phase-store behavior stays
held pending independent inspection. Parent37 and authority41 remain open.

## Accepted boundaries and source ownership

Notification `cb51aaee4a01519c82d13330a738e7cc8f698675` remains default-OFF.
Producer paths: Api/Models/DeliveryIntentModels.cs:27; Api/Controllers/
DeliveryIntentsController.cs:22,42,68,81,91; Data/PostgresDeliveryIntentStore.cs:45.
The exact PUT/POST/GET routes are `/notifications/v2/delivery-intents/{intentId}`,
`/{intentId}/execute`, and `/{intentId}` respectively. Canonical lowercase D UUIDs.
PUT returns200; POST returns200 only for acceptance, otherwise202; GET returns200.
404/403/503, timeout or response loss is not no-send proof.

Wire receipt has exactly IntentId, Purpose, ResourceType, ResourceId,
WorkflowOperationId, State, Version, AdmittedAt, UpdatedAt, ProviderMessageId.
It has NO issuer, actor, channel, payload digest, producer binding/key or signature.
Expected actor/issuer and endpoint provenance must come from future validated
service JWT/HTTPS named transport plus exact request context, not receipt claims.
Producer checks unique exact configured iss/aud, identity_kind=service and
sub=service:legacy-accounting; fresh live capabilities admit/execute/read target
`legacy-notification/invoice/<invariant positive ID>`. No local claim rewriting,
grant bypass or assumption that Accounting41 is solved. Store-level typed receipt
validation proves syntax/continuity only, never authentication or an IAM grant.
No wire parser/consumer exists in this stage. A typed DateTimeOffset cannot attest
whether raw JSON originally supplied an explicit offset; the future adapter must
validate that wire requirement before constructing an observation. Current codec
validates typed UTC instants/microsecond precision, not JSON parser provenance.

Foundation codec ONLY bounds strict UTF8/nonblank/NUL-free strings, IDs and fixed
purpose/sender/version text. OriginIssuer and SenderIssuer are caller-supplied;
NO URI syntax, configured issuer allowlist or verified-origin check exists here.
The reviewed foundation-doc correction now states this limitation explicitly.

Historical source mirror cutoff `bed10c7d15e0698e0b75f1329d0f312937f5d77f`:
`5fac706a7983a6d359b39acbd670e6800afe020e` root original invoice/create/email;
`c821605b7ecde5f79d01966888b116defb10650d` parent
`92e8ad50c5d33f7458e86bf9cd33eabbdb1eb220` Intranet invoice Create/resilience
and Quotation Accept; `487d06df2f4614c53bead4b19d53d2613a46fd64` parent
`11c1c03995a6aa12708781dc2e7e3b760c040105` invoice paid-outcome/Web/SQL cohort.
This new safety contract does not claim a historical source defect or complete
those SHAs. Intranet202 interim reload fence, paid/Web/financial/data migration,
SQL deployment retirement, source data, activation/provider gates stay separate.

## Storage choices and selected shape

RemoteVersion alone cannot retain equal-version receipt equality, immutable remote
admission time or distinguish remote submitting from outcomeUnknown across restart.
Do not reconstruct those facts from local phase or assumed version arithmetic.
Selected: four additive nullable fields, not raw JSON/provider ID or a new outbox.
Alternative raw receipt retention unnecessarily persists opaque provider identifiers;
alternative transient validation loses restart continuity. Existing payload HMAC is
unchanged and no new keyring/resource is proposed.

```sql
ALTER TABLE public."InvoiceNotificationCorrelation"
  ADD COLUMN "RemoteState" varchar(32) NULL,
  ADD COLUMN "RemoteAdmittedAt" timestamptz NULL,
  ADD COLUMN "RemoteUpdatedAt" timestamptz NULL,
  ADD COLUMN "RemoteReceiptBinding" bytea NULL;
```

No defaults/generated values/backfill, no FK, no second table. Reviewed next
forward-only migration must be separate from foundation migration. Down throws
before ANY SQL, retaining rows/history. Only fresh disposable PG18 application
after explicit schema review; no persistent DDL or startup migration.

Required validated new continuity check (with existing identity/state checks kept):
- RemoteVersion NULL iff ALL four new fields NULL; when nonnull all four required,
  binding exactly32 bytes, remote version positive, remoteUpdated>=remoteAdmitted.
- Prepared/AdmissionIssued require RemoteVersion NULL. Admitted/ExecutionIssued
  require remoteState='admitted'. OutcomeUnknown requires remoteState IN
  ('submitting','outcomeUnknown'). ProviderAccepted requires 'providerAccepted'.
- AdmissionIssued has admission timestamp/no execution timestamp. Admitted has
  admission/no execution. ExecutionIssued/OutcomeUnknown/ProviderAccepted require
  retained admission AND execution timestamps. No arbitrary phase writes.
- RejectedBeforeSubmission currently has no producer transition; refuse remote
  storage/import. Existing local enum remains unchanged. Unsupported old rows
  stop migration atomically rather than fabricate receipt lineage or a retry grant.
- Local timestamps keep existing order; remote times are NOT compared with local
  clocks. Existing terminal rows with missing four-field lineage cannot be silently
  backfilled. If encountered, additive migration validates/refuses rather than
  fabricating authority; preflight is a separate rollout gate.

Current producer timestamp precision is concrete, not a hypothetical defect:
Admit INSERT then LockedAsync and Transition UPDATE then LockedAsync reread
PostgreSQL before returning receipts. Read also returns persisted rows. Require
received normalized UTC ticks divisible by10 (PostgreSQL microseconds); reject
hostile sub-microsecond input before framing/persistence, never silently round it.
Equal PUT/GET timestamps must frame identically. Equivalent explicit UTC offsets
normalize to the same instant; textual fractional-zero formatting is irrelevant.
Add literal tests for exactµ/equivalent offsets/1-tick residual rejection before
codec/schema behavior. No extra residual columns are needed for this producer.

Receipt bounds derive the producer: exact36-character canonical UUIDs, invariant
positive Int32 resource ID, fixed invoice-issued/invoice strings and named states.
ProviderMessageId is nonblank .NET Length<=256 for accepted and NULL for other
states (store TryAcceptAsync and validated producer state constraint). Preserve
strict valid UTF8/NUL refusal; do not substitute a guessed byte256 limit for source
character256. No raw identifier logs/columns. Sender issuer is bounded by existing
local codec512 UTF8 bytes AND must eventually match actual producer's configured
issuer/identity (.NET Length<=256), not a self-attested URI parsed by this codec.

Exact proposed additional checks (existing stricter local timestamp/lineage check
remains required; these are not a replacement):
```sql
CHECK (
 ("RemoteVersion" IS NULL AND "RemoteState" IS NULL AND
  "RemoteAdmittedAt" IS NULL AND "RemoteUpdatedAt" IS NULL AND "RemoteReceiptBinding" IS NULL)
 OR
 ("RemoteVersion" IS NOT NULL AND "RemoteVersion">0 AND "RemoteState" IS NOT NULL AND
  "RemoteAdmittedAt" IS NOT NULL AND "RemoteUpdatedAt" IS NOT NULL AND
  "RemoteReceiptBinding" IS NOT NULL AND octet_length("RemoteReceiptBinding")=32 AND
  "RemoteUpdatedAt">="RemoteAdmittedAt" AND
  (("RemoteState"='admitted' AND "RemoteVersion"=1) OR
   ("RemoteState"='submitting' AND "RemoteVersion"=2) OR
   ("RemoteState" IN ('outcomeUnknown','providerAccepted') AND "RemoteVersion"=3)))
)
CHECK (
 ("Phase" IN ('Prepared','AdmissionIssued') AND "RemoteVersion" IS NULL)
 OR ("Phase" IN ('Admitted','ExecutionIssued') AND "RemoteVersion" IS NOT NULL AND "RemoteState"='admitted')
 OR ("Phase"='OutcomeUnknown' AND "RemoteVersion" IS NOT NULL AND "RemoteState" IN ('submitting','outcomeUnknown'))
 OR ("Phase"='ProviderAccepted' AND "RemoteVersion" IS NOT NULL AND "RemoteState"='providerAccepted')
)
```
No unsupported rejected receipt pairing is admitted. NULLs are explicitly guarded
so partial quartets cannot pass SQL CHECK's unknown result. Root reviewed the
source-derived version tightening after actual PG accepted-version4/admitted2/
rejected receipt assertion REDs.

Readiness becomes exact25 columns with unchanged PK/invoice-purpose unique scope,
default deterministic collation/opclasses, validated canonical checks, public
ordinary relation/no RLS/rules/triggers/extra indexes and discovered model/history.
Use uncached ROW EXCLUSIVE lock before guard/read/update. Drift/missing migration
means unavailable, not repair or permission to use the old shape.

## Exact internal APIs / permit ownership

Proposed extension of IInvoiceNotificationCorrelationStore:
```csharp
Task<InvoiceNotificationAdmissionPermit?> IssueAdmissionAsync(
    InvoiceNotificationCorrelationIdentity identity, string payloadDigest,
    long expectedLocalVersion, CancellationToken token);
Task<InvoiceNotificationExecutionPermit?> IssueExecutionAsync(
    InvoiceNotificationCorrelationIdentity identity, string payloadDigest,
    long expectedLocalVersion, CancellationToken token);
Task<InvoiceNotificationCorrelation> ObserveReceiptAsync(
    InvoiceNotificationCorrelationIdentity identity, string payloadDigest,
    long expectedLocalVersion, InvoiceNotificationReceiptObservation receipt,
    CancellationToken token);
```

Observation is validated against caller's bound request identity, not called
Authenticated/Trusted merely because a constructor accepts strings. No HTTP/DI
or authentication dependency in the store. Future adapter must validate actual
transport/actor provenance before invoking this internal boundary.

Permits are sealed, constructor internal, defensive binding copies, identity +
committed localVersion + opaque payload binding; TryConsume is single-use via
Interlocked and consumes even if later RPC fails. A narrowly named NEW Application/
Properties/AssemblyInfo.cs friend grant ONLY to the Data assembly is proposed to
allow store minting without public permit constructors/factories. No friend grant
to browser/controllers/tests, public DTO or arbitrary snapshot-to-permit method.
This is application type discipline, not process-security or grant proof.

IssueAdmission: exact retained identity/key/payload binding; FOR UPDATE, expected
Prepared/version; CAS increments version and sets AdmissionIssued/server time.
IssueExecution: same binding, expected Admitted/version, validated retained admitted
receipt; CAS sets ExecutionIssued/server time. Missing/stale/other phase returns
null without winner disclosure. long.MaxValue refuses, no overflow.
There is no renewal/expiry/reissue of either permit. Read/replay never mint one.

Reuse exact registered typed options/fresh context, preserve original scoped context,
ONE guarded strategy entry, no effects inside retry policy. COMMIT AND transaction
AND context disposal must acknowledge before minting/returning a permit. Check
caller cancellation after disposal too; cancellation at that boundary yields no
permit and retains the committed fence. BeforeCOMMIT verified rollback may
propagate original/caller OCE; submittedCOMMIT/rollback/disposal ambiguity is fixed
unavailable with internal causes only. Bound cleanup by existing command timeout.
No Number lookup/reconciliation, transient blind replay or tracked-ID retry.

## Receipt framing and monotonic observations

Current producer graph: admitted1 -> submitting2 -> providerAccepted3 OR
outcomeUnknown3. A GET may skip an observation (1->3); the consumer may NOT skip
local execution lineage. No current producer unknown->accepted transition exists.
Equal-version state/content changes conflict; lower versions cannot downgrade.

Canonical receipt HMAC revision `accounting-notification-receipt-hmac-v1`:
ASCII tag +00; present marker00 +UInt64BE UTF8-byte length +strictUTF8 content;
null marker01 +UInt64BE zero, no content. Tags in this exact order:
receipt-binding-version, producer-contract (`delivery-intents-v2`),
local-payload-binding (lowercase hex of retained local HMAC), binding-key-id,
sender-issuer, sender-service-subject, intent-id, invoice-id, purpose, resource-type,
workflow-operation-id, remote-state, remote-version, remote-admitted-utc-ticks,
remote-updated-utc-ticks, provider-message-id.
All UUIDs lowercase D; integers/ticks invariant decimal; timestamps normalize UTC
instant before framing. Wire timestamps without an explicit UTC/offset, omitted
properties, null or invalid values refuse; do not conflate omission with year1.
Retained local key only, exactly32 bytes; never active-key rehash. Missing old key
unavailable. Header bytes/identifier/text are transient and never logged/spooled.

Literal producer-wire fixture (not a real provider receipt):
```json
{"intentId":"10000000-0000-0000-0000-000000000001","purpose":"invoice-issued","resourceType":"invoice","resourceId":"42","workflowOperationId":"20000000-0000-0000-0000-000000000002","state":"providerAccepted","version":3,"admittedAt":"2026-10-01T00:00:00Z","updatedAt":"2026-10-01T00:00:01Z","providerMessageId":"fixture-acceptance"}
```
Literal null provider fragment: tag `provider-message-id` encoded ASCII,
then `00 01 0000000000000000`. Empty-present instead is
`00 00 0000000000000000` and must NOT match (also invalid for accepted).
Full independent byte/HMAC golden must be written in NEW tests before codec
repair; expected bytes cannot call production frame/Compute helpers.

Independent draft golden computed with .NET HMACSHA256 over manually framed
literal fields, no production helper: key bytes00..1f; local-payload-binding64
literal lowercase `a`; retained kid=fixture-1; sender issuer=https://auth.example.test;
sender subject=service:legacy-accounting; all wire fixture values above.
UTC ticks are639264096000000000 and639264096010000000 (separately checked against
UTC dates). Frame771 bytes; HMAC
`9982DC0BF49D41109E8F1B5BEE4DB1883E91368E0F1D21C8A93535D0A9DD42DB`.
An earlier draft mis-transcribed the UTC ticks; it was corrected before defining
this golden, not an observed product RED or a runtime adjustment. Literal FrameHex:
```text
726563656970742D62696E64696E672D76657273696F6E000000000000000000276163636F756E74696E672D6E6F74696669636174696F6E2D726563656970742D686D61632D763170726F64756365722D636F6E74726163740000000000000000001364656C69766572792D696E74656E74732D76326C6F63616C2D7061796C6F61642D62696E64696E67000000000000000000406161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616161616162696E64696E672D6B65792D696400000000000000000009666978747572652D3173656E6465722D6973737565720000000000000000001968747470733A2F2F617574682E6578616D706C652E7465737473656E6465722D736572766963652D7375626A65637400000000000000000019736572766963653A6C65676163792D6163636F756E74696E67696E74656E742D69640000000000000000002431303030303030302D303030302D303030302D303030302D303030303030303030303031696E766F6963652D6964000000000000000000023432707572706F73650000000000000000000E696E766F6963652D6973737565647265736F757263652D7479706500000000000000000007696E766F696365776F726B666C6F772D6F7065726174696F6E2D69640000000000000000002432303030303030302D303030302D303030302D303030302D30303030303030303030303272656D6F74652D73746174650000000000000000001070726F7669646572416363657074656472656D6F74652D76657273696F6E000000000000000000013372656D6F74652D61646D69747465642D7574632D7469636B730000000000000000001236333932363430393630303030303030303072656D6F74652D757064617465642D7574632D7469636B730000000000000000001236333932363430393630313030303030303070726F76696465722D6D6573736167652D696400000000000000000012666978747572652D616363657074616E6365
```

Observe only with matching complete identity/binding/localVersion. AdmissionIssued
plus admitted1 -> Admitted. ExecutionIssued plus submitting2/unknown3 ->
OutcomeUnknown; plus accepted3 -> ProviderAccepted. OutcomeUnknown with retained
submitting2 may observe accepted3 or unknown3. Retained unknown3 never imports
accepted3 or invented accepted4. Accepted3 is terminal. Any remote execution state
without local execution timestamp refuses without inventing it.

Persist full four-field receipt evidence and localVersion+1 atomically. Same remote
version +exact keyed receipt is a read-only duplicate, not a new permit/version.
For higher remote version require exact immutable remoteAdmittedAt and monotonic
remoteUpdatedAt plus the current source graph. Malformed/equal-different/stale
receipt refuses without overwrite. PUT may replay an existing later state, but
that does not create missing local execution lineage. POST200 must mean accepted;
POST202 must not be relabeled accepted. Missing receipt/404/503 remains readback-only.

## Test-first waves / meaningful failure expectations

Initial NEW file uses discovered actual migration/registered retry-options/PG18:
four literal column contract cases should fail missing4 fields; existing missing
Read/zero-row control should pass. This is stronger-contract missing authority,
NOT a historical bug or executed CAS proof. No reflection/missing-symbol tests.
After design approval, root may permit compile shapes before typed CAS tests.
Current read-only cases share one class-owned container per testing skill. Later
drift/fault cases need separately isolated disposable databases and exact reached
observers, not shared mutable state. No IAM replacement/fake positive authority.

1. Schema: exact25 shape/validated checks/model/discovered Up rerun/forward-only
   Down retained rows/history; missing quartet, partial quartet, invalid phase/
   remote timestamps/version/binding length, nondeterministic collation and drift
   refuse. Existing prepared rows survive additive Up unchanged; no unsafe backfill.
2. Independent codec: complete literal frame/HMAC, Thai/culture/all fields,
   null/empty/order/UTF8/unpaired surrogate, equivalent UTC offsets/decimal integer
   canonicalization, exactµ/1-tick residual refusal, equal PUT/GET receipt framing,
   missing retained key; no production-derived expected values.
3. Actual two-context CAS: one admission permit, one execution permit, stale
   binding/version/phase/null/missing/overflow losers zero permits; local TryConsume
   one winner; Read/replay/remote admitted read cannot reconstruct execution permit.
4. Receipt: wrong entire tuple, missing execution lineage, every changed field at
   equalVersion, stale/downgrade, invalid state/version/time/provider ID, source
   legal skipped observations, unknown3->accepted3/4 refusals, accepted duplicate
   read-only, immutable remote admission time, oldkey retention/rotation.
5. Registered fault boundaries: transient first Save, caller cancel preCOMMIT,
   rollback failure, COMMIT before/after callback, postCOMMIT OCE/cancel, transaction
   disposal and context disposal/cancel. Assert FaultReached, one attempt/one Save/
   zero-or-one COMMIT, exact durable phase/version and NO returned permit. Callback
   instrumentation is component evidence, not actual physical wire-loss proof.
6. Fresh Release0W0E -> focus -> all214 old plus new -> unexcluded coverage,
   whole format/five audits/unsuppressed secrets/diff. Keep raw API39 quality OPEN.

## Historical proposed ownership / current behavior stop gate

Later reviewed edits: existing Application interface/models; NEW receipt codec and
permit models/AssemblyInfo; Data row/store/readiness; existing Invoice mapping;
NEW forward-only migration/designer and snapshot; NEW tests and this design.
Only precise foundation-doc trust correction is proposed. No old-test policy
change presumed; any intentional inventory update requires separate root review.

Root reviewed and authorized only typed codec/additive schema after actual RED.
Future phase-store APIs/fault behavior still require independent review.
Private exact Defaults8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3 / Contracts
78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7 outputs must be uniquely owned later.
No commit-ready or runtime/feature-green claim. No DI/consumer/IAM bypass/provider/network,
production data/persistent DDL/grant/activation/deployment/source/ledger writes.

## Initial terminal evidence — 2026-10-01 19:35

Root released private setup/build/initial5 after exact-main confirmation.
Own clean detached clones: Defaults8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3 /
Contracts78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7. Existing reviewed helper built
private Auth82c8d63dd08677a7f8ccd107c05dd6c9badbfd79 with own Defaults5c5f9479313710fa576f83d3b396442997a2fcf4 /
Contracts78e; no service/DB/provider started by preparation. No shared outputs.

Each command sets owned absolute MalievWorkspaceRoot to this worktree's
`.dependencies` and UseLocalMalievDependencies=true:
```powershell
dotnet restore Legacy.Maliev.AccountingService.slnx --nologo
dotnet build Legacy.Maliev.AccountingService.slnx -c Release --no-restore --nologo
dotnet test Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~InvoiceNotificationPhaseFenceContractTests' --logger trx --results-directory TestResults/phase-fence-initial-contract-red
dotnet format Legacy.Maliev.AccountingService.slnx --verify-no-changes --no-restore --include Legacy.Maliev.AccountingService.Tests/InvoiceNotificationPhaseFenceContractTests.cs
```
Restore/build terminal exit0; build0warnings/0errors including private Auth.
Initial TRX `TestResults/phase-fence-initial-contract-red/natth_MALIEV-31USFIV_2026-10-01_19_35_10_net10.0.trx`:
total/executed5, failed4, passed1, errors0, notExecuted0. Each failure is exact
expected nullable/plain column type versus missing actual null, not a setup error
or missing C# symbol. Real registered retry-enabled options/discovered migrations/
public PG relation executed. Existing Read missing intent leaves rows empty and
passes; it is not remote no-send proof. Shared disposable container was disposed.

Scoped format exit0. Literal JSON/frame readback parsed and independently HMACed
771 bytes to exact9982...42DB; this checks the artifact, not unimplemented codec.
Unsuppressed gitleaks on both NEW files exit0/no findings, git diff whitespace check
exit0. No existing tracked edits; no seams, migration/model changes, callbacks,
provider effects, authenticated HTTP/grant claims or commits. All handles terminal.
Unfiltered219/coverage/audits not run at this intentionally RED test-only checkpoint;
accepted exact-main214 remains baseline evidence, not a passing219 candidate.

## Typed/schema stage chronology — 2026-10-01

Inert compilation-only receipt Frame/Compute returned empty arrays and progression
returned Duplicate, with no caller. Fresh private Release0W0E then typed24 focus:
`TestResults/phase-fence-codec-red/natth_MALIEV-31USFIV_2026-10-01_19_52_33_net10.0.trx`
=23 genuine assertion failures/1 defensive-copy control, zero errors/skips.
Strict tagged UTF8/HMAC codec then fresh Release0W0E/expanded54PASS:
`TestResults/phase-fence-codec-green/natth_MALIEV-31USFIV_2026-10-01_20_36_34_net10.0.trx`.
Full771-byte independent frame literal is now asserted, not just length/digest.
Sender/intent/invoice/workflow receipt context plus retained local payload HMAC
are framed; origin/quotation are NOT independently re-framed here. Only future
full-identity/binding store validation can inherit those facts. No self-attested
issuer, supplied digest or typed observation authenticates the caller.

Generator diagnostics: API startup lacks EF Design, then Data factory required
design-time environment. Corrected Data-only generation uses an unreachable
synthetic localhost metadata connection, never a real DB. Installed tool10.0.5
reported older than runtime10.0.12; generated model annotation remains10.0.12.
New migration20261001133820 adds exactly4 nullable fields/2checks; reviewed Down
throws beforeSQL. No financial model, old migration, key scope or routes changed.

Initial reviewed generic receipt checks allowed accepted4/admitted2/unsupported
rejected. Actual disposablePG10 cases exposed3 assertionRED/7controlsGREEN:
`TestResults/phase-fence-strict-schema-red/natth_MALIEV-31USFIV_2026-10-01_20_42_07_net10.0.trx`.
Root confirmed strict source graph and atomic refusal of unsupported old lineage.
Fresh Release0W0E then schema20PASS:
`TestResults/phase-fence-schema-strict-fresh/natth_MALIEV-31USFIV_2026-10-01_20_45_42_net10.0.trx`.
Prepared/AdmissionIssued original21-column rows survive with null receipts;
unsupported accepted/rejected old rows preserve21columns/history/state after
failed transactionalUp, not backfill. Each mutable case owns an isolated DB in
the class disposablePG18 container. Metadata-only baseline uses actual registered
retry options; bare schema-save cases are physical constraint proof, not normal
HTTP/auth acceptance.

Compiler diagnostics for NEW raw-string fixture are not productRED. An accidental
`--no-build` run after that failed compilation used stale15-case binary:
`TestResults/phase-fence-schema-strict/...20_44_56...`; excluded from acceptance.
Corrected fresh Release preceded the20PASS above; no relaxation of assertions.

Readiness exact25 fields/6constraints keeps original identity/state canonical
hashes unchanged; new receipt07B7F85EFDCBC4A00B801D8B0D0F5F7F5EEA7405460AF48D1AD1BB675AE9416D,
phaseC6CD3AA2D4B86EE44187E1918AE3852DFDD4456C3F265800368FAA703FF37D72
derive actual discoveredUp PG expressions. Both retained migration histories,
default collation/opclasses/no unexpected relation effects remain required.

Combined181 diagnostic terminal173PASS/8FAIL/0skip:
`TestResults/phase-fence-combined-diagnostic/natth_MALIEV-31USFIV_2026-10-01_20_46_44_net10.0.trx`.
Eight failures were1intentional old21shape inventory,6State-only corruption cases
now refused first by new receipt checks,1old corruption seed blocked before Read.
Root explicitly approved ONLY4column/2constraint inventory additions, adversarial
drop of new checks while preserving exact old corrupt row/readback assertions,
and valid receipt quartet with original bad local timestamps in six State proofs.
Missing RemoteVersion/partial quartet rejection is separately preserved in NEW
tests; no old exception assertions, financial/caller behavior or timeouts weakened.

Fresh Release0W0E followed combined194PASS/0skip:
`TestResults/phase-fence-final-focus/natth_MALIEV-31USFIV_2026-10-01_20_53_10_net10.0.trx`.
Unfiltered302PASS/0skip with unchanged XPlat coverage:
`TestResults/phase-fence-final-full/natth_MALIEV-31USFIV_2026-10-01_20_57_11_net10.0.trx`.
Additional final Thai codec control derives783-byte frame by replacing only final
provider bytes/UInt64 length in the independent771-byte literal; exact30 UTF8 bytes
E0B983E0B89AE0B981E0B888E0B989E0B887E0B8ABE0B899E0B8B5E0B989,
key00..1f, independent HMAC94F1ED3FDB90E50D4C1B0430D15F0A3291F7D67CD7597DAB7EE92051E4768F13.
No production helper derives its expected values. This adds a control, not a new
historical defect claim; final fresh303 validation supersedes302 for freeze.

## Frozen Accounting44 candidate — 2026-10-01 21:07

HEAD/base remains1c3a4437fb52a24d9093dc2d34572fe85683bd37;13owned changed/new
files only. No commits/push, provider/IAM/auth wiring, schema application outside
unique disposablePG18, source/ledger/shared-worktree edits or activation.
All process handles terminal. Root independently reviews this candidate before
phase-store APIs; accepted replay and financial108 legacy controls remain green.

Final fresh private Release terminal0warnings/0errors. Each command uses
`MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/accounting-notification-phase-fences-20261001/.dependencies`
and `UseLocalMalievDependencies=true`; exact8f4/78e private clones remain unchanged.
```powershell
dotnet build Legacy.Maliev.AccountingService.slnx -c Release --no-restore --nologo
dotnet test Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~InvoiceNotificationReceiptBindingTests|FullyQualifiedName~InvoiceNotificationPhaseFenceContractTests|FullyQualifiedName~PhysicalPhaseChecks_RejectMissingOrInventedExecutionLineage|FullyQualifiedName~DiscoveredMigration_ExactPhysicalColumnsAndValidatedUniqueAuthority_ModelRerunAgree|FullyQualifiedName~TypedRead_InvalidStoredPhaseVersionTimeCannotBypassValidatedStateAuthority' --logger trx --results-directory TestResults/phase-fence-final-expanded-focus
dotnet test Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -c Release --no-build --no-restore --logger trx --results-directory TestResults/phase-fence-final-303 --collect:'XPlat Code Coverage'
dotnet format Legacy.Maliev.AccountingService.slnx --verify-no-changes --no-restore
```
Final focus `TestResults/phase-fence-final-expanded-focus/natth_MALIEV-31USFIV_2026-10-01_21_01_44_net10.0.trx`:
97PASS (67codec/22PG/8approvedoldfixture controls), errors0/skips0.
Full `TestResults/phase-fence-final-303/natth_MALIEV-31USFIV_2026-10-01_21_02_29_net10.0.trx`:
303PASS, errors0/skips0; covers214old +89new, no old suppression/timeout changes.
Coverage `TestResults/phase-fence-final-303/ae06aaf6-18eb-4255-94ae-9ebabb664fe5/coverage.cobertura.xml`:
raw/unexcluded API209/615=33.98%, Application501/572=87.59%, Data5962/6202=96.13%,
Domain149/174=85.63%. Data denominator includes generated migrations/snapshot;
not a hand-written-only coverage claim. API80quality/Accounting39 remains OPEN;
no denominator filters/waivers and no parent37/authority41 closure.

Whole format terminal exit0. Five exact Api/Application/Data/Domain/Tests project
audits `dotnet list <project.csproj> package --vulnerable --include-transitive`
terminal exit0/no vulnerable packages reported by current NuGet source.
`C:/Users/natth/go/bin/gitleaks.exe dir <each-owned-file> --no-banner --redact`
scans all13files unsuppressed exit0/no findings; independent fixture vectors are
synthetic, no allow marker or literal evasion. `git diff --check` exit0; only Git's
existing CRLF-to-LF normalization notice on generated snapshot, not build warning.
No actionlint needed: no workflow file changed. Source/provider/deployment/data
acceptance and future CAS fault/permit proofs were not run: deliberately excluded
from this codec/schema stage, not claimed passing.

## Independent root acceptance before PR

Root read the strict codec, additive migration, readiness/model diff, golden
vectors, physical constraint tests and precise existing fixture updates. An
independent PowerShell HMAC calculation over the literal Thai frame (no production
helper) confirmed 783 bytes and `94F1ED3FDB90E50D4C1B0430D15F0A3291F7D67CD7597DAB7EE92051E4768F13`.
Root Release build: zero warnings/errors; focus: 131 passed, zero failures/skips.
Root unfiltered suite: 303 passed, zero failures/skips, duration 3m17s;
`TestResults/phase44-root-full/root-full.trx` SHA256
`24EBBED7C8CB4FCD0EA508CB07B61CFA6408E2B05CE474766351E4D25A8636B0`.
Raw root coverage agrees with the frozen figures: API 33.98%, Application 87.58%,
Data 96.13%, Domain 85.63%; no quality-gate waiver. Root whole-format verification,
five transitive dependency audits, thirteen unsuppressed redacted file scans and
diff checks passed. All test databases were disposable. Protected exact-head and
post-merge CI remain required; parent37/authority41/quality39 remain open.

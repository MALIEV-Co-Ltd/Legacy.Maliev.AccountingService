# Invoice delegation chain acceptance evidence

## Scope and source classification

Accounting issue #23 bounded TEST/DOC/TOOLING slice, evaluated 2026-09-30. This
is an actual Auth HTTP exchange to a Production Accounting HTTP boundary with
real PostgreSQL admission storage. It is not actual Intranet controller/browser
execution, end-to-end invoice generation, or acceptance of the broader issue.
The broader issue remains open. Changes are restricted to the test project,
chain test and fixture, preparation script, and this document. No production
source, workflows, canonical checkout, infrastructure, or credentials changed.
No commit or push was authorized for this delegated handoff.

Source revisions inspected and clean dependency states reverified:

| Component | Exact revision | Classification |
| --- | --- | --- |
| Accounting base | `0ec928ee470e29777151e8028b3f300a93f5b538` | Actual API/controller/verifier/admission store; test-only working-tree additions |
| Accounting Defaults | `8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3` | Existing unchanged CI dependency |
| Auth | `82c8d63dd08677a7f8ccd107c05dd6c9badbfd79` | Clean pinned checkout, real API child and issuer |
| Auth Defaults | `5c5f9479313710fa576f83d3b396442997a2fcf4` | Clean separate pinned Auth runtime dependency |
| CompatibilityContracts | `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7` | Pinned dependency, no contract edits |
| Intranet | Not executed in this slice | Compatible wire reproduced by HTTP fixture only |

`gh run view 36428938901 --repo MALIEV-Co-Ltd/Legacy.Maliev.AccountingService
--json conclusion,headSha,status` freshly returned `success`, `completed`, and
the exact Accounting base above. This is baseline CI evidence, not a CI run
for the uncommitted additions.

## Trust boundaries exercised

The preparation target runs before the test project's `PrepareForBuild`, so
the existing unchanged CI build reaches it automatically. It checks out public
Git repositories under ignored `.dependencies/delegation-chain`, validates
exact HEAD, clean tracked/untracked status, and expected origin, and refuses
to reset or replace mismatched/dirty existing checkouts. Auth has its own
Defaults checkout/output rather than the Accounting Defaults dependency.
Subprocesses have five-minute preparation deadlines; the MSBuild wrapper has
an overall deadline. Ambient token/configuration environment variables are
not projected. Git prompts, helpers, injected configuration, global/system
configuration, and HTTP extra headers are disabled for public source access.
The Auth build uses Release, `--no-incremental`, and `-warnaserror`.

The fixture starts disposable PostgreSQL 18 and migrates the real Accounting
Invoice database. The actual Auth API binary runs in a separate child using
`Production`, fresh in-memory RSA material, disposable database connection
strings, and `http://127.0.0.1:0`. Startup discovery reads its structured
lifetime log, with a 45-second deadline. Auth HTTP requests time out after
15 seconds. A three-minute process watchdog and bounded teardown terminate
only the owned child and dispose the owned container. No persistent datasets,
GCS, Kubernetes, paid infrastructure, secrets, or deployment are used.

Ordinary fixture employee/service JWTs are signed test credentials, not
constructed principals or authentication-handler replacements. They enter
the actual Auth `POST /auth/v1/exchange/invoice-create` route. Its normal
JwtBearer validator, service policy, permission policy, employee validator,
and real delegation issuer execute. No Auth application reference or Auth
runtime service replacement is added to Accounting tests.

Accounting runs its real entry point with `WebApplicationFactory<Program>` in
`Production`. The test asserts issuer/audience/lifetime/signature validation,
RS256-only algorithms, and the absence of a signature-validator shortcut.
Its normal permission authorization handler, delegation verifier, and durable
`InvoiceCreationAdmissionStore` remain registered. Only external
`IIamServiceClient` transport and `IInvoiceCreationWorkflow` are replaced.
The IAM transport allows only the expected Intranet subject and exact create
permission; a forced authoritative denial is explicitly tested despite the
JWT create permission. The workflow replacement is a deterministic effect
counter/result, not invoice/document/file/notification generation.

## Exact wire asserted

- Service bearer subject `service:legacy-intranet`, identity kind `service`,
  permission `legacy-auth.invoice-delegation.issue` calls Auth.
- Auth JSON request uses `employeeAccessToken`, positive `quotationId`, and
  nonempty lowercase canonical D-format UUID `operationId`.
- Actual issuer JSON contains exactly `accessToken`, `tokenType: Bearer`, and
  `expiresIn: 120`.
- Actual delegation is RS256, single audience `legacy-accounting:invoice-create`,
  employee `sub`, Intranet `azp`, scope `legacy.accounting.create`, matching
  `quotation_id` and `operation_id`, nonempty canonical UUID `jti`, and numeric
  `iat`, `nbf`, `exp`, with equal issued/not-before dates and 120-second lifetime.
- Accounting receives its separate ordinary service bearer, canonical
  `Idempotency-Key`, and `X-Maliev-Employee-Delegation: Bearer <actual issuer JWT>`.
- The inspected verifier caps lifetime at 120 seconds with 30-second skew.
  Admission binds operation, quotation, employee, service, and editable-request
  fingerprint; renewed JTI is not a new operation or a changed intent.

## Executed cases

17 focused tests, zero skips:

- Actual issuer wire and Production Accounting success; renewed issuer JTI
  replays the stored result, with exactly one workflow effect and persisted
  employee/service/quotation/completed admission read back in another context.
- Authoritative IAM false returns 403 despite valid service JWT permissions;
  no effect and no admission occur.
- Mismatched quotation/operation, noncanonical operation header, wrong service
  actor, forged signature, wrong algorithm, future `nbf`, ambiguous `sub`,
  ambiguous scope, wrong audience, expired token, and lifetime over 120 seconds
  deny before effects or a new admission. The wrong service actor is denied
  by live authorization; other listed invalid delegations return 401.
- Real renewed Auth tokens with changed employee/quotation, or a changed
  editable body, conflict with an existing intent (409), retain the original
  durable row, and cause no repeated workflow effect.

Forged/ambiguous/time/algorithm negative JWTs are deliberately signed mutation
fixtures, not claimed to be issuer outputs. Success and durable actor/quotation
conflict tests consume actual HTTP issuer responses.

## Validation record

Run from the isolated Accounting worktree:

```powershell
dotnet build Legacy.Maliev.AccountingService.slnx -c Release --nologo -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/accounting-delegation-chain-20260930/.dependencies
dotnet test Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~InvoiceDelegationChainHttpTests --logger 'trx;LogFileName=delegation-chain-focused.trx'
dotnet test Legacy.Maliev.AccountingService.slnx -c Release --no-build --no-restore --logger 'trx;LogFileName=delegation-chain-full.trx'
dotnet test Legacy.Maliev.AccountingService.slnx -c Release --no-build --no-restore --collect 'XPlat Code Coverage' --logger 'trx;LogFileName=delegation-chain-coverage.trx'
```

Accounting Release and isolated Auth Release: zero warnings/errors. Focused:
17 passed, 0 failed/skipped. Full suite: 108 passed, 0 failed/skipped. Additional
coverage run: 108 passed, 0 failed/skipped. Initial red runs exposed test setup
issues (required Production CORS and suppressed structured lifetime logging),
not a reproduced production source defect; only fixture configuration changed.

Static checks executed:

```powershell
$env:UseLocalMalievDependencies='true'
$env:MalievWorkspaceRoot='B:/maliev-legacy/.worktrees/accounting-delegation-chain-20260930/.dependencies'
dotnet format Legacy.Maliev.AccountingService.slnx --verify-no-changes --no-restore --include Legacy.Maliev.AccountingService.Tests/InvoiceDelegationChainHttpTests.cs Legacy.Maliev.AccountingService.Tests/Fixtures/InvoiceDelegationChainFixture.cs
git diff --check
gitleaks dir Legacy.Maliev.AccountingService.Tests --no-banner --redact
gitleaks dir tooling --no-banner --redact
dotnet list Legacy.Maliev.AccountingService.slnx package --vulnerable --include-transitive
```

Formatting and diff checks passed. PowerShell AST parsing returned zero errors.
Both gitleaks directory scans found no leaks. NuGet vulnerability query reported
no vulnerable packages in any of the five Accounting projects against
`https://api.nuget.org/v3/index.json`.

## Explicit limitations and handoff

The MALIEV testing skill's blanket 80% line-coverage floor is **not satisfied**.
Cobertura measured aggregate 57.16% (5,949/10,407 lines), including dependency
assemblies: Accounting API 32.03%, Application 82.60%, Data 93.99%, Domain
85.63%, CompatibilityContracts 0%, ServiceDefaults 18.80%. This slice does not
claim whole-service coverage compliance or silently exclude low-covered
assemblies. Unrelated coverage expansion is outside its authorized boundary.
The report is in ignored test output
`Legacy.Maliev.AccountingService.Tests/TestResults/38b19597-6542-45e1-bc82-41279f81c60e/coverage.cobertura.xml`.

Linux/hosted execution of the new preparation hook is not verified by the old
baseline CI run; portability is source-inspected and local Windows pwsh/build
execution is verified. A clean hosted build/focused/full run is still required
before claiming new CI acceptance. Actual Intranet producer/browser behavior,
production credentials, real IAM transport, and real invoice side effects are
deliberately excluded. Issue #23 must remain open for that broader acceptance.

Independent parent acceptance on 2026-09-30: inspected all five files and the
normal production bearer, live-permission, delegation and admission boundaries;
Release build passed with zero warnings/errors, focused 17/17 and full 108/108
passed with no skips. Durable root TRX evidence is retained outside the repo in
`.artifacts/accounting-delegation-root-20260930`. Root solution formatting,
PowerShell parsing, whitespace and changed-source Gitleaks (38,657 bytes) passed.
This does not resolve the coverage residual, Intranet/browser chain, or broad
issue #23. Protected-head and post-merge main CI are still required.

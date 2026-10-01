# Accepted Auth producer session-chain acceptance — 2026-10-01

## Scope and current status

Final owned validation is terminal: Release0warnings/0errors, focused29PASS, unfiltered313PASS/0FAIL/0errors/0skip (existing303+new10), whole-solution format, five package audits, script AST parse, whitespace and unsuppressed scoped secret scan PASS. All outputs are released to root for independent review; no commit has been created.

Accounting base `1913979fa67e1d5864d1469ac1e9e768548af4ba`; this slice changes test infrastructure only. Producer Auth is accepted main `51afbbd6e2829382a3431338abedccf339de33b1`, exact-main CI `36889098627` SUCCESS. The old producer `82c8d63dd08677a7f8ccd107c05dd6c9badbfd79` and its private outputs are preserved. No Accounting runtime, authorization policy, public contract, financial fields, production resource or grants change. No commit/push or activation is authorized.

The original 17 chain assertions and adversarial mutations remain literally unchanged. Positive credentials now come from actual Auth `/auth/v1/login` and `/auth/v1/service/login`, followed by the actual `/auth/v1/exchange/invoice-create` route. Handmade signing remains only for existing adversarial fixtures. The narrow invoice-create delegation is not quotation employee-decision or invoice-completion authority.

## Four disposable owner databases

The fixture creates four distinct `accounting_chain_<random-run-UUID>_{invoice,customer,employee,state}` databases on its own PostgreSQL18 Testcontainer. Accounting owns invoice migrations. An ignored generated utility references only the exact pinned Auth Infrastructure project and invokes actual CustomerIdentity, EmployeeIdentity and RefreshSession EF `MigrateAsync`. No schema/history SQL is copied. It seeds two synthetic confirmed employee identities with random security/concurrency stamps and real PasswordHasher credentials. It does NOT seed sessions: normal employee login persists session/family/token-hash authority.

Before helper invocation, the fixture proves nonempty container identity, loopback binding and exact mapped port from the running container object. All four strings are constructed from that object, not ambient environment. The helper independently validates canonical run UUID; four exact distinct database names; same loopback host, mapped port, user/password as the container control connection; and disabled pooling. The helper accepts no connection arguments or persistent endpoint. Its explicit validation-only mode returns before creating a context or migrating; adversarial database/host/pooling/port controls exercise that guard.

Generated `Seed.csproj`/`Program.cs` are under owned ignored `.dependencies/delegation-chain/seed-51afbbd-v5`. Preparation checks resolved paths remain in its private boundary and refuses to overwrite any differing existing content. Earlier failed versions remain preserved. Generated source contains no credentials/row data; ephemeral inputs are process environment only. Child environment is sanitized; both pipes are drained without echoing values; failures are fixed messages. Migration cancellation is60seconds, child75seconds, kill/tree and bounded15second teardown. Producer process retains the existing bounded lifetime. Connections disable pooling; processes/contexts dispose before the container.

## Independent evidence and chronology

- Unchanged old-pin Release0warnings/0errors and existing chain17PASS/0FAIL/0skip: `TestResults/old-pin-focus/old-pin-focus.trx`.
- Pin-only accepted Auth Release0warnings/0errors followed by17FAIL/0PASS/0skip, all actual exchange expected200 versus401: `TestResults/new-pin-before-fixture/new-pin-before-fixture.trx`. This is the actual compatibility gap, not a new runtime defect.
- Initial helper build dependency conflict (`EF.Relational10.0.4` versus producer10.0.12) is preparation diagnostic, not product RED. Generated utility explicitly pins the producer's10.0.12 relational dependency.
- Initial normal-login fixture used a non-email username and reached400; LoginRequest requires EmailAddress. Corrected synthetic fixture uses reserved `.invalid` email-shaped usernames; no product validation relaxation.
- Initial real seed Id42 emitted actual subject42: `TestResults/accepted-pin-chain-v3/accepted-pin-chain-v3.trx`13PASS4FAIL. Fixture identities are now seeded with exact existing expected Id`employee:42`/`employee:99`; claims are never rewritten. Original assertions are untouched.
- New xUnit analyzer compile errors were corrected with predicate Assert.Single overloads. A prematurely launched no-build run used stale v3 binary: `TestResults/accepted-focus/accepted-focus.trx`14PASS5FAIL. It is explicitly excluded from final validation/product-RED claims.
- Fresh successful build followed by24PASS/0FAIL/0skip: `TestResults/accepted-focus-v4/accepted-focus-v4.trx`, comprising original17, new5 and existing two submitted-COMMIT uncertainty cases.

New independent controls read actual migration history/current database; employee JWT sid; real owner/kind/family/expiry/token hash; matching confirmed identity security stamp; real service-login claims and exact invoice audience/scope. An isolated fixture revokes the persisted family then observes actual exchange401, zero Accounting live checks/effects/admission rows. Guard negatives never invoke migrations. Final expanded focus/full/static gates are recorded below when terminal.

## Dependency and authority limits

Accounting private Defaults `8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3`; Contracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. Producer private Defaults `5c5f9479313710fa576f83d3b396442997a2fcf4`, same Contracts. All clones/outputs belong to this worktree and must stay clean at exact pins.

Accounting is normal Production HTTP with actual RS256 verification and real PostgreSQL admission/replay authority. Existing IIamServiceClient and invoice workflow remain strict controlled mocks: this proves the signed producer/consumer transport and durable Accounting admission boundary, NOT deployed live IAM grants, external financial workflow completion, quotation decision, source-data parity or provider delivery. The standalone submitted-COMMIT tests retain their existing actual registered-store behavior. Accounting37 durable notification adoption and Accounting41 trusted IAM bridge remain separate open prerequisites. No new permissions, bypasses, wildcard grants or JWT rewriting are introduced.

## Reproduction

Run from this owned worktree with private dependencies, serially:

```powershell
dotnet build Legacy.Maliev.AccountingService.slnx -c Release --nologo -warnaserror -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/accounting-auth-session-chain-20261001/.dependencies
dotnet test Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~InvoiceDelegationChainHttpTests|FullyQualifiedName~AcceptedAuthProducerChainTests|FullyQualifiedName~SubmittedCommitUnknown' --results-directory TestResults/accepted-final-focus --logger 'trx;LogFileName=focus.trx'
dotnet test Legacy.Maliev.AccountingService.Tests/Legacy.Maliev.AccountingService.Tests.csproj -c Release --no-build --no-restore --results-directory TestResults/accepted-final-full --logger 'trx;LogFileName=full.trx' --collect:'XPlat Code Coverage'
```

This is synthetic disposable test preparation, not a production Auth executable or runtime project dependency. No browser/live provider/persistent SQL/deployment/GitHub mutation is in scope.

## Final terminal evidence

- Focus `TestResults/accepted-final-focus/focus.trx`:29executed/passed,0failed/errors/notExecuted; original17 + new10 + two existing submitted-COMMIT controls.
- Full `TestResults/accepted-final-full/full.trx`:313executed/passed,0failed/errors/notExecuted,3m21s. All303existing cases pass without modifying their assertions.
- Unexcluded coverage `TestResults/accepted-final-full/54d8def6-107d-4c59-b3d0-66a1d71d9592/coverage.cobertura.xml`: API33.98%, Application87.58%, Data96.14%, Domain85.63%. API80 quality Accounting39 remains OPEN; no filter/denominator waiver.
- Whole private-graph `dotnet format Legacy.Maliev.AccountingService.slnx --verify-no-changes --no-restore` exit0, with environment `UseLocalMalievDependencies=true` and absolute owned `MalievWorkspaceRoot`.
- Five serial `dotnet list <Api|Application|Data|Domain|Tests project> package --vulnerable --include-transitive` exit0/no vulnerable packages.
- Actual PowerShell AST parse of preparation script: zero parse errors. `git diff --check` exit0. Original17 test source is unchanged.
- `C:/Users/natth/go/bin/gitleaks.exe stdin --redact=100 --no-banner --no-color` over all four owned files:42143bytes scanned,0findings,exit0; no new suppression or credential-literal replacement.
- All five private source clones verified clean at the exact pins above. Old Auth clone and earlier generated diagnostics are preserved. No active process handle remains; accepted final build/focus/full handles were terminal before statics.

The authentication, testing and TDD skills guided the real-session fixture boundary and honest diagnostic separation. This test-only producer adoption does not resolve historical source-owner financial correctness or deployment authority.

## Root integration validation

Root independently built the exact private graph in Release with warnings as
errors: zero warnings/errors. Focused29/29 and unfiltered313/313 passed with
zero skips; full duration3m10s. Root TRX
`TestResults/root-accepted-full/root-full.trx` SHA256
`CD805CB43FDE95FC5207543123D597497A93AA2B51DA7B9759B2EC535EF3216A`.
Whole private-graph formatting, unsuppressed four-file secret scan and whitespace
passed. [Issue46](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.AccountingService/issues/46)
tracks protected-main integration; exact-head and post-main CI remain required.
Agent restrictions above do not authorize deployment or broaden root runtime
authority. No source-owner ledger entry is resolved by these fixture tests alone.

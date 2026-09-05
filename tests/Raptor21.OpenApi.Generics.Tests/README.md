# Raptor21.OpenApi.Generics.Tests

The first test project in the repo. It covers the two code-emitting paths — the Refit (C#) generator and its
TypeScript sibling — plus the reconstruction they share.

## What is covered

- **`CSharpStabilityTests`** — the Refit output of the sample document is pinned to a committed snapshot. The
  TypeScript emitter shares `GenericsReconstruction` and `Names` with this path, so this snapshot is the
  tripwire that catches a shared-seam refactor changing the C# output as a side effect. A second test asserts
  the envelope/container are reconstructed as generics and the flattened `x-ignore-model` schemas never become
  models.
- **`TypeScriptEmitterTests`** — behavioural assertions over a fixture (`Fixtures/widgets.openapi.json`) built
  to pack every hazard the design calls out: an enveloped object and an enveloped array, an array whose
  elements are themselves envelopes, a string enum, a nullable property, a no-body (`204`) operation, and a
  `200` that lists `text/plain` and `text/json` alongside `application/json` in a misleading order. It asserts
  `BaseResponse<T>` is a real generic, enums are string-literal unions, `credentials: 'include'` is on every
  call, `application/json` wins media-type selection, and method names come from `FallbackName` (the document
  has no `operationId`). Container reconstruction (`x-data-container` / `x-data-item`) is exercised through the
  sample fixture, since the live BackOffice document has no container. The full TypeScript output is also
  snapshotted for the same seam-refactor protection as the C# side.
- **`QueriesHookNamingTests`** — regression cover for two `queries.ts` defects the emitter accepts happily and
  the consumer only meets at `tsc` time. `queries.ts` flattens every client class's methods into module-level
  hooks, so two tags deriving the same method name used to declare the same `use…` hook twice and the consuming
  app died on the duplicate; and the per-tag key factory used to declare its parameters required while the hook
  passed `string | undefined` into them (TS2345, at a call the emitter itself wrote).
  `Fixtures/hooks.openapi.json` reproduces the live document's shape — no `operationId` anywhere, so names come
  from the route, and `/api/customers/{id}` under `CustomerDetail` collides with
  `/api/notifications/push/customers` under `Notifications`. The two invariants (no duplicate top-level export;
  every key factory entry mirrors its hook's parameter list, optional markers included) are written as functions
  over the emitted text, so they run against every fixture in the suite **and** against the verbatim pre-fix
  output — which is what keeps the guard from passing vacuously.

## Snapshot convention (plain committed `.expected` files)

Snapshots are plain text files under `Snapshots/`, committed and compared verbatim. This was chosen over Verify
because it is the lighter option: no package, no runner integration, no `.received`/approval dance, and the
expected text is reviewable in the diff of the change that moves it — which is exactly the point of a snapshot
that guards generated output.

`SnapshotTester` normalises line endings to `\n` on **both** sides before comparing. The generators mix
`Environment.NewLine` (CRLF on Windows, LF on Linux) with the newlines baked into the Scriban templates, so a
byte-exact compare would break the moment the same snapshot is checked on the other OS. CI runs on Linux;
developers run on Windows. The committed `.expected` files are stored LF.

Fixtures and snapshots are located in the **source tree** at run time via `[CallerFilePath]`, not copied to
`bin/`, so a create-on-missing or an update writes back to the committed files.

### Updating snapshots

An intended output change is re-baselined by running with the environment variable set:

```bash
# bash
RAPTOR21_UPDATE_SNAPSHOTS=1 dotnet test tests/Raptor21.OpenApi.Generics.Tests
```

```powershell
# PowerShell
$env:RAPTOR21_UPDATE_SNAPSHOTS = '1'; dotnet test tests/Raptor21.OpenApi.Generics.Tests; Remove-Item Env:\RAPTOR21_UPDATE_SNAPSHOTS
```

A missing snapshot is written and the test fails once, asking you to review the new file and re-run — this is
how a new snapshot is born without a run silently passing on output nobody looked at.

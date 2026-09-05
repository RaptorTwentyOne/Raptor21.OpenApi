# @raptortwentyone/openapi

The [Raptor21.OpenApi](https://github.com/RaptorTwentyOne/Raptor21.OpenApi) generator as an npm
command. It produces API clients that keep generic response envelopes (`BaseResponse<T>`, `Page<T>`)
as real generics instead of flattening them into one class per payload:

- **TypeScript**: `types.ts` (DTOs + `BaseResponse<T>`), `client.ts` (fetch, `credentials: 'include'`,
  envelope unwrapped to the payload), and with `--queries` a `queries.ts` of TanStack Query hooks and
  query-key factories.
- **C#**: Refit interfaces bound to the contract the server already owns.

The generator is a .NET tool. This package installs it — pinned to the same version as the package —
into its own directory the first time the command runs, then forwards every argument to it. Nothing
happens at `npm install` (no postinstall), nothing is installed globally.

## Requirements

A .NET SDK 8.0 or later on `PATH` (`dotnet --version` works). Get it from https://dot.net.

## Use

```bash
pnpm add -D @raptortwentyone/openapi      # or npm / yarn
```

```jsonc
// package.json
{
  "scripts": {
    "generate": "raptor21-openapi http://localhost:5087/swagger/v1/swagger.json -l typescript --queries --no-headers -o src/api/generated"
  }
}
```

`pnpm generate` writes `types.ts`, `client.ts` and `queries.ts` into `src/api/generated`. Every flag
of the .NET tool is available; `raptor21-openapi --help` lists them. The ones that matter for a
TypeScript consumer:

| Flag | Meaning |
|---|---|
| `-l, --language typescript` | Emit TypeScript (default is C#). `-o` is then a directory. |
| `--queries` | Also emit `queries.ts` (TanStack Query). |
| `--no-headers` | Drop cross-cutting header parameters (tenant, locale) from every signature — they belong to your fetch wrapper. |
| `--body-name <name>` | Name of the request-body parameter (default `request`). |
| `--optional-body` | Keep a body optional when the document leaves `requestBody.required` unset. |
| `--map <from>=<to>` | Map a projected contract type onto a local one. |

Wire the generated client to your app once:

```ts
import { configureApiClients } from './api/generated/queries'
configureApiClients({ baseUrl: '', fetch: myFetchWithCsrfAndAuth })
```

## Environment

| Variable | Effect |
|---|---|
| `RAPTOR21_OPENAPI_VERSION` | Use another tool version than the package's own (escape hatch). |
| `RAPTOR21_OPENAPI_TOOL_DIR` | Where the tool is installed (default `node_modules/@raptortwentyone/openapi/.tool`). |

## Versioning

The npm package and the .NET tool share one version; both are published from the same git tag of the
repository. A freshly released version can take a few minutes to become installable while nuget.org
indexes it.

## License

MIT

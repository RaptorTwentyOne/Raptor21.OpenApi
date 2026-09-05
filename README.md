# Raptor21.OpenApi

Keep your C# generic contracts intact across the OpenAPI lifecycle.

## The problem

An ASP.NET Core API returns a generic envelope:

```csharp
[ProducesResponseType(typeof(List<CountryDto>), StatusCodes.Status200OK)]
public async Task<IActionResult> GetCountries() =>
    CreateActionResultInstance(BaseResponse<List<CountryDto>>.Success(result, 200));
```

`BaseResponse<T>` is a contract that already has an owner. OpenAPI has no notion of generics, so
the document can only carry a flattened schema, and every client generator turns that schema into a
brand-new class:

```csharp
// What generators produce today
public partial class CountryDtoListBaseResponse
{
    public ICollection<CountryDto> Data { get; set; }
    public int StatusCode { get; set; }
    public bool IsSuccessful { get; set; }
    public ICollection<string> Errors { get; set; }
}
```

One endpoint, one harmless-looking class. Across an API surface it multiplies: a nine-service
gateway with ~280 operations produces a few hundred of these, each one a second definition of a
contract that already exists, and each one a place for the two definitions to drift apart.

Renaming does not help. Naming the schema `BaseResponse<CountryDto>` in the document is both invalid
OpenAPI — component keys are restricted to `^[a-zA-Z0-9.\-_]+$` — and useless in practice, because
generators treat the name as an opaque label and flatten it anyway.

## What this does

```csharp
// What Raptor21.OpenApi produces
Task<BaseResponse<ICollection<CountryDto>>> GetCountries(CancellationToken cancellationToken = default);
```

No generated envelope class. The client binds to the same `BaseResponse<T>` the server already
returns, so there is exactly one definition of the contract.

This works in two phases:

- **Projection** — the server writes the generic structure into the OpenAPI document as vendor
  extensions while emitting a perfectly ordinary, valid schema. Tooling that does not understand the
  extensions ignores them and still sees a correct document.
- **Reconstruction** — the client generator reads those extensions and rebuilds the generic type
  instead of materialising a new class.

The contract is the source of truth; OpenAPI is a projection of it; generation reconstructs it.

## Using it

Two independent halves. Add the projection to the server so its document carries the metadata; run the
generator against that document to get a client. You can adopt either one alone — a document without the
metadata still generates, it just cannot rebuild generics.

### 1. Projection — teach the server to describe its envelope

```bash
dotnet add package Raptor21.OpenApi.Generics.AspNetCore
```

```csharp
builder.Services.AddSwaggerGen(c =>
{
    c.AddOpenApiGenerics(g => g.UseEnvelope(typeof(BaseResponse<>)));
});
```

`UseEnvelope` does two things: it records `BaseResponse<>` as an envelope so the schemas it produces are
marked as such, and it re-declares payload-only responses as the envelope. That second part matters when
controllers are annotated with the payload but return the envelope:

```csharp
[ProducesResponseType(typeof(List<CountryDto>), StatusCodes.Status200OK)]   // the payload
public async Task<IActionResult> GetCountries()
    => CreateActionResultInstance(BaseResponse<List<CountryDto>>.Success(result, 200));   // the envelope
```

Without it the document promises a bare list and every generated client fails to deserialise the object it
actually receives. With it, the attributes stay exactly as their authors wrote them.

If your controllers already declare the envelope themselves, use `AddEnvelope` instead — it registers the
type without rewriting any response.

| Option | Purpose |
|---|---|
| `UseEnvelope(type)` | Register an envelope **and** apply it to payload-only responses. |
| `AddEnvelope(type)` | Register an envelope without rewriting responses. |
| `AddContainer(type)` | Register a payload container, such as a pagination page. |
| `Registry.AddFromAssemblies(...)` | Pick up types marked `[ApiWrapper]` / `[DataContainer]`. |
| `MarkProjectedSchemasIgnored` | Defaults to true. Turn off to keep a document that generators without reconstruction support can still build models from. |

Binary responses (`application/pdf`, spreadsheet exports) and actions typed as `IActionResult` or
`FileResult` are left alone — they are not enveloped on the wire.

### 2. Generation — turn a document into a client

```bash
dotnet tool install --global Raptor21.OpenApi.Generics.Cli
```

From a JavaScript project, use the npm front door instead — it installs the same .NET tool, pinned to
its own version, on first run (needs a .NET SDK on PATH) and forwards every flag:

```bash
pnpm add -D @raptortwentyone/openapi
```

```jsonc
{ "scripts": { "generate": "raptor21-openapi https://api.example.com/swagger/v1/swagger.json -l typescript --queries --no-headers -o src/api/generated" } }
```

Point it at a file or a URL:

```bash
raptor21-openapi https://api.example.com/swagger/v1/swagger.json \
  --namespace Acme.Api.Catalog \
  --output src/Acme.Api/Generated/Catalog.cs \
  --path-prefix api/catalog \
  --no-headers \
  --exclude-tag Test \
  --registration-method AddCatalogApis \
  --map Acme.Contracts.BaseResponse=Acme.Api.BaseResponse
```

Generation is a deliberate step, not a build step. A document only changes when the service does, so
regenerate when you mean to and review the result like any other change.

#### Options

| Option | What it does |
|---|---|
| `-n, --namespace` | Namespace for the generated interfaces and models. One per service keeps colliding DTO names apart. |
| `-o, --output` | File to write. Without it the code goes to standard output. |
| `--path-prefix` | Prepended to every route. Needed when the document comes from a service directly but clients reach it through a gateway that mounts it under a prefix — see the note below. |
| `--no-headers` | Leave every header parameter out of the signatures. |
| `--skip-header <name>` | Leave one header out. Repeatable. |
| `--exclude-tag <tag>` | Skip every operation carrying a tag, such as internal diagnostics endpoints. Repeatable. |
| `--map <from>=<to>` | Map a projected contract type onto a local one. Repeatable. |
| `--registration-method <name>` | Also emit an extension method registering every interface as a Refit client. |
| `--single-interface` | One interface for the whole document instead of one per tag. |
| `--payload-property <name>` | Envelope property holding the payload. Defaults to `data`. |
| `--using <namespace>` | Extra `using` in the generated file. Repeatable. |
| `--no-cancellation-tokens` | Omit the trailing `CancellationToken`. |
| `-l, --language typescript` | Emit `types.ts` / `client.ts` (fetch) instead of C#; `--output` is then a directory. |
| `--queries` | TypeScript: also emit `queries.ts` with TanStack Query hooks and query-key factories. |
| `--body-name <name>` | TypeScript: name of the request-body parameter. Defaults to `request`. |
| `--optional-body` | TypeScript: keep a body optional when the document leaves `requestBody.required` unset. By default every body is required, because Swashbuckle never sets the flag for `[FromBody]` and the server answers 400 without one. |

#### Why `--path-prefix` rather than a base address

Generated routes start with a slash. A rooted relative URI discards whatever path the `HttpClient`'s base
address carried, so `https://host/api/catalog/` + `/Products` resolves to `https://host/Products`. Putting
the prefix in the route is what makes the two agree.

#### Why `--map`

`x-api-wrapper-type` records the envelope's fully qualified name in the producing ecosystem. That is often
not the name the consumer knows it by — a mobile app cannot reference a server-side package, and a C#
consumer of a Java service has its own port of the contract. `--map` states the correspondence.

#### The generated project needs

```xml
<PackageReference Include="Refit" Version="15.0.0" />
<PackageReference Include="Refit.HttpClientFactory" Version="15.0.0" />   <!-- only with --registration-method -->
```

and your own definition of the envelope, matching the server's shape:

```csharp
public sealed class BaseResponse<T>
{
    [JsonPropertyName("data")] public T? Data { get; set; }
    [JsonPropertyName("statusCode")] public int StatusCode { get; set; }
    [JsonPropertyName("isSuccessful")] public bool IsSuccessful { get; set; }
    [JsonPropertyName("errors")] public IList<string> Errors { get; set; } = [];
}
```

Registration binds the source-generated Refit implementations, so it works under trimming and
ahead-of-time compilation:

```csharp
services.AddCatalogApis(builder => builder
    .ConfigureHttpClient(c => c.BaseAddress = new Uri("https://api.example.com/"))
    .AddHttpMessageHandler<YourHeadersHandler>());
```

### Where to keep the documents

Nowhere in particular — the generator reads a path or a URL and is finished. Keeping a copy under source
control makes API changes visible as a diff and lets you regenerate offline; fetching fresh each time keeps
one less thing in the repository. Both work.

## Packages

| Package | Responsibility |
|---|---|
| `Raptor21.OpenApi.Generics.Abstractions` | The metadata protocol: extension keys, envelope and container descriptors, attributes. |
| `Raptor21.OpenApi.Generics.AspNetCore` | Projection — Swashbuckle integration that writes the metadata while generating the document. |
| `Raptor21.OpenApi.Generics.CodeGen` | Reconstruction — reads a document and emits Refit interfaces and models with generics restored. |
| `Raptor21.OpenApi.Generics.Cli` | `dotnet tool` front end for the generator. |

## Metadata protocol

The projection writes these vendor extensions onto the schemas it produces:

| Extension | Meaning |
|---|---|
| `x-api-wrapper` | The schema is a generic response envelope. |
| `x-api-wrapper-type` | Fully qualified type of the envelope. |
| `x-api-wrapper-datatype` | The envelope's payload datatype. |
| `x-data-container` | The payload has generic container semantics. |
| `x-data-container-type` | Fully qualified type of the container. |
| `x-data-item` | The concrete item or payload type. |
| `x-ignore-model` | Infrastructure schema — do not emit it as a standalone model. |
| `x-raptor21-version` | On `info`: the protocol edition the document was projected with (`"1"`). A generator refuses a major it does not implement; a document without it is read as version 1. |

The full contract — every key, its type, where it may appear, how a generator reconstructs from it, and the
compatibility rules — is [docs/Raptor21-Generics-Extensions-v1.md](docs/Raptor21-Generics-Extensions-v1.md).

The `*-type` values carry language-specific type names, so a document produced by one ecosystem
describes that ecosystem's types. Cross-ecosystem consumers share the protocol shape and map the
names through their own type registry.

## Prior art and thanks

The metadata protocol and the projection/reconstruction split come from
[**blueprint-platform/openapi-generics**](https://github.com/blueprint-platform/openapi-generics)
by the Blueprint Platform team — an MIT-licensed Java/Spring Boot solution to exactly this problem.
That project worked out the hard part: that the contract, not the document, should be the authority,
and that generic identity can survive OpenAPI as explicit reconstruction metadata.

This is an independent C# implementation, not a port. It deliberately reuses their extension names so
the two ecosystems describe the same thing the same way. Where the languages differ, so do the
implementations — C# generics let the reconstruction bind the type directly in the signature, where
the Java implementation emits a thin subclass that binds the parameters.

If you work in Java or Spring Boot, use theirs. It is the original, and it is good.

## Releasing

The git tag is the version. `Directory.Build.props` carries a development version so a local
`dotnet pack` produces something installable; a release overrides it from the tag, so every published
package traces back to exactly one commit.

```bash
git tag v0.2.0
git push origin v0.2.0
```

That triggers `.github/workflows/release.yml`, which builds, packs at the tagged version, pushes the
packages to nuget.org, opens a GitHub release with them attached, and then publishes `@raptortwentyone/openapi`
to npm at the same version (the `npm` job needs either an `NPM_TOKEN` secret or a trusted publisher
configured for the package on npmjs.com; without both it skips with a warning). A tag carrying a suffix
(`-preview.2`, `-rc.1`) is published as a prerelease.

A tag that is not a version is rejected before anything is packed. **nuget.org is permanent** — a
published version can be unlisted but never replaced — so the guard fails loudly rather than letting a
typo become a package nobody can take back.

## License

MIT — see [LICENSE](LICENSE).

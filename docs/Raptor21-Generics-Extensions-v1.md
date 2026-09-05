# Raptor21 Generics Extensions — protocol v1

The vendor extensions that carry generic contract identity through an OpenAPI document. This is the
contract between a **projector** (something that writes a document — today
`Raptor21.OpenApi.Generics.AspNetCore`) and a **reconstructor** (something that reads one — today the
C# Refit and TypeScript fetch generators in `Raptor21.OpenApi.Generics.CodeGen`). Anything that honours
this page can stand on either side.

The keys are shared with [blueprint-platform/openapi-generics](https://github.com/blueprint-platform/openapi-generics),
so a document projected here reads the same way there. The document-level version marker and the
compatibility rules are Raptor21's.

## 1. Principles

1. **A projected document is valid OpenAPI 3.x.** Every extension rides beside an ordinary, correct,
   flattened schema. A reader that ignores `x-` keys sees exactly what it would have seen without them.
2. **The contract is the source of truth; the document is a projection; generation is a reconstruction.**
   Extensions say what a flattened shape *came from*. They never change what the shape *is*.
3. **Names are the producing ecosystem's.** `*-type` values hold whatever the projector's language calls
   the contract (a C# server writes `Acme.Contracts.BaseResponse`). A consumer maps them to its own types
   through a type registry (`--map` in the CLI); unmapped names are used as written.
4. **Additive within a major.** A minor bump may add keys or values; it may not change the meaning of an
   existing key. A reader must ignore keys it does not know.

## 2. Document-level marker

| Key | Where | Type | Value |
|---|---|---|---|
| `x-raptor21-version` | `info` | string | `"<major>"` or `"<major>.<minor>"`, e.g. `"1"` |

- The projector writes the edition it implements. This page is edition **1**.
- A reconstructor compares the **major** with the edition it implements. A different major is refused
  outright (the generator throws instead of emitting a client that would misread the metadata). A higher
  minor of the same major is accepted; the reader ignores what it does not know.
- A document **without** the marker is read as version 1. Documents projected before the marker existed
  keep working unchanged.

## 3. Schema-level extensions

All of these appear on schema objects under `components/schemas` (or inline schemas). Booleans are JSON
booleans, strings are JSON strings.

### 3.1 Envelope

| Key | Type | Meaning |
|---|---|---|
| `x-api-wrapper` | boolean | The schema is a projected generic response envelope (e.g. `BaseResponse<T>` flattened for one `T`). |
| `x-api-wrapper-type` | string | Fully qualified type of the open envelope, e.g. `Acme.Contracts.BaseResponse`. |
| `x-api-wrapper-datatype` | string | The payload's datatype as named by the projector, e.g. `List<CountryDto>`. Descriptive: reconstructors read the payload's *shape* from the envelope's payload property (§4), not from this value. |

The projector writes all three together on every closed envelope it emits, and `x-ignore-model` (§3.3)
unless configured not to.

### 3.2 Container

| Key | Type | Meaning |
|---|---|---|
| `x-data-container` | string | The schema is a projected generic payload container (a page, a slice…). Value: the container's simple name, e.g. `Page`. |
| `x-data-container-type` | string | Fully qualified type of the open container, e.g. `Acme.Contracts.Page`. |
| `x-data-item` | string | The item type carried by the container as named by the projector, e.g. `CountryDto`. |

A container may itself be the payload of an envelope: `BaseResponse<Page<CountryDto>>` projects as an
envelope schema whose payload property references a container schema. Reconstruction composes them.

### 3.3 Infrastructure marker

| Key | Type | Meaning |
|---|---|---|
| `x-ignore-model` | boolean | The schema exists only to carry projection metadata and must not become a standalone generated model. |

Projected envelopes and containers carry it by default. A projector can leave it off to keep a document
that older generators (without reconstruction) can still turn into usable, if duplicated, models.

## 4. Reconstruction rules

A reconstructor walks `components/schemas` once, then resolves operation parameter and response schemas.

1. **Envelope**: for a schema with `x-api-wrapper: true`, read `x-api-wrapper-type`, map it through the
   consumer's type registry, and take the payload's schema from the envelope's **payload property** —
   `data` unless the consumer says otherwise (`--payload-property`). The reconstructed type is
   `Envelope<Payload>` where `Payload` is resolved recursively by these same rules.
2. **Container**: for a schema with `x-data-container-type`, map the container type, resolve the item's
   schema from the container's item-bearing property, and produce `Container<Item>`.
3. **Array of reconstructed elements**: an array whose item schema was reconstructed becomes the
   language's collection of that type (`ICollection<BaseResponse<T>>`, `BaseResponse<T>[]`). It is *not*
   itself an envelope and is never unwrapped.
4. **Excluded models**: every schema with `x-ignore-model: true` is left out of generated models. Its
   name is still known to the reconstructor so references resolve.
5. **Nullability**: the envelope's own nullability and the payload's nullability are independent. A
   language that can express `T | null` keeps the payload's; a type argument position that cannot
   (generic arguments) has nullability stripped.
6. **Media type selection** for responses: prefer `application/json`, then any `+json`, then the first
   listed. Servers that list `text/plain` before JSON with the bare payload under it are common; picking
   JSON is what makes the envelope recognisable.
7. **Unknown keys** (any `x-` key not on this page) are ignored.

## 5. Request bodies (reader guidance, not extensions)

The protocol does not touch request bodies, but the reconstructors apply two conventions worth stating
because they are visible in the generated surface:

- `requestBody.required` defaults to false in OpenAPI and Swashbuckle never sets it for `[FromBody]`.
  The TypeScript generator therefore treats a JSON body as **required** unless told otherwise
  (`--optional-body`); an explicit `required: true` is always honoured. The C# generator keeps the
  document's flag.
- A `multipart/form-data` body is spread into one parameter per property. Binary fields (`format:
  binary`, or arrays of them) become file parts (`StreamPart` / `Blob`); other fields are scalars.

## 6. Compatibility

| Situation | Behaviour |
|---|---|
| Document has no `x-raptor21-version` | Read as v1. |
| Document major = reader major | Accepted. Unknown keys ignored. |
| Document major ≠ reader major | Refused with a message naming both versions. |
| Document has envelope keys but no `x-ignore-model` | Envelope still reconstructed; the flattened schema is additionally emitted as a model (older-generator compatibility mode). |
| `x-api-wrapper-type` not in the consumer's registry | Used verbatim as a type name. |

## 7. Example

```json
{
  "openapi": "3.0.4",
  "info": { "title": "Catalog", "version": "1.0", "x-raptor21-version": "1" },
  "components": {
    "schemas": {
      "CountryDto": { "type": "object", "properties": { "code": { "type": "string" } } },
      "CountryDtoListBaseResponse": {
        "type": "object",
        "properties": {
          "data": { "type": "array", "items": { "$ref": "#/components/schemas/CountryDto" }, "nullable": true },
          "statusCode": { "type": "integer" },
          "isSuccessful": { "type": "boolean" },
          "errors": { "type": "array", "items": { "type": "string" }, "nullable": true }
        },
        "x-api-wrapper": true,
        "x-api-wrapper-type": "Acme.Contracts.BaseResponse",
        "x-api-wrapper-datatype": "List<CountryDto>",
        "x-ignore-model": true
      }
    }
  }
}
```

Reconstructs as `BaseResponse<ICollection<CountryDto>>` (C#) and `BaseResponse<CountryDto[]>` (TypeScript),
with a single `BaseResponse<T>` definition and no `CountryDtoListBaseResponse` model.

## 8. Change log

| Edition | Change |
|---|---|
| 1 | Initial: envelope, container and infrastructure keys (shared with openapi-generics), document-level version marker, compatibility rules. |

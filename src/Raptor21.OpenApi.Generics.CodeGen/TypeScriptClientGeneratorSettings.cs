using System;
using System.Collections.Generic;

namespace Raptor21.OpenApi.Generics.CodeGen;

/// <summary>Controls generation of a TypeScript fetch client from an OpenAPI document.</summary>
public sealed class TypeScriptClientGeneratorSettings : ClientGeneratorSettingsBase
{
    /// <summary>How operations are split across client classes.</summary>
    public InterfaceGrouping Grouping { get; set; } = InterfaceGrouping.ByTag;

    /// <summary>Name of the single generated client class when <see cref="InterfaceGrouping.Single"/> is used.</summary>
    public string SingleClientName { get; set; } = "ApiClient";

    /// <summary>
    /// Header parameters to leave out of generated method signatures.
    /// </summary>
    /// <remarks>
    /// Cross-cutting headers — a tenant, a locale, a platform marker — belong to the fetch pipeline (an
    /// interceptor or a wrapping <c>fetch</c>), not to every call site. A document that documents them per
    /// operation describes the wire correctly; repeating them in every method does not.
    /// </remarks>
    public ISet<string> ExcludedHeaderParameters { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Drops every header parameter from generated signatures.</summary>
    public bool ExcludeAllHeaderParameters { get; set; }

    /// <summary>Operation tags to skip entirely, such as internal diagnostics endpoints.</summary>
    public ISet<string> ExcludedTags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Prepended to every route, for a service reached through a gateway that mounts it under a prefix.</summary>
    public string? PathPrefix { get; set; }

    /// <summary>Also emit <c>queries.ts</c> with TanStack Query hooks over the client.</summary>
    public bool GenerateQueries { get; set; }

    /// <summary>Name of the request-body parameter in generated signatures. Defaults to <c>request</c>.</summary>
    /// <remarks>
    /// The document names the body parameter <c>body</c> whatever it carries; call sites read better with a
    /// name that says what the argument is. When an operation also has a path, query or header parameter
    /// of the same name, the body falls back to <c>requestBody</c>.
    /// </remarks>
    public string BodyParameterName { get; set; } = "request";

    /// <summary>
    /// Treat the request body as required even when the document leaves <c>requestBody.required</c> unset.
    /// Defaults to true. A body the document marks <c>required: true</c> is always required.
    /// </summary>
    /// <remarks>
    /// OpenAPI defaults <c>required</c> to false and Swashbuckle does not set it for <c>[FromBody]</c>
    /// parameters, so a document produced by ASP.NET Core describes every JSON body as optional — bodies the
    /// server answers 400 without. Generating <c>request?: T</c> from that pushes a spurious undefined-check
    /// onto every call site. Turn this off for a document that marks its bodies deliberately.
    /// </remarks>
    public bool BodyParameterRequired { get; set; } = true;
}

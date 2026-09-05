using System;
using System.Collections.Generic;

namespace Raptor21.OpenApi.Generics.CodeGen;

/// <summary>How operations are grouped into generated interfaces.</summary>
public enum InterfaceGrouping
{
    /// <summary>One interface per OpenAPI tag. The usual choice — it mirrors how controllers are organised.</summary>
    ByTag,

    /// <summary>A single interface for the whole document.</summary>
    Single,
}

/// <summary>Controls generation of a Refit client from an OpenAPI document.</summary>
public sealed class RefitClientGeneratorSettings : ClientGeneratorSettingsBase
{
    /// <summary>Namespace for the generated interfaces and models.</summary>
    public string Namespace { get; set; } = "GeneratedClient";

    /// <summary>How operations are split across interfaces.</summary>
    public InterfaceGrouping Grouping { get; set; } = InterfaceGrouping.ByTag;

    /// <summary>Name of the single generated interface when <see cref="InterfaceGrouping.Single"/> is used.</summary>
    public string SingleInterfaceName { get; set; } = "IApiClient";

    /// <summary>Adds a trailing <see cref="System.Threading.CancellationToken"/> to every method.</summary>
    public bool GenerateCancellationTokens { get; set; } = true;

    /// <summary>
    /// Header parameters to leave out of generated method signatures.
    /// </summary>
    /// <remarks>
    /// Cross-cutting headers — a tenant, a locale, a platform marker — belong to one place in the client's
    /// message pipeline, not to every call site. A document that documents them per operation is describing
    /// the wire correctly; repeating them in three hundred signatures is not.
    /// </remarks>
    public ISet<string> ExcludedHeaderParameters { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Drops every header parameter from generated signatures.</summary>
    public bool ExcludeAllHeaderParameters { get; set; }

    /// <summary>Operation tags to skip entirely, such as internal diagnostics endpoints.</summary>
    public ISet<string> ExcludedTags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Prepended to every route in the generated attributes.
    /// </summary>
    /// <remarks>
    /// A document taken from a service directly describes that service's own routes, while clients reach it
    /// through a gateway that mounts it under a prefix. Setting the prefix here rather than on the HTTP client's
    /// base address is deliberate: routes begin with a slash, and a rooted relative URI discards the base
    /// address's path, so a prefix applied there would silently vanish.
    /// </remarks>
    public string? PathPrefix { get; set; }

    /// <summary>Emits XML doc comments from the document's summaries and descriptions.</summary>
    public bool GenerateXmlDocComments { get; set; } = true;

    /// <summary>
    /// Emits an extension method that registers every generated interface as a Refit client.
    /// </summary>
    /// <remarks>
    /// A document of any size produces dozens of interfaces, and a hand-maintained registration list falls out
    /// of step with the document the first time an endpoint is added. Generating it keeps the two together and
    /// avoids reflection over the assembly, which trimmed and ahead-of-time compiled apps do not tolerate.
    /// Requires <c>Refit.HttpClientFactory</c> in the generated project.
    /// </remarks>
    public bool GenerateDependencyInjection { get; set; }

    /// <summary>
    /// Name of the generated registration method. Defaults to <c>AddGeneratedApis</c>.
    /// </summary>
    public string RegistrationMethodName { get; set; } = "AddGeneratedApis";

    /// <summary>Additional namespaces to import in the generated file.</summary>
    public IList<string> AdditionalNamespaces { get; } = new List<string>();
}

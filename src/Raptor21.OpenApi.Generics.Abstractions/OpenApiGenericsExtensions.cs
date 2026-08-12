namespace Raptor21.OpenApi.Generics;

/// <summary>
/// Vendor extension keys that carry generic contract identity through an OpenAPI document.
/// </summary>
/// <remarks>
/// OpenAPI has no generics, so a projected schema can only be a flattened shape. These extensions ride
/// alongside it and say what the shape came from, which is what lets a generator rebuild
/// <c>BaseResponse&lt;CountryDto&gt;</c> instead of inventing <c>CountryDtoBaseResponse</c>.
///
/// The keys match those used by
/// <see href="https://github.com/blueprint-platform/openapi-generics">blueprint-platform/openapi-generics</see>
/// so documents produced here describe themselves the same way theirs do. A document carrying them stays
/// valid OpenAPI: tooling that does not recognise the extensions ignores them and reads the flattened schema.
///
/// Note that the <c>*-type</c> values hold language-specific type names. A document written by a C# server
/// names C# types; a consumer in another ecosystem shares the protocol but maps the names itself.
/// </remarks>
public static class OpenApiGenericsExtensions
{
    /// <summary>Marks a schema as a projected generic response envelope. Boolean.</summary>
    public const string ApiWrapper = "x-api-wrapper";

    /// <summary>Fully qualified type of the envelope, e.g. <c>Acme.Contracts.BaseResponse</c>. String.</summary>
    public const string ApiWrapperType = "x-api-wrapper-type";

    /// <summary>The envelope's payload datatype, named as it appears in the document. String.</summary>
    public const string ApiWrapperDataType = "x-api-wrapper-datatype";

    /// <summary>Marks a schema whose payload has generic container semantics. String — the container's simple name.</summary>
    public const string DataContainer = "x-data-container";

    /// <summary>Fully qualified type of the container, e.g. <c>Acme.Contracts.Page</c>. String.</summary>
    public const string DataContainerType = "x-data-container-type";

    /// <summary>The concrete item or payload type carried by the container. String.</summary>
    public const string DataItem = "x-data-item";

    /// <summary>
    /// Marks an infrastructure schema that exists only to carry projection metadata and must not become a
    /// standalone generated model. Boolean.
    /// </summary>
    public const string IgnoreModel = "x-ignore-model";
}

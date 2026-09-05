using System.Collections.Generic;

using Microsoft.OpenApi;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Raptor21.OpenApi.Generics.AspNetCore;

/// <summary>
/// Writes the metadata protocol version onto the document's <c>info</c> object.
/// </summary>
/// <remarks>
/// The schema-level extensions say what each projected shape came from; this one says which edition of the
/// protocol they follow, so a generator can refuse a document it would otherwise misread. The current
/// edition is <see cref="OpenApiGenericsExtensions.CurrentVersion"/>; the protocol is documented in
/// <c>docs/Raptor21-Generics-Extensions-v1.md</c>.
/// </remarks>
public sealed class ApiGenericsVersionDocumentFilter : IDocumentFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        if (swaggerDoc?.Info is null)
            return;

        swaggerDoc.Info.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        swaggerDoc.Info.Extensions[OpenApiGenericsExtensions.Version] =
            new JsonNodeExtension(OpenApiGenericsExtensions.CurrentVersion);
    }
}

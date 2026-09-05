using System;

using Microsoft.Extensions.DependencyInjection;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Raptor21.OpenApi.Generics.AspNetCore;

/// <summary>Wires generic contract projection into Swashbuckle's document generation.</summary>
public static class OpenApiGenericsSwaggerGenExtensions
{
    /// <summary>
    /// Projects generic response contract identity into the generated OpenAPI document.
    /// </summary>
    /// <example>
    /// An envelope owned by a package you cannot annotate, applied to responses that declare only their payload:
    /// <code>
    /// builder.Services.AddSwaggerGen(c =>
    /// {
    ///     c.AddOpenApiGenerics(g => g.UseEnvelope(typeof(BaseResponse&lt;&gt;)));
    /// });
    /// </code>
    /// </example>
    public static SwaggerGenOptions AddOpenApiGenerics(
        this SwaggerGenOptions swaggerGenOptions,
        Action<OpenApiGenericsOptions>? configure = null)
    {
        if (swaggerGenOptions is null)
            throw new ArgumentNullException(nameof(swaggerGenOptions));

        var options = new OpenApiGenericsOptions();
        configure?.Invoke(options);

        // Ordering matters: the operation filter swaps in the envelope schema, and generating that schema is
        // what makes the schema filter run for the envelope type and stamp it.
        swaggerGenOptions.OperationFilter<ApiGenericsEnvelopeOperationFilter>(options);
        swaggerGenOptions.SchemaFilter<ApiGenericsSchemaFilter>(options);
        swaggerGenOptions.DocumentFilter<ApiGenericsVersionDocumentFilter>();

        return swaggerGenOptions;
    }
}

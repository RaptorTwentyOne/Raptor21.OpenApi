using System;
using System.Collections.Generic;

using Microsoft.OpenApi;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Raptor21.OpenApi.Generics.AspNetCore;

/// <summary>
/// Stamps generic contract identity onto the schemas Swashbuckle generates for registered envelopes and
/// containers.
/// </summary>
/// <remarks>
/// The schema itself stays an ordinary flattened object — a reader that knows nothing about these extensions
/// sees a valid document and a correct shape. What the extensions add is where the shape came from, which is
/// the only thing a generator needs to rebuild <c>BaseResponse&lt;CountryDto&gt;</c> rather than declaring a
/// new <c>CountryDtoBaseResponse</c>.
/// </remarks>
public sealed class ApiGenericsSchemaFilter : ISchemaFilter
{
    private readonly OpenApiGenericsOptions _options;

    /// <summary>Creates the filter.</summary>
    public ApiGenericsSchemaFilter(OpenApiGenericsOptions options)
        => _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        // Reference schemas reach this filter too; only the concrete definition carries extensions worth writing.
        if (schema is not OpenApiSchema concrete || context?.Type is null)
            return;

        if (_options.Registry.TryGetWrapper(context.Type, out var wrapper))
            StampWrapper(concrete, context.Type, wrapper);

        if (_options.Registry.TryGetContainer(context.Type, out var container))
            StampContainer(concrete, context.Type, container);
    }

    private void StampWrapper(OpenApiSchema schema, Type closedType, ApiWrapperDescriptor descriptor)
    {
        var payload = closedType.GetGenericArguments()[descriptor.DataParameterIndex];

        Set(schema, OpenApiGenericsExtensions.ApiWrapper, true);
        Set(schema, OpenApiGenericsExtensions.ApiWrapperType, descriptor.TypeName);
        Set(schema, OpenApiGenericsExtensions.ApiWrapperDataType, DataTypeName(payload));

        if (_options.MarkProjectedSchemasIgnored)
            Set(schema, OpenApiGenericsExtensions.IgnoreModel, true);
    }

    private void StampContainer(OpenApiSchema schema, Type closedType, DataContainerDescriptor descriptor)
    {
        var item = closedType.GetGenericArguments()[descriptor.ItemParameterIndex];

        Set(schema, OpenApiGenericsExtensions.DataContainer, descriptor.Name);
        Set(schema, OpenApiGenericsExtensions.DataContainerType, descriptor.TypeName);
        Set(schema, OpenApiGenericsExtensions.DataItem, DataTypeName(item));

        if (_options.MarkProjectedSchemasIgnored)
            Set(schema, OpenApiGenericsExtensions.IgnoreModel, true);
    }

    /// <summary>
    /// A readable name for the payload. Reconstruction reads the payload's real shape from the envelope's own
    /// data property, so this value is descriptive rather than load-bearing — it exists because the protocol
    /// carries it and because it makes a projected document legible to a human reading the raw JSON.
    /// </summary>
    private static string DataTypeName(Type type)
    {
        if (type.IsArray)
            return DataTypeName(type.GetElementType()!) + "[]";

        if (type.IsGenericType)
        {
            var arguments = type.GetGenericArguments();
            var names = new string[arguments.Length];
            for (var i = 0; i < arguments.Length; i++)
                names[i] = DataTypeName(arguments[i]);

            return TypeNaming.SimpleNameWithoutArity(type) + "<" + string.Join(",", names) + ">";
        }

        return TypeNaming.SimpleNameWithoutArity(type);
    }

    private static void Set(OpenApiSchema schema, string key, string value)
        => Extensions(schema)[key] = new JsonNodeExtension(value);

    private static void Set(OpenApiSchema schema, string key, bool value)
        => Extensions(schema)[key] = new JsonNodeExtension(value);

    private static IDictionary<string, IOpenApiExtension> Extensions(OpenApiSchema schema)
        => schema.Extensions ??= new Dictionary<string, IOpenApiExtension>();
}

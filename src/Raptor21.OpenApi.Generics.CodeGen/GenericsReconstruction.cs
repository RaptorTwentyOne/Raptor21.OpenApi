using System;
using System.Collections.Generic;
using System.Linq;

using NJsonSchema;

using NSwag;

namespace Raptor21.OpenApi.Generics.CodeGen;

/// <summary>
/// Rebuilds generic contract types from the metadata a projected document carries.
/// </summary>
/// <remarks>
/// A projected envelope reaches the document as an ordinary flattened schema — <c>CountryDtoBaseResponse</c>
/// with a <c>data</c> property — plus extensions saying what it was before flattening. This class reads those
/// extensions and hands back <c>BaseResponse&lt;ICollection&lt;CountryDto&gt;&gt;</c>, so the generated client
/// binds to the contract the service already owns rather than declaring a parallel copy of it.
///
/// Schemas that exist only to carry this metadata are collected in <see cref="ExcludedTypeNames"/> and never
/// become models.
/// </remarks>
public sealed class GenericsReconstruction
{
    private readonly OpenApiDocument _document;
    private readonly ClientGeneratorSettingsBase _settings;
    private readonly ITypeResolver _resolver;
    private readonly Dictionary<JsonSchema, string> _reconstructed = new();
    private readonly HashSet<string> _excluded = new(StringComparer.Ordinal);

    /// <summary>Analyses a document's definitions.</summary>
    public GenericsReconstruction(OpenApiDocument document, ClientGeneratorSettingsBase settings, ITypeResolver resolver)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

        ProtocolVersion = ReadProtocolVersion(document);
        Analyse();
    }

    /// <summary>
    /// The metadata protocol version the document declares on <c>info</c> (<c>x-raptor21-version</c>), or
    /// null for a document written before the marker existed. Both are read as version 1.
    /// </summary>
    public string? ProtocolVersion { get; }

    /// <summary>
    /// Reads and validates the document's protocol marker. A major version other than the one this library
    /// implements is refused: the extensions may then carry meanings this reconstruction would misread, and
    /// a wrong client is worse than no client.
    /// </summary>
    private static string? ReadProtocolVersion(OpenApiDocument document)
    {
        var extensions = document.Info?.ExtensionData;
        if (extensions is null || !extensions.TryGetValue(OpenApiGenericsExtensions.Version, out var raw) || raw is null)
            return null;

        var version = raw.ToString()?.Trim();
        if (string.IsNullOrEmpty(version))
            return null;

        var major = version!.Split('.')[0];
        if (!string.Equals(major, OpenApiGenericsExtensions.CurrentVersion, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"The document declares Raptor21 generics protocol version '{version}', but this generator implements " +
                $"version {OpenApiGenericsExtensions.CurrentVersion}. Update the generator, or project the document with a matching " +
                "Raptor21.OpenApi.Generics.AspNetCore.");
        }

        return version;
    }

    /// <summary>Schema names that must not become generated models.</summary>
    public IReadOnlyCollection<string> ExcludedTypeNames => _excluded;

    /// <summary>Envelopes and containers found in the document, by schema name.</summary>
    public IReadOnlyDictionary<string, string> ReconstructedTypes { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The C# type for a schema, with generic contracts restored where the metadata allows it.
    /// </summary>
    public string ResolveType(JsonSchema? schema, string typeNameHint = "")
        => ResolveType(schema, typeNameHint, nullable: null);

    private string ResolveType(JsonSchema? schema, string typeNameHint, bool? nullable)
    {
        if (schema is null)
            return "object";

        var actual = schema.ActualSchema;

        if (_reconstructed.TryGetValue(actual, out var reconstructed))
            return reconstructed;

        // An array of envelopes still needs the element reconstructed.
        if (actual.Type.HasFlag(JsonObjectType.Array) && actual.Item is not null)
        {
            var item = actual.Item.ActualSchema;
            if (_reconstructed.TryGetValue(item, out var reconstructedItem))
                return _resolver.Collection(reconstructedItem);
        }

        return _resolver.Resolve(actual, nullable ?? actual.IsNullable(SchemaType.OpenApi3), typeNameHint);
    }

    private void Analyse()
    {
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (name, schema) in _document.Definitions)
        {
            if (schema is null)
                continue;

            if (ReadBool(schema, OpenApiGenericsExtensions.IgnoreModel))
                _excluded.Add(name);
        }

        // Two passes: containers first, so an envelope wrapping a container reconstructs the inner type too.
        foreach (var (name, schema) in _document.Definitions)
        {
            if (schema is null || !TryReconstructContainer(schema, out var type))
                continue;

            _reconstructed[schema.ActualSchema] = type;
            byName[name] = type;
        }

        foreach (var (name, schema) in _document.Definitions)
        {
            if (schema is null || !TryReconstructWrapper(schema, out var type))
                continue;

            _reconstructed[schema.ActualSchema] = type;
            byName[name] = type;
        }

        ReconstructedTypes = byName;
    }

    private bool TryReconstructWrapper(JsonSchema schema, out string type)
    {
        type = string.Empty;

        if (!ReadBool(schema, OpenApiGenericsExtensions.ApiWrapper))
            return false;

        var envelope = ReadString(schema, OpenApiGenericsExtensions.ApiWrapperType);
        if (string.IsNullOrWhiteSpace(envelope))
            return false;

        var payloadProperty = schema.ActualSchema.ActualProperties
            .FirstOrDefault(p => string.Equals(p.Key, _settings.PayloadPropertyName, StringComparison.OrdinalIgnoreCase));

        // An envelope whose payload property is missing is not one this document can describe; leaving it as a
        // plain generated model is better than emitting a type that will not compile.
        if (payloadProperty.Value is null)
            return false;

        // The payload's own nullability belongs to the envelope's property, not to the type argument. Carrying
        // it into the argument would put a '?' inside every signature for no gain. A nullable value type is a
        // different matter — there 'int?' says something 'int' does not, so it is left alone.
        var payloadType = ResolveType(payloadProperty.Value, payloadProperty.Key, nullable: false);
        if (IsReferenceLike(payloadProperty.Value))
            payloadType = payloadType.TrimEnd('?');

        type = $"{Map(envelope!)}<{payloadType}>";
        return true;
    }

    private bool TryReconstructContainer(JsonSchema schema, out string type)
    {
        type = string.Empty;

        var container = ReadString(schema, OpenApiGenericsExtensions.DataContainerType);
        if (string.IsNullOrWhiteSpace(container))
            return false;

        var itemName = ReadString(schema, OpenApiGenericsExtensions.DataItem);
        if (string.IsNullOrWhiteSpace(itemName))
            return false;

        if (!_document.Definitions.TryGetValue(itemName!, out var itemSchema) || itemSchema is null)
            return false;

        type = $"{Map(container!)}<{_resolver.Resolve(itemSchema.ActualSchema, false, itemName!)}>";
        return true;
    }

    /// <summary>
    /// Whether a schema lands on a C# reference type, where a trailing '?' is an annotation rather than part
    /// of the type's identity.
    /// </summary>
    private static bool IsReferenceLike(JsonSchema schema)
    {
        var type = schema.ActualSchema.Type;

        return type.HasFlag(JsonObjectType.Object)
               || type.HasFlag(JsonObjectType.Array)
               || type.HasFlag(JsonObjectType.String)
               || type == JsonObjectType.None;
    }

    private string Map(string projectedTypeName)
        => _settings.TypeMappings.TryGetValue(projectedTypeName, out var mapped) ? mapped : projectedTypeName;

    private static bool ReadBool(JsonSchema schema, string key)
    {
        var value = Read(schema, key);

        return value switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var parsed) && parsed,
            _ => false,
        };
    }

    private static string? ReadString(JsonSchema schema, string key)
        => Read(schema, key)?.ToString();

    private static object? Read(JsonSchema schema, string key)
    {
        var extensions = schema.ActualSchema.ExtensionData;
        if (extensions is not null && extensions.TryGetValue(key, out var value))
            return value;

        extensions = schema.ExtensionData;
        return extensions is not null && extensions.TryGetValue(key, out value) ? value : null;
    }
}

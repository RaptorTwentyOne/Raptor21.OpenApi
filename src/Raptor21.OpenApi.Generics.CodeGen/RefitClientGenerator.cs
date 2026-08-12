using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using NJsonSchema;
using NJsonSchema.CodeGeneration.CSharp;

using NSwag;

namespace Raptor21.OpenApi.Generics.CodeGen;

/// <summary>
/// Generates a Refit client from an OpenAPI document, restoring generic response contracts on the way.
/// </summary>
/// <remarks>
/// Models come from NJsonSchema, which already knows how to turn a JSON schema into a C# type and has done so
/// for years. What this class adds is the part NJsonSchema cannot know: which schemas are projected envelopes
/// that must not become models at all, and what generic type each of their references should become instead.
/// Transport is Refit's job, so nothing here emits HTTP code.
/// </remarks>
public sealed class RefitClientGenerator
{
    private readonly RefitClientGeneratorSettings _settings;

    /// <summary>Creates a generator.</summary>
    public RefitClientGenerator(RefitClientGeneratorSettings settings)
        => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <summary>Generates a single C# file containing the client interfaces and their models.</summary>
    public string Generate(OpenApiDocument document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));

        var csharpSettings = new CSharpGeneratorSettings
        {
            Namespace = _settings.Namespace,
            ClassStyle = CSharpClassStyle.Poco,
            JsonLibrary = CSharpJsonLibrary.SystemTextJson,
            GenerateDataAnnotations = false,
            GenerateOptionalPropertiesAsNullable = true,
            GenerateNullableReferenceTypes = true,
            RequiredPropertiesMustBeDefined = false,
            GenerateJsonMethods = false,
            SchemaType = SchemaType.OpenApi3,
            // date-time maps to DateTime rather than the DateTimeOffset NJsonSchema prefers. Services that
            // serialise an unzoned DateTime send values a DateTimeOffset cannot always represent — most
            // sharply DateTime.MinValue, which shifts below DateTimeOffset.MinValue under any positive local
            // offset and fails to parse. DateTime accepts everything such a service can send.
            DateTimeType = "System.DateTime",
        };

        var resolver = new CSharpTypeResolver(csharpSettings);
        resolver.RegisterSchemaDefinitions(document.Definitions);

        var reconstruction = new GenericsReconstruction(document, _settings, resolver);
        csharpSettings.ExcludedTypeNames = reconstruction.ExcludedTypeNames.ToArray();

        var models = GenerateModels(document, csharpSettings, resolver);
        var interfaces = GenerateInterfaces(document, reconstruction, out var interfaceNames);
        var registration = _settings.GenerateDependencyInjection
            ? GenerateRegistration(interfaceNames)
            : string.Empty;

        return Compose(interfaces, registration, models);
    }

    /// <summary>
    /// NJsonSchema generates from a schema root, so the document's definitions are hung off a synthetic one.
    /// Generating the artifacts rather than a whole file keeps the output as bare declarations, which is what
    /// gets composed into the single file this generator emits.
    /// </summary>
    private static string GenerateModels(OpenApiDocument document, CSharpGeneratorSettings settings, CSharpTypeResolver resolver)
    {
        var root = new JsonSchema();

        foreach (var definition in document.Definitions)
            root.Definitions[definition.Key] = definition.Value;

        var artifacts = new CSharpGenerator(root, settings, resolver).GenerateTypes();

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            artifacts.Select(a => a.Code.TrimEnd()));
    }

    /// <summary>
    /// Emits the registration extension. Each client is added through a local generic helper so the interface
    /// types are named in code, which is what keeps the result usable under trimming.
    /// </summary>
    private string GenerateRegistration(IReadOnlyCollection<string> interfaceNames)
    {
        var builder = new StringBuilder();

        builder.AppendLine("/// <summary>Registers every client in this document as a Refit client.</summary>");
        builder.AppendLine($"public static class {Pascal(_settings.RegistrationMethodName)}Extensions");
        builder.AppendLine("{");
        builder.AppendLine("    /// <summary>Adds each generated interface, letting the caller configure every client the same way.</summary>");
        builder.AppendLine("    /// <param name=\"services\">The service collection.</param>");
        builder.AppendLine("    /// <param name=\"configure\">Applied to every client's builder.</param>");
        builder.AppendLine("    /// <param name=\"settings\">");
        builder.AppendLine("    /// Refit settings shared by every client. Worth setting: a serializer that omits nulls. Optional");
        builder.AppendLine("    /// properties are generated nullable, and a request body carrying explicit nulls is rejected by a");
        builder.AppendLine("    /// server whose own model declares those members non-nullable.");
        builder.AppendLine("    /// </param>");
        builder.AppendLine($"    public static IServiceCollection {_settings.RegistrationMethodName}(this IServiceCollection services, Action<IHttpClientBuilder>? configure = null, RefitSettings? settings = null)");
        builder.AppendLine("    {");

        foreach (var name in interfaceNames.OrderBy(n => n, StringComparer.Ordinal))
            builder.AppendLine($"        Add<{name}>(services, configure, settings);");

        builder.AppendLine();
        builder.AppendLine("        return services;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    // AddRefitGeneratedClient, not AddRefitClient: it binds the interface to the implementation");
        builder.AppendLine("    // Refit's source generator already emitted. The reflection-based path would need an extra");
        builder.AppendLine("    // package and does not survive trimming, which rules it out on mobile.");
        builder.AppendLine("    private static void Add<TApi>(IServiceCollection services, Action<IHttpClientBuilder>? configure, RefitSettings? settings)");
        builder.AppendLine("        where TApi : class");
        builder.AppendLine("    {");
        builder.AppendLine("        var builder = settings is null");
        builder.AppendLine("            ? services.AddRefitGeneratedClient<TApi>()");
        builder.AppendLine("            : services.AddRefitGeneratedClient<TApi>(settings);");
        builder.AppendLine();
        builder.AppendLine("        configure?.Invoke(builder);");
        builder.AppendLine("    }");
        builder.AppendLine("}");

        return builder.ToString();
    }

    private string Compose(string interfaces, string registration, string models)
    {
        var builder = new StringBuilder();

        builder.AppendLine("// <auto-generated>");
        builder.AppendLine("//     Generated by Raptor21.OpenApi.Generics.");
        builder.AppendLine("//     Generic response contracts are bound to their owning types rather than redefined here.");
        builder.AppendLine("//     Changes to this file will be lost the next time it is generated.");
        builder.AppendLine("// </auto-generated>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using System.Threading;");
        builder.AppendLine("using System.Threading.Tasks;");
        builder.AppendLine("using Refit;");

        if (_settings.GenerateDependencyInjection)
            builder.AppendLine("using Microsoft.Extensions.DependencyInjection;");

        foreach (var ns in _settings.AdditionalNamespaces)
            builder.AppendLine($"using {ns};");

        builder.AppendLine();
        builder.AppendLine($"namespace {_settings.Namespace};");
        builder.AppendLine();
        builder.Append(interfaces);
        builder.AppendLine();

        if (registration.Length > 0)
        {
            builder.Append(registration);
            builder.AppendLine();
        }

        builder.AppendLine(models);

        return builder.ToString();
    }

    private string GenerateInterfaces(OpenApiDocument document, GenericsReconstruction reconstruction, out IReadOnlyCollection<string> interfaceNames)
    {
        var operations = document.Operations
            .Where(o => !IsExcluded(o))
            .ToList();

        var groups = _settings.Grouping == InterfaceGrouping.Single
            ? new[] { new { Name = _settings.SingleInterfaceName, Operations = (IEnumerable<OpenApiOperationDescription>)operations } }
            : operations
                .GroupBy(PrimaryTag)
                .Select(g => new { Name = "I" + Pascal(g.Key) + "Api", Operations = (IEnumerable<OpenApiOperationDescription>)g })
                .ToArray();

        var builder = new StringBuilder();
        var names = new List<string>();

        foreach (var group in groups.OrderBy(g => g.Name, StringComparer.Ordinal))
        {
            names.Add(group.Name);
            builder.AppendLine($"public interface {group.Name}");
            builder.AppendLine("{");

            var used = new HashSet<string>(StringComparer.Ordinal);
            var first = true;

            foreach (var operation in group.Operations.OrderBy(o => o.Path, StringComparer.Ordinal).ThenBy(o => o.Method.ToString(), StringComparer.Ordinal))
            {
                if (!first) builder.AppendLine();
                first = false;

                AppendMethod(builder, operation, reconstruction, used);
            }

            builder.AppendLine("}");
            builder.AppendLine();
        }

        interfaceNames = names;
        return builder.ToString();
    }

    private void AppendMethod(
        StringBuilder builder,
        OpenApiOperationDescription description,
        GenericsReconstruction reconstruction,
        HashSet<string> usedNames)
    {
        var operation = description.Operation;
        var multipart = IsMultipart(operation);

        if (_settings.GenerateXmlDocComments)
        {
            var summary = operation.Summary ?? operation.Description;
            if (!string.IsNullOrWhiteSpace(summary))
            {
                builder.AppendLine("    /// <summary>");
                foreach (var line in summary!.Replace("\r\n", "\n").Split('\n'))
                    builder.AppendLine($"    /// {System.Security.SecurityElement.Escape(line.Trim())}");
                builder.AppendLine("    /// </summary>");
            }
        }

        if (multipart)
            builder.AppendLine("    [Multipart]");

        builder.AppendLine($"    [{Pascal(description.Method.ToString())}(\"{Route(description.Path)}\")]");

        var returnType = ResolveReturnType(operation, reconstruction);
        var name = UniqueMethodName(description, usedNames);
        var parameters = BuildParameters(operation, reconstruction, multipart);

        builder.AppendLine($"    {returnType} {name}({string.Join(", ", parameters)});");
    }

    private List<string> BuildParameters(OpenApiOperation operation, GenericsReconstruction reconstruction, bool multipart)
    {
        var parameters = new List<string>();
        var optional = new List<string>();

        foreach (var parameter in operation.ActualParameters)
        {
            if (parameter.Kind == OpenApiParameterKind.Header && IsExcludedHeader(parameter.Name))
                continue;

            // Refit builds a multipart request from individual parts, so the body schema is spread across
            // parameters rather than passed as one object. The object would have no generated type anyway —
            // an inline request body never reaches the document's component schemas.
            if (multipart && parameter.Kind == OpenApiParameterKind.Body)
            {
                AppendMultipartParts(parameter, reconstruction, parameters, optional);
                continue;
            }

            var type = reconstruction.ResolveType(parameter.Schema ?? parameter.ActualSchema, parameter.Name);
            var identifier = Camel(parameter.Name);

            var rendered = parameter.Kind switch
            {
                OpenApiParameterKind.Path => $"{type} {identifier}",
                OpenApiParameterKind.Query => $"[Query] {type} {identifier}",
                OpenApiParameterKind.Header => $"[Header(\"{parameter.Name}\")] {type} {identifier}",
                OpenApiParameterKind.Body => $"[Body] {type} {identifier}",
                OpenApiParameterKind.FormData => $"[AliasAs(\"{parameter.Name}\")] {type} {identifier}",
                _ => $"{type} {identifier}",
            };

            if (parameter.IsRequired || parameter.Kind == OpenApiParameterKind.Path)
                parameters.Add(rendered);
            else
                optional.Add(rendered);
        }

        parameters.AddRange(optional);

        if (_settings.GenerateCancellationTokens)
            parameters.Add("CancellationToken cancellationToken = default");

        return parameters;
    }

    private string Route(string path)
    {
        if (string.IsNullOrWhiteSpace(_settings.PathPrefix))
            return path;

        var prefix = "/" + _settings.PathPrefix!.Trim('/');
        return path.StartsWith("/", StringComparison.Ordinal) ? prefix + path : prefix + "/" + path;
    }

    private static bool IsMultipart(OpenApiOperation operation)
        => operation.RequestBody?.Content is { Count: > 0 } content
           && content.Keys.Any(k => k.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Turns a multipart request body into Refit parts: a file field becomes a <c>StreamPart</c>, everything
    /// else becomes an aliased scalar parameter.
    /// </summary>
    private void AppendMultipartParts(
        OpenApiParameter body,
        GenericsReconstruction reconstruction,
        List<string> required,
        List<string> optional)
    {
        var schema = body.ActualSchema;

        foreach (var property in schema.ActualProperties)
        {
            var identifier = Camel(property.Key);
            var isFile = property.Value.ActualSchema.Format == "binary";

            var rendered = isFile
                ? $"StreamPart {identifier}"
                : $"[AliasAs(\"{property.Key}\")] {reconstruction.ResolveType(property.Value, property.Key)} {identifier}";

            if (property.Value.IsRequired || schema.RequiredProperties.Contains(property.Key))
                required.Add(rendered);
            else
                optional.Add(rendered);
        }
    }

    private string ResolveReturnType(OpenApiOperation operation, GenericsReconstruction reconstruction)
    {
        var success = operation.ActualResponses
            .Where(r => r.Key.StartsWith("2", StringComparison.Ordinal))
            .OrderBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => r.Value)
            .FirstOrDefault();

        var schema = SelectResponseSchema(success);

        return schema is null ? "Task" : $"Task<{reconstruction.ResolveType(schema)}>";
    }

    /// <summary>
    /// Picks the schema for the media type the client will actually receive.
    /// </summary>
    /// <remarks>
    /// An action without <c>[Produces]</c> is documented under several media types at once — commonly
    /// <c>text/plain</c>, <c>application/json</c> and <c>text/json</c> — and they need not agree, because
    /// document-shaping filters legitimately target one of them. Taking whichever happens to come first would
    /// make generation depend on dictionary order, so JSON is chosen deliberately.
    /// </remarks>
    private static JsonSchema? SelectResponseSchema(OpenApiResponse? response)
    {
        if (response?.Content is { Count: > 0 } content)
        {
            if (content.TryGetValue("application/json", out var json) && json.Schema is not null)
                return json.Schema;

            var structuredJson = content
                .Where(c => c.Key.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Value.Schema)
                .FirstOrDefault(s => s is not null);

            if (structuredJson is not null)
                return structuredJson;
        }

        return response?.Schema;
    }

    private bool IsExcluded(OpenApiOperationDescription description)
        => description.Operation.Tags is { Count: > 0 }
           && description.Operation.Tags.Any(t => _settings.ExcludedTags.Contains(t));

    private bool IsExcludedHeader(string name)
        => _settings.ExcludeAllHeaderParameters || _settings.ExcludedHeaderParameters.Contains(name);

    private static string PrimaryTag(OpenApiOperationDescription description)
        => description.Operation.Tags is { Count: > 0 } tags ? tags[0] : "Default";

    /// <summary>
    /// Picks a method name that is unique within its interface.
    /// </summary>
    /// <remarks>
    /// Candidates are tried from most to least natural: the document's operationId, then the route's action
    /// segment, then the same prefixed by the HTTP verb, then qualified by the parameters it takes. Two
    /// operations on one route — a GET and a DELETE of the same resource — resolve at the verb step. The
    /// numeric suffix at the end is a guarantee of uniqueness rather than a name anyone should see; reaching it
    /// means the document could not distinguish the operations, and giving them operationIds is the fix.
    /// </remarks>
    private string UniqueMethodName(OpenApiOperationDescription description, HashSet<string> used)
    {
        string? fallback = null;

        foreach (var candidate in NameCandidates(description))
        {
            fallback ??= candidate;

            if (used.Add(candidate))
                return candidate;
        }

        var baseName = fallback ?? "Invoke";
        var index = 2;

        while (!used.Add(baseName + index.ToString(CultureInfo.InvariantCulture)))
            index++;

        return baseName + index.ToString(CultureInfo.InvariantCulture);
    }

    private static IEnumerable<string> NameCandidates(OpenApiOperationDescription description)
    {
        if (!string.IsNullOrWhiteSpace(description.Operation.OperationId))
            yield return Pascal(description.Operation.OperationId!);

        var fallback = FallbackName(description);
        yield return fallback;

        var verb = Pascal(description.Method.ToString());
        var verbPrefixed = fallback.StartsWith(verb, StringComparison.Ordinal) ? null : verb + fallback;

        if (verbPrefixed is not null)
            yield return verbPrefixed;

        var arguments = description.Operation.ActualParameters
            .Where(p => p.Kind == OpenApiParameterKind.Path || p.Kind == OpenApiParameterKind.Query)
            .Select(p => Pascal(p.Name))
            .ToArray();

        if (arguments.Length == 0)
            yield break;

        var qualifier = "By" + string.Join("And", arguments);

        yield return fallback + qualifier;

        if (verbPrefixed is not null)
            yield return verbPrefixed + qualifier;
    }

    /// <summary>
    /// Builds a method name from the route.
    /// </summary>
    /// <remarks>
    /// A route like <c>/Languages/GetLanguages</c> already names the action, so prefixing the HTTP verb again
    /// would produce <c>GetLanguagesGetLanguages</c>. When the route has a controller segment and an action
    /// segment, the action segment alone is the name. A route that is only a resource — <c>/Countries</c> —
    /// carries no verb of its own, so there the HTTP method supplies it.
    /// </remarks>
    private static string FallbackName(OpenApiOperationDescription description)
    {
        var segments = description.Path
            .Split(['/'], StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !s.StartsWith("{", StringComparison.Ordinal))
            .Select(Pascal)
            .Where(s => s.Length > 0)
            .ToArray();

        if (segments.Length >= 2)
            return segments[^1];

        var resource = segments.Length == 1 ? segments[0] : string.Empty;
        return Pascal(description.Method.ToString()) + resource;
    }

    private static string Pascal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var parts = value.Split(['_', '-', ' ', '.', '/'], StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder();

        foreach (var part in parts)
        {
            var cleaned = new string(part.Where(char.IsLetterOrDigit).ToArray());
            if (cleaned.Length == 0)
                continue;

            builder.Append(char.ToUpper(cleaned[0], CultureInfo.InvariantCulture));
            builder.Append(cleaned.Substring(1));
        }

        return builder.ToString();
    }

    private static string Camel(string value)
    {
        var pascal = Pascal(value);
        if (pascal.Length == 0)
            return "value";

        var camel = char.ToLower(pascal[0], CultureInfo.InvariantCulture) + pascal.Substring(1);
        return Keywords.Contains(camel) ? "@" + camel : camel;
    }

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit",
        "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
        "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out",
        "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
        "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };
}

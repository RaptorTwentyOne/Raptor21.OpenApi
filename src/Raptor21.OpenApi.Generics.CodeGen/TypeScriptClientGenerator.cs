using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using NJsonSchema;
using NJsonSchema.CodeGeneration.TypeScript;

using NSwag;

using Scriban;
using Scriban.Runtime;

namespace Raptor21.OpenApi.Generics.CodeGen;

/// <summary>The three files a TypeScript generation run produces. <see cref="Queries"/> is null unless requested.</summary>
public sealed class TypeScriptClientOutput
{
    /// <summary>Creates the output set.</summary>
    public TypeScriptClientOutput(string types, string client, string? queries)
    {
        Types = types;
        Client = client;
        Queries = queries;
    }

    /// <summary><c>types.ts</c>: DTOs, string-union enums, and the reconstructed generic envelopes.</summary>
    public string Types { get; }

    /// <summary><c>client.ts</c>: a fetch-based client grouped into classes, unwrapping envelopes to payloads.</summary>
    public string Client { get; }

    /// <summary><c>queries.ts</c>: TanStack Query hooks. Null when queries were not requested.</summary>
    public string? Queries { get; }
}

/// <summary>
/// Generates a fetch-based TypeScript client from an OpenAPI document, restoring generic response contracts
/// as real TypeScript generics. The sibling of <see cref="RefitClientGenerator"/>: it shares the same
/// reconstruction and name derivation, and differs only in the language it targets.
/// </summary>
/// <remarks>
/// Hybrid emission: Scriban templates carry each file's scaffold (the auto-generated banner, the client's
/// fetch plumbing, the queries file's imports), while the type-bearing parts — model declarations, envelope
/// interfaces, method bodies — are built in code from the NJsonSchema TypeScript resolver, which is the only
/// component that knows how a schema becomes a TypeScript type.
/// </remarks>
public sealed class TypeScriptClientGenerator
{
    private readonly TypeScriptClientGeneratorSettings _settings;

    private readonly HashSet<string> _envelopeNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _containerNames = new(StringComparer.Ordinal);
    private string _payloadKey = "data";

    /// <summary>Creates a generator.</summary>
    public TypeScriptClientGenerator(TypeScriptClientGeneratorSettings settings)
        => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <summary>Generates <c>types.ts</c>, <c>client.ts</c> and optionally <c>queries.ts</c>.</summary>
    public TypeScriptClientOutput Generate(OpenApiDocument document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));

        var tsSettings = new TypeScriptGeneratorSettings
        {
            TypeStyle = TypeScriptTypeStyle.Interface,
            EnumStyle = TypeScriptEnumStyle.StringLiteral,
            NullValue = TypeScriptNullValue.Null,
            // >= 2.0 turns on strict-null-check aware output, so nullable schemas render as `T | null` rather
            // than swallowing the distinction.
            TypeScriptVersion = 4.3m,
            MarkOptionalProperties = true,
            // A fetch client hands back parsed JSON; dates arrive as ISO strings, so binding them to `string`
            // is honest and adds no runtime date library to the consumer.
            DateTimeType = TypeScriptDateTimeType.String,
            SchemaType = SchemaType.OpenApi3,
            Namespace = string.Empty,
        };

        var resolver = new TypeScriptTypeResolver(tsSettings);
        resolver.RegisterSchemaDefinitions(document.Definitions);

        SeedDefaultTypeMappings(document);

        var reconstruction = new GenericsReconstruction(document, _settings, new TypeScriptTypeResolverAdapter(resolver));
        tsSettings.ExcludedTypeNames = reconstruction.ExcludedTypeNames.ToArray();

        var envelopes = BuildGenericInterfaces(document, resolver);
        var models = BuildModels(document, tsSettings, resolver);
        var types = RenderTypes(envelopes, models);

        var groups = BuildGroups(document, reconstruction);
        var exported = ExportedTypeNames(document, reconstruction);

        var client = RenderClient(groups, exported);
        var queries = _settings.GenerateQueries ? RenderQueries(groups, exported) : null;

        return new TypeScriptClientOutput(types, client, queries);
    }

    // ---- generic contract reconstruction (envelopes + containers) ----

    private void SeedDefaultTypeMappings(OpenApiDocument document)
    {
        foreach (var schema in document.Definitions.Values)
        {
            if (schema is null)
                continue;

            var wrapper = ExtString(schema, OpenApiGenericsExtensions.ApiWrapperType);
            if (!string.IsNullOrWhiteSpace(wrapper) && !_settings.TypeMappings.ContainsKey(wrapper!))
                _settings.TypeMappings[wrapper!] = LastSegment(wrapper!);

            var container = ExtString(schema, OpenApiGenericsExtensions.DataContainerType);
            if (!string.IsNullOrWhiteSpace(container) && !_settings.TypeMappings.ContainsKey(container!))
                _settings.TypeMappings[container!] = LastSegment(container!);
        }
    }

    private string Map(string projectedTypeName)
        => _settings.TypeMappings.TryGetValue(projectedTypeName, out var mapped) ? mapped : LastSegment(projectedTypeName);

    /// <summary>
    /// Builds the generic interfaces the reconstructed usages refer to. An envelope becomes
    /// <c>BaseResponse&lt;T&gt;</c> with its payload property generalised to <c>T</c>; a container becomes
    /// <c>Page&lt;T&gt;</c> with the item-bearing property generalised. Each distinct name is emitted once.
    /// </summary>
    private string BuildGenericInterfaces(OpenApiDocument document, TypeScriptTypeResolver resolver)
    {
        var blocks = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, schema) in document.Definitions)
        {
            if (schema is null || !ExtBool(schema, OpenApiGenericsExtensions.ApiWrapper))
                continue;

            var full = ExtString(schema, OpenApiGenericsExtensions.ApiWrapperType);
            if (string.IsNullOrWhiteSpace(full))
                continue;

            var name = Map(full!);
            _envelopeNames.Add(name);
            if (!seen.Add(name))
                continue;

            blocks.Add(BuildEnvelopeInterface(name, schema, resolver));
        }

        foreach (var (_, schema) in document.Definitions)
        {
            if (schema is null)
                continue;

            var full = ExtString(schema, OpenApiGenericsExtensions.DataContainerType);
            if (string.IsNullOrWhiteSpace(full))
                continue;

            var itemName = ExtString(schema, OpenApiGenericsExtensions.DataItem);
            if (string.IsNullOrWhiteSpace(itemName)
                || !document.Definitions.TryGetValue(itemName!, out var itemSchema)
                || itemSchema is null)
                continue;

            var name = Map(full!);
            _containerNames.Add(name);
            if (!seen.Add(name))
                continue;

            blocks.Add(BuildContainerInterface(name, schema, itemSchema, resolver));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, blocks);
    }

    private string BuildEnvelopeInterface(string name, JsonSchema schema, TypeScriptTypeResolver resolver)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"/** Response envelope. Restored as a generic; the flattened per-payload copies are not emitted. */");
        builder.AppendLine($"export interface {name}<T> {{");

        foreach (var property in schema.ActualSchema.ActualProperties)
        {
            var isPayload = string.Equals(property.Key, _settings.PayloadPropertyName, StringComparison.OrdinalIgnoreCase);
            if (isPayload)
                _payloadKey = property.Key;

            var type = isPayload ? "T" : resolver.Resolve(property.Value.ActualSchema, false, property.Key);
            builder.AppendLine("    " + RenderProperty(property, type, schema.ActualSchema));
        }

        builder.Append("}");
        return builder.ToString();
    }

    private string BuildContainerInterface(string name, JsonSchema schema, JsonSchema itemSchema, TypeScriptTypeResolver resolver)
    {
        var itemType = resolver.Resolve(itemSchema.ActualSchema, false, name);

        var builder = new StringBuilder();
        builder.AppendLine($"/** Payload container. Restored as a generic; the flattened per-item copies are not emitted. */");
        builder.AppendLine($"export interface {name}<T> {{");

        foreach (var property in schema.ActualSchema.ActualProperties)
        {
            var actual = property.Value.ActualSchema;
            string type;

            if (actual.Type.HasFlag(JsonObjectType.Array) && actual.Item is not null
                && string.Equals(resolver.Resolve(actual.Item.ActualSchema, false, property.Key), itemType, StringComparison.Ordinal))
            {
                type = "T[]";
            }
            else if (string.Equals(resolver.Resolve(actual, false, property.Key), itemType, StringComparison.Ordinal))
            {
                type = "T";
            }
            else
            {
                type = resolver.Resolve(actual, false, property.Key);
            }

            builder.AppendLine("    " + RenderProperty(property, type, schema.ActualSchema));
        }

        builder.Append("}");
        return builder.ToString();
    }

    private static string RenderProperty(KeyValuePair<string, JsonSchemaProperty> property, string type, JsonSchema owner)
    {
        var name = property.Key;
        var required = property.Value.IsRequired || owner.RequiredProperties.Contains(name);
        var nullable = property.Value.IsNullable(SchemaType.OpenApi3);
        var readOnly = property.Value.IsReadOnly;

        var prefix = readOnly ? "readonly " : string.Empty;
        var optional = required ? string.Empty : "?";
        var suffix = nullable ? " | null" : string.Empty;

        return $"{prefix}{name}{optional}: {type}{suffix};";
    }

    private static string BuildModels(OpenApiDocument document, TypeScriptGeneratorSettings settings, TypeScriptTypeResolver resolver)
    {
        var root = new JsonSchema();
        foreach (var definition in document.Definitions)
            root.Definitions[definition.Key] = definition.Value;

        var artifacts = new TypeScriptGenerator(root, settings, resolver).GenerateTypes();
        return string.Join(Environment.NewLine + Environment.NewLine, artifacts.Select(a => a.Code.TrimEnd()));
    }

    // ---- method model shared by client.ts and queries.ts ----

    private sealed class TsParam
    {
        public string Name = string.Empty;
        public string WireName = string.Empty;
        public string Type = string.Empty;
        public bool Optional;
        public OpenApiParameterKind Kind;
        /// <summary>A multipart field carrying a file (or a list of files): sent as a Blob, never stringified.</summary>
        public bool IsFile;
        /// <summary>A multipart field whose schema is an array: appended once per element.</summary>
        public bool IsArray;
    }

    private sealed class TsMethod
    {
        public string Name = string.Empty;
        public string HttpMethod = string.Empty;
        public string Path = string.Empty;
        public string? Summary;
        public string Tag = string.Empty;
        public List<TsParam> Parameters = new();
        public string ReturnType = "void";
        public bool ReturnsVoid;
        public bool IsEnvelope;
        public string EnvelopeType = string.Empty;
        public TsParam? Body;
        public bool IsQuery;
        /// <summary>The request body is multipart/form-data, sent as a FormData built from the form-field parameters.</summary>
        public bool IsMultipart;

        /// <summary>
        /// The name this operation's hook is exported under in queries.ts. Method names are unique
        /// only inside their own client class, but queries.ts emits every hook as a flat module-level
        /// function, so the name must be unique across the whole document. Set by
        /// <see cref="AssignHookNames"/>.
        /// </summary>
        public string HookName = string.Empty;

        public List<TsParam> Signature =>
            Parameters.Where(p => !p.Optional).Concat(Parameters.Where(p => p.Optional)).ToList();
    }

    private sealed class TsGroup
    {
        public string ClassName = string.Empty;
        public string KeysName = string.Empty;
        public string InstanceName = string.Empty;
        public string Tag = string.Empty;
        public List<TsMethod> Methods = new();
    }

    private List<TsGroup> BuildGroups(OpenApiDocument document, GenericsReconstruction reconstruction)
    {
        var operations = document.Operations
            .Where(o => !IsExcluded(o))
            .ToList();

        IEnumerable<IGrouping<string, OpenApiOperationDescription>> grouped = _settings.Grouping == InterfaceGrouping.Single
            ? new[] { new Grouping(_settings.SingleClientName, operations) }
            : operations.GroupBy(PrimaryTag);

        var groups = new List<TsGroup>();

        foreach (var g in grouped.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var className = _settings.Grouping == InterfaceGrouping.Single
                ? _settings.SingleClientName
                : Names.Pascal(g.Key) + "Api";

            var group = new TsGroup
            {
                Tag = g.Key,
                ClassName = className,
                InstanceName = Names.Camel(className, IdentifierEscaper.TypeScript),
                KeysName = Names.Camel(className.EndsWith("Api", StringComparison.Ordinal) ? className[..^3] : className, IdentifierEscaper.TypeScript) + "Keys",
            };

            var used = new HashSet<string>(StringComparer.Ordinal);

            foreach (var operation in g.OrderBy(o => o.Path, StringComparer.Ordinal).ThenBy(o => o.Method.ToString(), StringComparer.Ordinal))
                group.Methods.Add(BuildMethod(operation, reconstruction, used));

            groups.Add(group);
        }

        return groups;
    }

    private TsMethod BuildMethod(OpenApiOperationDescription description, GenericsReconstruction reconstruction, HashSet<string> used)
    {
        var operation = description.Operation;

        var method = new TsMethod
        {
            Name = Names.Camel(Names.UniqueMethodName(description, used), IdentifierEscaper.TypeScript),
            HttpMethod = description.Method.ToString().ToUpperInvariant(),
            Path = Route(description.Path),
            Summary = operation.Summary ?? operation.Description,
            Tag = PrimaryTag(description),
            IsQuery = string.Equals(description.Method.ToString(), "Get", StringComparison.OrdinalIgnoreCase),
        };

        var multipart = IsMultipart(operation);

        foreach (var parameter in operation.ActualParameters)
        {
            if (parameter.Kind == OpenApiParameterKind.Header && IsExcludedHeader(parameter.Name))
                continue;

            if (parameter.Kind == OpenApiParameterKind.Body)
            {
                // A multipart body goes over the wire as FormData built from its fields, so the schema is spread
                // across parameters. The inline object would have no generated type anyway: an inline request
                // body never reaches the document's component schemas.
                if (multipart)
                {
                    AppendMultipartFields(parameter, reconstruction, method);
                    continue;
                }

                var body = new TsParam
                {
                    Name = Names.Camel(_settings.BodyParameterName, IdentifierEscaper.TypeScript),
                    WireName = parameter.Name,
                    Type = reconstruction.ResolveType(parameter.Schema ?? parameter.ActualSchema, parameter.Name),
                    Optional = !(parameter.IsRequired || _settings.BodyParameterRequired),
                    Kind = OpenApiParameterKind.Body,
                };

                method.Body = body;
                method.Parameters.Add(body);
                continue;
            }

            if (parameter.Kind == OpenApiParameterKind.FormData)
            {
                // Swagger 2.0 spells multipart as individual formData parameters; same wire shape, same FormData.
                method.IsMultipart = true;
                method.Parameters.Add(FormField(parameter.Name, parameter.Schema ?? parameter.ActualSchema, parameter.IsRequired, reconstruction));
                continue;
            }

            method.Parameters.Add(new TsParam
            {
                Name = Names.Camel(parameter.Name, IdentifierEscaper.TypeScript),
                WireName = parameter.Name,
                Type = reconstruction.ResolveType(parameter.Schema ?? parameter.ActualSchema, parameter.Name),
                Optional = !(parameter.IsRequired || parameter.Kind == OpenApiParameterKind.Path),
                Kind = parameter.Kind,
            });
        }

        // The body's configured name must not shadow a path, query or header parameter of the same operation.
        if (method.Body is not null
            && method.Parameters.Any(p => !ReferenceEquals(p, method.Body) && string.Equals(p.Name, method.Body.Name, StringComparison.Ordinal)))
        {
            method.Body.Name = Names.Camel(_settings.BodyParameterName + "Body", IdentifierEscaper.TypeScript);
        }

        var success = operation.ActualResponses
            .Where(r => r.Key.StartsWith("2", StringComparison.Ordinal))
            .OrderBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => r.Value)
            .FirstOrDefault();

        var schema = SelectResponseSchema(success);

        if (schema is null)
        {
            method.ReturnsVoid = true;
            method.ReturnType = "void";
            return method;
        }

        var full = reconstruction.ResolveType(schema);
        method.ReturnType = full;

        foreach (var envelope in _envelopeNames)
        {
            if (full.StartsWith(envelope + "<", StringComparison.Ordinal) && full.EndsWith(">", StringComparison.Ordinal))
            {
                method.IsEnvelope = true;
                method.EnvelopeType = full;
                method.ReturnType = full.Substring(envelope.Length + 1, full.Length - envelope.Length - 2);
                break;
            }
        }

        return method;
    }

    // ---- client.ts ----

    private string RenderClient(List<TsGroup> groups, IReadOnlyCollection<string> exported)
    {
        var classes = new StringBuilder();

        foreach (var group in groups)
        {
            classes.AppendLine($"export class {group.ClassName} extends ApiClientBase {{");

            var first = true;
            foreach (var method in group.Methods)
            {
                if (!first) classes.AppendLine();
                first = false;
                classes.Append(RenderMethod(method));
            }

            classes.AppendLine("}");
            classes.AppendLine();
        }

        var body = classes.ToString().TrimEnd() + Environment.NewLine;
        var imports = RenderTypeImports(body, exported);

        var model = new ScriptObject();
        model["imports"] = imports;
        model["classes"] = body;
        return Render("client.ts.scriban", model);
    }

    private string RenderMethod(TsMethod method)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(method.Summary))
            builder.AppendLine("  /** " + method.Summary!.Replace("\r\n", " ").Replace("\n", " ").Trim() + " */");

        var signature = string.Join(", ", method.Signature.Select(p => $"{p.Name}{(p.Optional ? "?" : string.Empty)}: {p.Type}"));
        builder.AppendLine($"  async {method.Name}({signature}): Promise<{method.ReturnType}> {{");

        builder.AppendLine("    const url = " + UrlExpression(method) + ";");

        var init = new List<string> { $"method: '{method.HttpMethod}'", "credentials: 'include'" };

        var headers = method.Parameters
            .Where(p => p.Kind == OpenApiParameterKind.Header)
            .Select(p => $"'{p.WireName}': String({p.Name})")
            .ToList();

        // A multipart request must not name its Content-Type: the browser writes the header itself, with the
        // boundary it chose for the FormData, and a hand-written value would lack that boundary.
        if (method.Body is not null && !method.IsMultipart)
            headers.Insert(0, "'Content-Type': 'application/json'");

        if (headers.Count > 0)
            init.Add("headers: { " + string.Join(", ", headers) + " }");

        if (method.IsMultipart)
        {
            builder.Append(RenderFormData(method));
            init.Add("body: $form");
        }
        else if (method.Body is not null)
        {
            init.Add($"body: JSON.stringify({method.Body.Name})");
        }

        var fetchCall = "this.$http(url, { " + string.Join(", ", init) + " })";

        // Internal locals are $-prefixed: generated parameter names come from Names.Camel, which never emits a
        // leading '$', so a body parameter named 'body' cannot collide with the response local.
        if (method.ReturnsVoid)
        {
            builder.AppendLine("    await " + fetchCall + ";");
        }
        else if (method.IsEnvelope)
        {
            builder.AppendLine("    const $response = await " + fetchCall + ";");
            builder.AppendLine($"    const $envelope = await $response.json() as {method.EnvelopeType};");
            builder.AppendLine($"    return $envelope.{_payloadKey} as {method.ReturnType};");
        }
        else
        {
            builder.AppendLine("    const $response = await " + fetchCall + ";");
            builder.AppendLine($"    return await $response.json() as {method.ReturnType};");
        }

        builder.AppendLine("  }");
        return builder.ToString();
    }

    private static bool IsMultipart(OpenApiOperation operation)
        => operation.RequestBody?.Content is { Count: > 0 } content
           && content.Keys.Any(k => k.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase));

    /// <summary>Spreads a multipart body schema into one form-field parameter per property.</summary>
    private static void AppendMultipartFields(OpenApiParameter body, GenericsReconstruction reconstruction, TsMethod method)
    {
        method.IsMultipart = true;
        var schema = body.ActualSchema;

        foreach (var property in schema.ActualProperties)
        {
            var required = property.Value.IsRequired || schema.RequiredProperties.Contains(property.Key);
            method.Parameters.Add(FormField(property.Key, property.Value, required, reconstruction));
        }
    }

    /// <summary>
    /// A form field: binary (or a list of binaries) is typed <c>Blob</c> so a File or Blob can be appended as-is;
    /// anything else keeps its schema type and is stringified on append, which is how FormData carries scalars.
    /// </summary>
    private static TsParam FormField(string name, JsonSchema schema, bool required, GenericsReconstruction reconstruction)
    {
        var actual = schema.ActualSchema;
        var isArray = actual.Type.HasFlag(JsonObjectType.Array);
        var isFile = actual.Format == "binary" || (isArray && actual.Item?.ActualSchema.Format == "binary");

        return new TsParam
        {
            Name = Names.Camel(name, IdentifierEscaper.TypeScript),
            WireName = name,
            Type = isFile ? (isArray ? "Blob[]" : "Blob") : reconstruction.ResolveType(schema, name),
            Optional = !required,
            Kind = OpenApiParameterKind.FormData,
            IsFile = isFile,
            IsArray = isArray,
        };
    }

    /// <summary>
    /// Builds the <c>$form</c> local for a multipart method. Optional fields are appended only when present, so
    /// an absent optional never reaches the wire as the string "undefined".
    /// </summary>
    private static string RenderFormData(TsMethod method)
    {
        var builder = new StringBuilder();
        builder.AppendLine("    const $form = new FormData();");

        foreach (var field in method.Parameters.Where(p => p.Kind == OpenApiParameterKind.FormData))
        {
            var element = field.IsFile ? "$item" : "String($item)";
            var single = field.IsFile ? field.Name : "String(" + field.Name + ")";

            var append = field.IsArray
                ? "for (const $item of " + field.Name + ") $form.append('" + field.WireName + "', " + element + ");"
                : "$form.append('" + field.WireName + "', " + single + ");";

            builder.AppendLine(field.Optional
                ? "    if (" + field.Name + " !== undefined && " + field.Name + " !== null) " + append
                : "    " + append);
        }

        return builder.ToString();
    }

    private string UrlExpression(TsMethod method)
    {
        var path = method.Path;

        foreach (var p in method.Parameters.Where(p => p.Kind == OpenApiParameterKind.Path))
            path = path.Replace("{" + p.WireName + "}", "${encodeURIComponent(String(" + p.Name + "))}");

        var queryParams = method.Parameters.Where(p => p.Kind == OpenApiParameterKind.Query).ToList();

        var querySuffix = string.Empty;
        if (queryParams.Count > 0)
        {
            var entries = queryParams.Select(p =>
                string.Equals(p.Name, p.WireName, StringComparison.Ordinal)
                    ? p.Name
                    : $"'{p.WireName}': {p.Name}");
            querySuffix = "${this.$query({ " + string.Join(", ", entries) + " })}";
        }

        return "`${this.$baseUrl}" + path + querySuffix + "`";
    }

    // ---- queries.ts ----

    /// <summary>
    /// Gives every operation a hook name that is unique across the whole queries.ts module.
    /// client.ts can reuse a method name in two classes (CustomersApi.query and ShippingApi.query are
    /// distinct members), but queries.ts flattens them into module-level functions, so the bare
    /// "use" + method name collides. When two or more operations would produce the same bare name,
    /// BOTH sides are qualified with their tag — never just the loser — so the emitted names do not
    /// depend on group order. A numeric suffix is the last resort.
    /// </summary>
    private static void AssignHookNames(List<TsGroup> groups)
    {
        var bareCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            foreach (var method in group.Methods)
            {
                var bare = "use" + Names.Pascal(method.Name);
                bareCounts[bare] = bareCounts.TryGetValue(bare, out var count) ? count + 1 : 1;
            }
        }

        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            foreach (var method in group.Methods)
            {
                var bare = "use" + Names.Pascal(method.Name);
                var preferred = bareCounts[bare] == 1
                    ? bare
                    : "use" + Names.Pascal(group.Tag) + Names.Pascal(method.Name);

                var candidate = preferred;
                var suffix = 2;
                while (!taken.Add(candidate))
                    candidate = preferred + suffix++;

                method.HookName = candidate;
            }
        }
    }

    private string RenderQueries(List<TsGroup> groups, IReadOnlyCollection<string> exported)
    {
        AssignHookNames(groups);

        var clientClasses = groups.Select(g => g.ClassName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var imports = new StringBuilder();
        imports.AppendLine($"import {{ {string.Join(", ", clientClasses)}, type ClientOptions }} from './client';");

        var typeImports = RenderTypeImports(string.Join(" ", groups.SelectMany(g => g.Methods).Select(m => m.ReturnType + " " + string.Join(" ", m.Signature.Select(p => p.Type)))), exported);
        if (!string.IsNullOrWhiteSpace(typeImports))
            imports.Append(typeImports);

        var config = new StringBuilder();
        config.AppendLine("// Module-level client instances. Call configureApiClients() once at startup to point them at your API.");
        foreach (var g in groups)
            config.AppendLine($"let {g.InstanceName} = new {g.ClassName}();");
        config.AppendLine();
        config.AppendLine("/** Reconfigures every generated client, e.g. to set the base URL or a custom fetch. */");
        config.AppendLine("export function configureApiClients(options: ClientOptions): void {");
        foreach (var g in groups)
            config.AppendLine($"  {g.InstanceName} = new {g.ClassName}(options);");
        config.Append("}");

        var hooks = new StringBuilder();

        foreach (var group in groups)
        {
            var queryMethods = group.Methods.Where(m => m.IsQuery).ToList();
            if (queryMethods.Count > 0)
            {
                hooks.AppendLine($"export const {group.KeysName} = {{");
                foreach (var m in queryMethods)
                {
                    // The key factory must mirror the hook signature exactly, optional markers included: a hook
                    // declaring `q?: string` passes `string | undefined` straight into this call.
                    var keyArgs = m.Signature.Select(p => $"{p.Name}{(p.Optional ? "?" : string.Empty)}: {p.Type}");
                    var keyParts = new List<string> { $"'{group.Tag}'", $"'{m.Name}'" };
                    keyParts.AddRange(m.Signature.Select(p => p.Name));
                    hooks.AppendLine($"  {m.Name}: ({string.Join(", ", keyArgs)}) => [{string.Join(", ", keyParts)}] as const,");
                }
                hooks.AppendLine("} as const;");
                hooks.AppendLine();
            }

            foreach (var m in group.Methods)
            {
                hooks.Append(RenderHook(group, m));
                hooks.AppendLine();
            }
        }

        var model = new ScriptObject();
        model["imports"] = imports.ToString().TrimEnd();
        model["config"] = config.ToString();
        model["hooks"] = hooks.ToString().TrimEnd() + Environment.NewLine;
        return Render("queries.ts.scriban", model);
    }

    private string RenderHook(TsGroup group, TsMethod method)
    {
        var result = method.ReturnsVoid ? "void" : method.ReturnType;
        var call = $"{group.InstanceName}.{method.Name}({string.Join(", ", method.Signature.Select(p => p.Name))})";

        var builder = new StringBuilder();

        if (method.IsQuery)
        {
            var args = method.Signature.Select(p => $"{p.Name}{(p.Optional ? "?" : string.Empty)}: {p.Type}").ToList();
            args.Add($"options?: Omit<ReactQuery.UseQueryOptions<{result}>, 'queryKey' | 'queryFn'>");

            var keyArgs = string.Join(", ", method.Signature.Select(p => p.Name));

            builder.AppendLine($"export function {method.HookName}({string.Join(", ", args)}) {{");
            builder.AppendLine($"  return ReactQuery.useQuery({{ queryKey: {group.KeysName}.{method.Name}({keyArgs}), queryFn: () => {call}, ...options }});");
            builder.AppendLine("}");
        }
        else
        {
            var variables = method.Signature.Count == 0
                ? "void"
                : "{ " + string.Join("; ", method.Signature.Select(p => $"{p.Name}{(p.Optional ? "?" : string.Empty)}: {p.Type}")) + " }";

            var mutationCall = method.Signature.Count == 0
                ? $"() => {call}"
                : $"(variables: {variables}) => {group.InstanceName}.{method.Name}({string.Join(", ", method.Signature.Select(p => "variables." + p.Name))})";

            builder.AppendLine($"export function {method.HookName}(options?: ReactQuery.UseMutationOptions<{result}, Error, {variables}>) {{");
            builder.AppendLine($"  return ReactQuery.useMutation({{ mutationFn: {mutationCall}, ...options }});");
            builder.AppendLine("}");
        }

        return builder.ToString();
    }

    // ---- shared helpers ----

    private string RenderTypes(string envelopes, string models)
    {
        var model = new ScriptObject();
        model["envelopes"] = envelopes;
        model["models"] = models;
        return Render("types.ts.scriban", model);
    }

    private IReadOnlyCollection<string> ExportedTypeNames(OpenApiDocument document, GenericsReconstruction reconstruction)
    {
        var excluded = new HashSet<string>(reconstruction.ExcludedTypeNames, StringComparer.Ordinal);

        var names = new HashSet<string>(StringComparer.Ordinal);
        names.UnionWith(_envelopeNames);
        names.UnionWith(_containerNames);

        foreach (var key in document.Definitions.Keys)
            if (!excluded.Contains(key))
                names.Add(key);

        return names;
    }

    private static string RenderTypeImports(string body, IReadOnlyCollection<string> exported)
    {
        var referenced = exported
            .Where(name => Regex.IsMatch(body, $@"\b{Regex.Escape(name)}\b"))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        return referenced.Count == 0
            ? string.Empty
            : $"import type {{ {string.Join(", ", referenced)} }} from './types';";
    }

    private string Route(string path)
    {
        if (string.IsNullOrWhiteSpace(_settings.PathPrefix))
            return path;

        var prefix = "/" + _settings.PathPrefix!.Trim('/');
        return path.StartsWith("/", StringComparison.Ordinal) ? prefix + path : prefix + "/" + path;
    }

    private bool IsExcluded(OpenApiOperationDescription description)
        => description.Operation.Tags is { Count: > 0 }
           && description.Operation.Tags.Any(t => _settings.ExcludedTags.Contains(t));

    private bool IsExcludedHeader(string name)
        => _settings.ExcludeAllHeaderParameters || _settings.ExcludedHeaderParameters.Contains(name);

    private static string PrimaryTag(OpenApiOperationDescription description)
        => description.Operation.Tags is { Count: > 0 } tags ? tags[0] : "Default";

    /// <summary>Picks the schema for the media type the client will actually receive: application/json first.</summary>
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

    private static string LastSegment(string typeName)
    {
        var value = typeName;
        var plus = value.LastIndexOf('+');
        if (plus >= 0) value = value.Substring(plus + 1);
        var dot = value.LastIndexOf('.');
        if (dot >= 0) value = value.Substring(dot + 1);
        return value;
    }

    private static object? Ext(JsonSchema schema, string key)
    {
        var extensions = schema.ActualSchema.ExtensionData;
        if (extensions is not null && extensions.TryGetValue(key, out var value))
            return value;

        extensions = schema.ExtensionData;
        return extensions is not null && extensions.TryGetValue(key, out value) ? value : null;
    }

    private static bool ExtBool(JsonSchema schema, string key)
        => Ext(schema, key) switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var parsed) && parsed,
            _ => false,
        };

    private static string? ExtString(JsonSchema schema, string key) => Ext(schema, key)?.ToString();

    // Shared across generator instances and threads: the test suite runs its classes in parallel, and a plain
    // Dictionary corrupted itself under that load on CI ("Operations that change non-concurrent collections
    // must have exclusive access"). A parsed Scriban Template is immutable and renders through its own
    // TemplateContext, so sharing the parsed object is safe; only the cache itself needed to be.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Template> TemplateCache = new(StringComparer.Ordinal);

    private static string Render(string templateName, ScriptObject model)
    {
        var template = TemplateCache.GetOrAdd(templateName, static name =>
        {
            var assembly = typeof(TypeScriptClientGenerator).Assembly;
            var resource = assembly.GetManifestResourceNames()
                .First(n => n.Equals(name, StringComparison.Ordinal) || n.EndsWith("." + name, StringComparison.Ordinal));

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new System.IO.StreamReader(stream);
            return Template.Parse(reader.ReadToEnd());
        });

        var context = new TemplateContext { MemberRenamer = member => member.Name };
        context.PushGlobal(model);
        return template.Render(context);
    }

    private sealed class Grouping : IGrouping<string, OpenApiOperationDescription>
    {
        private readonly IEnumerable<OpenApiOperationDescription> _items;
        public Grouping(string key, IEnumerable<OpenApiOperationDescription> items) { Key = key; _items = items; }
        public string Key { get; }
        public IEnumerator<OpenApiOperationDescription> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }
}

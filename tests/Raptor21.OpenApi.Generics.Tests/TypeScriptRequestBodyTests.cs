using System;
using System.Threading.Tasks;

using Raptor21.OpenApi.Generics.CodeGen;

using Xunit;

namespace Raptor21.OpenApi.Generics.Tests;

/// <summary>
/// Request bodies in the TypeScript emitter: the body parameter's name and required-ness, a name collision
/// with a query parameter, multipart bodies sent as FormData, and the document-level protocol version marker.
/// The fixture packs one operation per case.
/// </summary>
public sealed class TypeScriptRequestBodyTests
{
    private static async Task<TypeScriptClientOutput> GenerateUploadsAsync(Action<TypeScriptClientGeneratorSettings>? configure = null)
    {
        var document = await Documents.FromFixtureAsync("uploads.openapi.json");
        var settings = new TypeScriptClientGeneratorSettings { GenerateQueries = true };
        configure?.Invoke(settings);
        return new TypeScriptClientGenerator(settings).Generate(document);
    }

    // ---- JSON bodies ----

    [Fact]
    public async Task Body_parameter_is_named_request_and_required_by_default()
    {
        var output = await GenerateUploadsAsync();

        // The document marks /rename's body required: required either way.
        Assert.Contains("async rename(id: string, request: RenameRequest): Promise<UploadResultDto>", output.Client);
        // /note leaves requestBody.required unset (Swashbuckle's default): still required by default.
        Assert.Contains("async note(id: string, request: NoteRequest): Promise<void>", output.Client);
        Assert.Contains("body: JSON.stringify(request)", output.Client);
        Assert.DoesNotContain("body?: ", output.Client);
    }

    [Fact]
    public async Task Optional_body_setting_follows_the_document()
    {
        var output = await GenerateUploadsAsync(s => s.BodyParameterRequired = false);

        // Explicit required: true in the document still wins.
        Assert.Contains("async rename(id: string, request: RenameRequest)", output.Client);
        // Unset in the document -> optional, after the required path parameter.
        Assert.Contains("async note(id: string, request?: NoteRequest)", output.Client);
    }

    [Fact]
    public async Task Body_parameter_name_is_configurable()
    {
        var output = await GenerateUploadsAsync(s => s.BodyParameterName = "payload");

        Assert.Contains("async rename(id: string, payload: RenameRequest)", output.Client);
        Assert.Contains("body: JSON.stringify(payload)", output.Client);
    }

    [Fact]
    public async Task Body_name_yields_to_a_query_parameter_of_the_same_name()
    {
        var output = await GenerateUploadsAsync();

        // /search has a query parameter literally called "request"; the body steps aside to requestBody so
        // neither shadows the other, and the query string still carries the original wire name.
        Assert.Contains("async search(requestBody: SearchRequest, request?: string)", output.Client);
        Assert.Contains("this.$query({ request })", output.Client);
        Assert.Contains("body: JSON.stringify(requestBody)", output.Client);
    }

    [Fact]
    public async Task Mutation_hook_variables_use_the_body_name()
    {
        var output = await GenerateUploadsAsync();

        Assert.Contains("(variables: { id: string; request: RenameRequest }) => filesApi.rename(variables.id, variables.request)", output.Queries);
    }

    // ---- multipart ----

    [Fact]
    public async Task Multipart_body_is_spread_into_fields_and_sent_as_FormData()
    {
        var output = await GenerateUploadsAsync();
        var method = ExtractMethod(output.Client, "postFiles");

        // Files are Blobs, scalars keep their schema type; required fields first, optional after.
        Assert.Contains("async postFiles(file: Blob, note?: string, tags?: string[], attachments?: Blob[]): Promise<UploadResultDto>", method);
        Assert.Contains("const $form = new FormData();", method);
        Assert.Contains("$form.append('file', file);", method);
        Assert.Contains("if (note !== undefined && note !== null) $form.append('note', String(note));", method);
        Assert.Contains("if (tags !== undefined && tags !== null) for (const $item of tags) $form.append('tags', String($item));", method);
        Assert.Contains("if (attachments !== undefined && attachments !== null) for (const $item of attachments) $form.append('attachments', $item);", method);
        Assert.Contains("body: $form", method);
        // The browser sets the multipart Content-Type (with the boundary) itself.
        Assert.DoesNotContain("Content-Type", method);
        Assert.DoesNotContain("JSON.stringify", method);
    }

    [Fact]
    public async Task Multipart_mutation_hook_carries_the_fields_as_variables()
    {
        var output = await GenerateUploadsAsync();

        Assert.Contains("{ file: Blob; note?: string; tags?: string[]; attachments?: Blob[] }", output.Queries);
    }

    // ---- protocol version ----

    [Fact]
    public async Task Protocol_version_is_read_from_info()
    {
        var document = await Documents.FromFixtureAsync("uploads.openapi.json");
        var reconstruction = new GenericsReconstruction(document, new TypeScriptClientGeneratorSettings(), new CSharpTypeResolverAdapterForTests());

        Assert.Equal("1", reconstruction.ProtocolVersion);
    }

    [Fact]
    public async Task Document_without_a_version_marker_is_accepted_as_version_1()
    {
        var document = await Documents.FromFixtureAsync("widgets.openapi.json");
        var reconstruction = new GenericsReconstruction(document, new TypeScriptClientGeneratorSettings(), new CSharpTypeResolverAdapterForTests());

        Assert.Null(reconstruction.ProtocolVersion);
    }

    [Fact]
    public async Task Unknown_major_version_is_refused()
    {
        var document = await Documents.FromJsonAsync("""
            {
              "openapi": "3.0.4",
              "info": { "title": "Future", "version": "1.0", "x-raptor21-version": "2.0" },
              "paths": {}
            }
            """);

        var ex = Assert.Throws<NotSupportedException>(() => new TypeScriptClientGenerator(new TypeScriptClientGeneratorSettings()).Generate(document));
        Assert.Contains("'2.0'", ex.Message);
        Assert.Contains("version 1", ex.Message);
    }

    [Fact]
    public async Task Minor_versions_of_the_same_major_are_accepted()
    {
        var document = await Documents.FromJsonAsync("""
            {
              "openapi": "3.0.4",
              "info": { "title": "Future", "version": "1.0", "x-raptor21-version": "1.3" },
              "paths": {}
            }
            """);

        var output = new TypeScriptClientGenerator(new TypeScriptClientGeneratorSettings()).Generate(document);
        Assert.NotNull(output.Client);
    }

    // ---- helpers ----

    private static string ExtractMethod(string client, string name)
    {
        var start = client.IndexOf($"async {name}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"method '{name}' not found in client.ts");
        var end = client.IndexOf("\n  }", start, StringComparison.Ordinal);
        return client.Substring(start, end - start);
    }

    /// <summary>The resolver only matters for schema types, which the version tests never touch.</summary>
    private sealed class CSharpTypeResolverAdapterForTests : ITypeResolver
    {
        public string Resolve(NJsonSchema.JsonSchema schema, bool nullable, string typeNameHint) => "object";
        public string Collection(string elementType) => elementType + "[]";
    }
}

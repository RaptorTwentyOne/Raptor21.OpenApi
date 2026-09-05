using System.Threading.Tasks;

using Raptor21.OpenApi.Generics.CodeGen;

using Xunit;

namespace Raptor21.OpenApi.Generics.Tests;

/// <summary>
/// Covers the TypeScript emitter against a fixture that deliberately packs every hazard the design calls out:
/// an enveloped object and an enveloped array, an array whose elements are themselves envelopes, a string enum,
/// a nullable property, a no-body operation, and a 200 that lists text/plain and text/json alongside
/// application/json in a misleading order.
/// </summary>
public sealed class TypeScriptEmitterTests
{
    private static async Task<TypeScriptClientOutput> GenerateWidgetsAsync(bool queries = true)
    {
        var document = await Documents.FromFixtureAsync("widgets.openapi.json");

        var settings = new TypeScriptClientGeneratorSettings
        {
            GenerateQueries = queries,
        };
        // A cross-cutting tenant header belongs to the fetch pipeline, not to every call site.
        settings.ExcludedHeaderParameters.Add("X-Tenant");

        return new TypeScriptClientGenerator(settings).Generate(document);
    }

    // ---- types.ts ----

    [Fact]
    public async Task Envelope_is_emitted_as_a_real_generic_interface()
    {
        var output = await GenerateWidgetsAsync();

        // BaseResponse<T>, not a flattened WidgetDtoBaseResponse copy, and the payload property is generalised.
        Assert.Contains("export interface BaseResponse<T> {", output.Types);
        Assert.Contains("data?: T;", output.Types);
        // The flattened envelope schemas (x-ignore-model) are never emitted as their own interfaces.
        Assert.DoesNotContain("WidgetDtoBaseResponse", output.Types);
        Assert.DoesNotContain("WidgetDtoListBaseResponse", output.Types);
    }

    [Fact]
    public async Task String_enum_is_emitted_as_a_union_of_literals()
    {
        var output = await GenerateWidgetsAsync();

        Assert.Contains("\"Active\"", output.Types);
        Assert.Contains("\"Discontinued\"", output.Types);
        Assert.Contains("\"Active\" | \"Discontinued\"", output.Types);
    }

    [Fact]
    public async Task Nullable_property_keeps_its_null_in_the_type()
    {
        var output = await GenerateWidgetsAsync();

        // The DTO's nullable description survives as `| null`, and the envelope's nullable errors too.
        Assert.Matches(@"description\??:\s*string\s*\|\s*null", output.Types);
        Assert.Matches(@"errors\??:\s*string\[\]\s*\|\s*null", output.Types);
    }

    // ---- client.ts ----

    [Fact]
    public async Task Client_sends_credentials_on_every_call()
    {
        var output = await GenerateWidgetsAsync();

        Assert.Contains("credentials: 'include'", output.Client);
    }

    [Fact]
    public async Task Method_names_come_from_the_route_when_there_is_no_operationId()
    {
        var output = await GenerateWidgetsAsync();

        // FallbackName derivation, with no operationId in the document to fall back to:
        //  - a bare single-segment resource (/widgets) borrows the HTTP verb -> getWidgets, deleteWidgets;
        //  - a multi-segment route uses its last segment verbatim -> batch, report;
        //  - the /widgets vs /widgets/{id} GET collision is broken by qualifying with the path arg -> getWidgetsById.
        Assert.Contains("async getWidgets(", output.Client);
        Assert.Contains("async getWidgetsById(", output.Client);
        Assert.Contains("async batch(", output.Client);
        Assert.Contains("async report(", output.Client);
        Assert.Contains("async deleteWidgets(", output.Client);
    }

    [Fact]
    public async Task Enveloped_object_and_array_are_unwrapped_to_their_payloads()
    {
        var output = await GenerateWidgetsAsync();

        // Enveloped array -> WidgetDto[]; the body reads the payload off the envelope.
        Assert.Contains("async getWidgets(", output.Client);
        Assert.Contains("Promise<WidgetDto[]>", output.Client);
        Assert.Contains("return $envelope.data as WidgetDto[];", output.Client);

        // Enveloped object -> WidgetDto.
        Assert.Contains("async getWidgetsById(id: number): Promise<WidgetDto>", output.Client);
        Assert.Contains("return $envelope.data as WidgetDto;", output.Client);
    }

    [Fact]
    public async Task Array_of_envelopes_is_not_unwrapped()
    {
        var output = await GenerateWidgetsAsync();

        // The element is reconstructed as an envelope, but an array of envelopes is not itself an envelope,
        // so the payload stays wrapped: BaseResponse<WidgetDto>[] handed back as-is.
        Assert.Contains("async batch(): Promise<BaseResponse<WidgetDto>[]>", output.Client);
        Assert.Contains("return await $response.json() as BaseResponse<WidgetDto>[];", output.Client);
    }

    [Fact]
    public async Task Response_media_type_selection_prefers_application_json_over_text()
    {
        var output = await GenerateWidgetsAsync();

        // /widgets/report lists text/plain (the bare payload) and text/json before application/json (the
        // envelope). Picking application/json is what lets the envelope be recognised and unwrapped.
        Assert.Contains("async report(): Promise<WidgetDto>", output.Client);
        Assert.Contains("const $envelope = await $response.json() as BaseResponse<WidgetDto>;", output.Client);
    }

    [Fact]
    public async Task No_body_operation_returns_void_and_does_not_parse_a_response()
    {
        var output = await GenerateWidgetsAsync();

        var method = ExtractMethod(output.Client, "deleteWidgets");
        Assert.Contains("Promise<void>", method);
        Assert.Contains("await this.$http(", method);
        Assert.DoesNotContain(".json()", method);
    }

    [Fact]
    public async Task Excluded_header_parameter_is_absent_from_signatures_and_body()
    {
        var output = await GenerateWidgetsAsync();

        Assert.DoesNotContain("X-Tenant", output.Client);
        Assert.DoesNotContain("xTenant", output.Client);
    }

    [Fact]
    public async Task Query_parameters_are_assembled_through_the_shared_helper()
    {
        var output = await GenerateWidgetsAsync();

        var method = ExtractMethod(output.Client, "getWidgets");
        Assert.Contains("this.$query({", method);
        Assert.Contains("search", method);
        Assert.Contains("page", method);
    }

    // ---- queries.ts ----

    [Fact]
    public async Task Queries_file_emits_a_query_hook_for_gets_and_a_mutation_hook_for_the_rest()
    {
        var output = await GenerateWidgetsAsync(queries: true);

        Assert.NotNull(output.Queries);
        Assert.Contains("import * as ReactQuery from '@tanstack/react-query';", output.Queries!);
        Assert.Contains("export function useGetWidgets(", output.Queries);
        Assert.Contains("ReactQuery.useQuery(", output.Queries);
        Assert.Contains("export function useDeleteWidgets(", output.Queries);
        Assert.Contains("ReactQuery.useMutation(", output.Queries);
    }

    [Fact]
    public async Task Queries_file_is_null_unless_requested()
    {
        var output = await GenerateWidgetsAsync(queries: false);
        Assert.Null(output.Queries);
    }

    // ---- snapshots (seam-refactor tripwire for the TypeScript path) ----

    [Fact]
    public async Task Widgets_typescript_output_is_stable()
    {
        var output = await GenerateWidgetsAsync(queries: true);

        SnapshotTester.Match(output.Types, "widgets.types.ts.expected");
        SnapshotTester.Match(output.Client, "widgets.client.ts.expected");
        SnapshotTester.Match(output.Queries!, "widgets.queries.ts.expected");
    }

    // ---- container reconstruction (task 2c): the live doc has no container, so the sample fixture carries one ----

    [Fact]
    public async Task Container_is_emitted_as_a_generic_and_nests_inside_the_envelope()
    {
        var document = await Documents.FromFixtureAsync("sample.openapi.json");
        var output = new TypeScriptClientGenerator(new TypeScriptClientGeneratorSettings()).Generate(document);

        // Page<T> restored as a generic, its item-bearing property generalised to T[] (nullable in the doc)...
        Assert.Contains("export interface Page<T> {", output.Types);
        Assert.Contains("items?: T[] | null;", output.Types);
        // ...the flattened container schema is not emitted...
        Assert.DoesNotContain("CountryDtoPage", output.Types);
        // ...and the paged endpoint unwraps the envelope to the container payload.
        Assert.Contains("Promise<Page<CountryDto>>", output.Client);
    }

    [Fact]
    public async Task Sample_typescript_output_is_stable()
    {
        var document = await Documents.FromFixtureAsync("sample.openapi.json");
        var output = new TypeScriptClientGenerator(new TypeScriptClientGeneratorSettings()).Generate(document);

        SnapshotTester.Match(output.Types, "sample.types.ts.expected");
        SnapshotTester.Match(output.Client, "sample.client.ts.expected");
    }

    /// <summary>Extracts a single generated method body from a client file, for method-scoped assertions.</summary>
    private static string ExtractMethod(string client, string methodName)
    {
        var start = client.IndexOf("async " + methodName + "(", System.StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method '{methodName}' not found in generated client.");

        var next = client.IndexOf("  async ", start + 1, System.StringComparison.Ordinal);
        return next < 0 ? client.Substring(start) : client.Substring(start, next - start);
    }
}

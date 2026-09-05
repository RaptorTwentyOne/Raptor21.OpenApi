using System.Threading.Tasks;

using Raptor21.OpenApi.Generics.CodeGen;

using Xunit;

namespace Raptor21.OpenApi.Generics.Tests;

/// <summary>
/// Locks the C# (Refit) output of the sample document to a committed snapshot. The TypeScript emitter shares
/// reconstruction and name derivation with this path; this snapshot is the tripwire that catches a shared-seam
/// refactor changing the C# output as a side effect.
/// </summary>
public sealed class CSharpStabilityTests
{
    [Fact]
    public async Task Sample_document_refit_output_is_stable()
    {
        var document = await Documents.FromFixtureAsync("sample.openapi.json");

        // Mirrors how Sampa consumes it: default envelope payload property ("data"), DI registration on,
        // grouped by tag. Fixed here so the snapshot describes one exact configuration.
        var settings = new RefitClientGeneratorSettings
        {
            Namespace = "Sampa.Generated",
            GenerateDependencyInjection = true,
        };

        var output = new RefitClientGenerator(settings).Generate(document);

        SnapshotTester.Match(output, "sample.client.cs.expected");
    }

    [Fact]
    public async Task Sample_document_reconstructs_envelope_and_container_as_generics()
    {
        var document = await Documents.FromFixtureAsync("sample.openapi.json");
        var settings = new RefitClientGeneratorSettings { Namespace = "Sampa.Generated" };

        var output = new RefitClientGenerator(settings).Generate(document);

        // The envelope is restored as a real generic over the payload, not redefined as a flattened model...
        Assert.Contains("BaseResponse<System.Collections.Generic.ICollection<CountryDto>>", output);
        // ...the container nests inside it as its own generic...
        Assert.Contains("Page<CountryDto>", output);
        // ...and the flattened envelope/container schemas (x-ignore-model) never become models.
        Assert.DoesNotContain("class CountryDtoBaseResponse", output);
        Assert.DoesNotContain("class CountryDtoListBaseResponse", output);
        Assert.DoesNotContain("class CountryDtoPageBaseResponse", output);
        Assert.DoesNotContain("class CountryDtoPage ", output);
        // The one true model survives.
        Assert.Contains("class CountryDto", output);
    }
}

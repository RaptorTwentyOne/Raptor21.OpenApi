using System.Threading.Tasks;

using NSwag;

namespace Raptor21.OpenApi.Generics.Tests;

/// <summary>Loads OpenAPI documents for the tests, from committed fixtures or inline JSON.</summary>
internal static class Documents
{
    /// <summary>Parses an OpenAPI document from a fixture file under <c>Fixtures/</c>.</summary>
    public static Task<OpenApiDocument> FromFixtureAsync(string fileName)
        => OpenApiDocument.FromJsonAsync(SnapshotTester.ReadFixture(fileName));

    /// <summary>Parses an OpenAPI document from an inline JSON string.</summary>
    public static Task<OpenApiDocument> FromJsonAsync(string json)
        => OpenApiDocument.FromJsonAsync(json);
}

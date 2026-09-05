using System;
using System.IO;
using System.Runtime.CompilerServices;

using Xunit;

namespace Raptor21.OpenApi.Generics.Tests;

/// <summary>
/// A dependency-free snapshot harness. Generated output is compared against a committed <c>.expected</c> text
/// file under <c>Snapshots/</c>; fixtures (input OpenAPI documents) are read from <c>Fixtures/</c>.
/// </summary>
/// <remarks>
/// <para>
/// Why plain committed files rather than Verify: the seam this project guards is "the emitter produces exactly
/// this text". A committed <c>.expected</c> file makes that text reviewable in the diff of the change that moves
/// it, needs no package, no runner integration, and no <c>.received</c>/approval dance — the lighter of the two
/// options the task allowed.
/// </para>
/// <para>
/// Line endings are normalised to <c>\n</c> on both sides before comparison. The generators mix
/// <see cref="Environment.NewLine"/> (CRLF on Windows, LF on Linux) with the newlines baked into the Scriban
/// templates, so a byte-exact compare would fail the moment the same snapshot is checked on the other OS. The
/// CI matrix runs on Linux; developers run on Windows. Normalising keeps one committed file honest on both.
/// </para>
/// <para>
/// Workflow: a missing snapshot is written and the test fails once, asking you to review and re-run — this is
/// how a new snapshot is born without silently passing. Setting the environment variable
/// <c>RAPTOR21_UPDATE_SNAPSHOTS=1</c> rewrites every snapshot and passes, for an intentional output change.
/// </para>
/// </remarks>
internal static class SnapshotTester
{
    private const string UpdateEnvVar = "RAPTOR21_UPDATE_SNAPSHOTS";

    /// <summary>Absolute path of the test project directory, resolved from the compile-time path of this file.</summary>
    private static string ProjectDirectory([CallerFilePath] string thisFilePath = "")
        => Path.GetDirectoryName(thisFilePath)!;

    private static string FixturesDirectory => Path.Combine(ProjectDirectory(), "Fixtures");

    private static string SnapshotsDirectory => Path.Combine(ProjectDirectory(), "Snapshots");

    private static bool UpdateRequested =>
        Environment.GetEnvironmentVariable(UpdateEnvVar) is "1" or "true" or "TRUE";

    /// <summary>Reads a fixture OpenAPI document (raw JSON text) by file name.</summary>
    public static string ReadFixture(string fileName)
    {
        var path = Path.Combine(FixturesDirectory, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fixture '{fileName}' not found at {path}.", path);

        return File.ReadAllText(path);
    }

    /// <summary>
    /// Asserts that <paramref name="actual"/> matches the committed snapshot named <paramref name="snapshotName"/>,
    /// normalising line endings on both sides. Creates the snapshot (and fails) when it is missing; overwrites it
    /// (and passes) when an update was requested.
    /// </summary>
    public static void Match(string actual, string snapshotName)
    {
        Directory.CreateDirectory(SnapshotsDirectory);
        var path = Path.Combine(SnapshotsDirectory, snapshotName);
        var normalisedActual = Normalize(actual);

        if (UpdateRequested)
        {
            File.WriteAllText(path, normalisedActual);
            return;
        }

        if (!File.Exists(path))
        {
            File.WriteAllText(path, normalisedActual);
            Assert.Fail(
                $"Snapshot '{snapshotName}' did not exist and was created at {path}. " +
                "Review it and re-run the tests to lock it in.");
        }

        var expected = Normalize(File.ReadAllText(path));

        if (!string.Equals(expected, normalisedActual, StringComparison.Ordinal))
        {
            Assert.Fail(
                $"Output does not match snapshot '{snapshotName}'.\n" +
                $"If this change is intended, re-run with {UpdateEnvVar}=1 to update the snapshot.\n\n" +
                FirstDifference(expected, normalisedActual));
        }
    }

    private static string Normalize(string text)
        => text.Replace("\r\n", "\n").Replace("\r", "\n");

    /// <summary>Produces a compact, line-oriented description of the first divergence for the failure message.</summary>
    private static string FirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var max = Math.Max(expectedLines.Length, actualLines.Length);

        for (var i = 0; i < max; i++)
        {
            var e = i < expectedLines.Length ? expectedLines[i] : "<end of file>";
            var a = i < actualLines.Length ? actualLines[i] : "<end of file>";
            if (!string.Equals(e, a, StringComparison.Ordinal))
            {
                return $"First difference at line {i + 1}:\n  expected: {e}\n  actual:   {a}";
            }
        }

        return $"Files differ in length only (expected {expectedLines.Length} lines, actual {actualLines.Length}).";
    }
}

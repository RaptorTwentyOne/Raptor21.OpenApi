using System;
using System.Collections.Generic;

namespace Raptor21.OpenApi.Generics.CodeGen;

/// <summary>
/// Settings that reconstruction needs whatever the target language: which envelope property carries the
/// payload, and how projected contract names map onto the names the consumer actually uses.
/// </summary>
public abstract class ClientGeneratorSettingsBase
{
    /// <summary>
    /// Maps a type name found in <c>x-api-wrapper-type</c> or <c>x-data-container-type</c> onto the type the
    /// generated client should use.
    /// </summary>
    /// <remarks>
    /// The projected name is whatever the producing ecosystem calls the contract, which is not necessarily
    /// what the consumer calls it — a document written by a C# service names C# types, and a TypeScript
    /// consumer binding to its own port of the same contract needs to say so. Unmapped names are used as
    /// written.
    /// </remarks>
    public IDictionary<string, string> TypeMappings { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// JSON property name carrying the envelope's payload. Defaults to <c>data</c>.
    /// </summary>
    /// <remarks>
    /// The document says a schema is an envelope but not which of its properties is the cargo, so
    /// reconstruction needs to be told. Every envelope in a document is expected to agree on this.
    /// </remarks>
    public string PayloadPropertyName { get; set; } = "data";
}

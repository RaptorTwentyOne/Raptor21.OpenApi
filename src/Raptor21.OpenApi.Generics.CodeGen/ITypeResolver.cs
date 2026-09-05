using NJsonSchema;
using NJsonSchema.CodeGeneration.CSharp;
using NJsonSchema.CodeGeneration.TypeScript;

namespace Raptor21.OpenApi.Generics.CodeGen;

/// <summary>
/// The language-specific part of reconstruction: turning a schema into a type name, and wrapping a type in
/// the language's idiomatic collection. Everything else in <see cref="GenericsReconstruction"/> is the same
/// whatever the target language.
/// </summary>
public interface ITypeResolver
{
    /// <summary>Resolves a schema to a type name in the target language.</summary>
    string Resolve(JsonSchema schema, bool nullable, string hint);

    /// <summary>Wraps an already-resolved element type in the language's idiomatic collection.</summary>
    string Collection(string itemType);
}

/// <summary>C# resolver: delegates to NJsonSchema and wraps collections as <c>ICollection&lt;T&gt;</c>.</summary>
public sealed class CSharpTypeResolverAdapter : ITypeResolver
{
    private readonly CSharpTypeResolver _inner;

    /// <summary>Wraps an NJsonSchema C# resolver.</summary>
    public CSharpTypeResolverAdapter(CSharpTypeResolver inner) => _inner = inner;

    /// <inheritdoc />
    public string Resolve(JsonSchema schema, bool nullable, string hint) => _inner.Resolve(schema, nullable, hint);

    /// <inheritdoc />
    public string Collection(string itemType) => $"System.Collections.Generic.ICollection<{itemType}>";
}

/// <summary>TypeScript resolver: delegates to NJsonSchema and wraps collections as <c>T[]</c>.</summary>
public sealed class TypeScriptTypeResolverAdapter : ITypeResolver
{
    private readonly TypeScriptTypeResolver _inner;

    /// <summary>Wraps an NJsonSchema TypeScript resolver.</summary>
    public TypeScriptTypeResolverAdapter(TypeScriptTypeResolver inner) => _inner = inner;

    /// <inheritdoc />
    public string Resolve(JsonSchema schema, bool nullable, string hint) => _inner.Resolve(schema, nullable, hint);

    /// <inheritdoc />
    public string Collection(string itemType) => $"{itemType}[]";
}

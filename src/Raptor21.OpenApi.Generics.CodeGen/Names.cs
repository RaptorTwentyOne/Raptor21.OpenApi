using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using NSwag;

namespace Raptor21.OpenApi.Generics.CodeGen;

/// <summary>
/// Escapes identifiers that collide with a target language's reserved words.
/// </summary>
/// <remarks>
/// The set and the escape differ by language: C# prefixes a keyword with <c>@</c> and keeps the same
/// spelling, whereas TypeScript has no such prefix and must rename — a trailing underscore is the least
/// surprising choice and stays a legal identifier.
/// </remarks>
public sealed class IdentifierEscaper
{
    private readonly HashSet<string> _reserved;
    private readonly Func<string, string> _escape;

    /// <summary>Creates an escaper from a reserved-word set and the rename applied to a collision.</summary>
    public IdentifierEscaper(IEnumerable<string> reserved, Func<string, string> escape)
    {
        _reserved = new HashSet<string>(reserved ?? throw new ArgumentNullException(nameof(reserved)), StringComparer.Ordinal);
        _escape = escape ?? throw new ArgumentNullException(nameof(escape));
    }

    /// <summary>Returns the identifier unchanged, or its escaped form when it is a reserved word.</summary>
    public string Escape(string identifier)
        => _reserved.Contains(identifier) ? _escape(identifier) : identifier;

    // The keyword arrays are declared before the escaper properties: static fields initialise in textual
    // order, so an escaper that referenced an array declared below it would see a null.
    private static readonly string[] CSharpKeywords =
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit",
        "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
        "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out",
        "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
        "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    private static readonly string[] TypeScriptKeywords =
    {
        "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else",
        "enum", "export", "extends", "false", "finally", "for", "function", "if", "import", "in", "instanceof",
        "new", "null", "return", "super", "switch", "this", "throw", "true", "try", "typeof", "var", "void",
        "while", "with", "as", "implements", "interface", "let", "package", "private", "protected", "public",
        "static", "yield", "any", "boolean", "number", "string", "symbol", "type", "undefined", "never", "object",
        "declare", "readonly", "abstract", "async", "await", "of",
    };

    /// <summary>C# keywords, escaped with a leading <c>@</c>.</summary>
    public static IdentifierEscaper CSharp { get; } = new(CSharpKeywords, k => "@" + k);

    /// <summary>JavaScript/TypeScript reserved words, escaped with a trailing underscore.</summary>
    public static IdentifierEscaper TypeScript { get; } = new(TypeScriptKeywords, k => k + "_");
}

/// <summary>
/// Language-neutral name derivation shared by the Refit and TypeScript emitters. Casing and method-name
/// candidates are the same everywhere; only reserved-word escaping differs, which callers supply.
/// </summary>
public static class Names
{
    /// <summary>PascalCase of a value split on <c>_ - space . /</c>. Non letter-or-digit characters are dropped.</summary>
    public static string Pascal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var parts = value.Split(new[] { '_', '-', ' ', '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
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

    /// <summary>camelCase of a value, escaped for the target language when it lands on a reserved word.</summary>
    public static string Camel(string value, IdentifierEscaper? escaper = null)
    {
        var pascal = Pascal(value);
        if (pascal.Length == 0)
            return "value";

        var camel = char.ToLower(pascal[0], CultureInfo.InvariantCulture) + pascal.Substring(1);
        return escaper is null ? camel : escaper.Escape(camel);
    }

    /// <summary>Picks a PascalCase method name that is unique within its group.</summary>
    /// <remarks>
    /// Candidates run from most to least natural: the document's operationId, the route's action segment, the
    /// same prefixed by the HTTP verb, then qualified by the parameters it takes. The numeric suffix is a
    /// last-resort uniqueness guarantee, not a name anyone should have to read.
    /// </remarks>
    public static string UniqueMethodName(OpenApiOperationDescription description, HashSet<string> used)
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

    /// <summary>Builds a method name from the route, letting the HTTP verb supply one only when the route is a bare resource.</summary>
    private static string FallbackName(OpenApiOperationDescription description)
    {
        var segments = description.Path
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !s.StartsWith("{", StringComparison.Ordinal))
            .Select(Pascal)
            .Where(s => s.Length > 0)
            .ToArray();

        if (segments.Length >= 2)
            return segments[segments.Length - 1];

        var resource = segments.Length == 1 ? segments[0] : string.Empty;
        return Pascal(description.Method.ToString()) + resource;
    }
}

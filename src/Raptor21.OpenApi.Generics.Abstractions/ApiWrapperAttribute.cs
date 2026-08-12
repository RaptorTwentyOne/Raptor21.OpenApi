using System;

namespace Raptor21.OpenApi.Generics;

/// <summary>
/// Declares an open generic type as a response envelope, so projection records it rather than letting the
/// document flatten it into an anonymous shape.
/// </summary>
/// <remarks>
/// Use this on envelopes you own. Envelopes that come from a package you cannot annotate are registered at
/// startup instead — see the AspNetCore package's <c>UseEnvelope</c>.
/// </remarks>
/// <example>
/// <code>
/// [ApiWrapper]
/// public class BaseResponse&lt;T&gt;
/// {
///     public T Data { get; set; }
///     public int StatusCode { get; set; }
///     public bool IsSuccessful { get; set; }
///     public List&lt;string&gt; Errors { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false)]
public sealed class ApiWrapperAttribute : Attribute
{
    /// <summary>
    /// Which type parameter carries the payload. Defaults to the first, which covers every single-parameter
    /// envelope; set it for envelopes that take the payload somewhere other than first.
    /// </summary>
    public int DataParameterIndex { get; set; }

    /// <summary>
    /// Name of the property holding the payload. Defaults to <c>Data</c>. Reconstruction does not need it,
    /// but projection uses it to tell the payload apart from the envelope's own fields.
    /// </summary>
    public string DataPropertyName { get; set; } = "Data";
}

/// <summary>
/// Declares an open generic type as a payload container — a pagination envelope, a result page, anything
/// that wraps the item type without being the response envelope itself.
/// </summary>
/// <remarks>
/// Containers nest inside envelopes: <c>BaseResponse&lt;Page&lt;CustomerDto&gt;&gt;</c> projects an envelope
/// whose payload is a container whose item is <c>CustomerDto</c>. Ordinary collections do not need this —
/// arrays already survive projection.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false)]
public sealed class DataContainerAttribute : Attribute
{
    /// <summary>Which type parameter carries the item. Defaults to the first.</summary>
    public int ItemParameterIndex { get; set; }
}

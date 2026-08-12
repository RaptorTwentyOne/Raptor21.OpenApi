using System;
using System.Collections.Generic;

namespace Raptor21.OpenApi.Generics.AspNetCore;

/// <summary>
/// Configures how generic contract identity is projected into the OpenAPI document.
/// </summary>
public sealed class OpenApiGenericsOptions
{
    /// <summary>The envelope and container contracts to recognise.</summary>
    public OpenApiGenericsRegistry Registry { get; } = new();

    /// <summary>
    /// The envelope applied to responses that declare only their payload. Leave it null when controllers
    /// already declare the envelope themselves.
    /// </summary>
    /// <remarks>
    /// This exists for the common case where an action is annotated
    /// <c>[ProducesResponseType(typeof(List&lt;CountryDto&gt;), 200)]</c> but returns
    /// <c>BaseResponse&lt;List&lt;CountryDto&gt;&gt;</c> at runtime. Without it the document promises a bare
    /// payload and every generated client fails to deserialise the response it actually receives. Setting it
    /// re-declares those responses as the envelope, leaving the attributes untouched.
    /// </remarks>
    public ApiWrapperDescriptor? AutoEnvelope { get; private set; }

    /// <summary>
    /// Media types whose schemas take the automatic envelope. Defaults to <c>application/json</c>; binary
    /// payloads such as PDF or spreadsheet downloads are not enveloped on the wire and stay untouched.
    /// </summary>
    public ISet<string> AutoEnvelopeMediaTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/json" };

    /// <summary>
    /// Marks projected envelope and container schemas with <c>x-ignore-model</c> so generators skip them.
    /// On by default — it is the whole point of the projection. Turn it off to keep a document that
    /// generators without reconstruction support can still produce usable (if duplicated) models from.
    /// </summary>
    public bool MarkProjectedSchemasIgnored { get; set; } = true;

    /// <summary>Registers an envelope contract.</summary>
    /// <param name="openGenericType">The open generic, e.g. <c>typeof(BaseResponse&lt;&gt;)</c>.</param>
    /// <param name="dataParameterIndex">Which type parameter carries the payload.</param>
    /// <param name="dataPropertyName">Name of the property carrying the payload.</param>
    public OpenApiGenericsOptions UseEnvelope(Type openGenericType, int dataParameterIndex = 0, string dataPropertyName = "Data")
    {
        var descriptor = new ApiWrapperDescriptor(openGenericType, dataParameterIndex, dataPropertyName);
        Registry.AddWrapper(descriptor);
        AutoEnvelope = descriptor;
        return this;
    }

    /// <summary>Registers an envelope contract without applying it automatically to payload-only responses.</summary>
    public OpenApiGenericsOptions AddEnvelope(Type openGenericType, int dataParameterIndex = 0, string dataPropertyName = "Data")
    {
        Registry.AddWrapper(openGenericType, dataParameterIndex, dataPropertyName);
        return this;
    }

    /// <summary>Registers a payload container contract such as a pagination page.</summary>
    public OpenApiGenericsOptions AddContainer(Type openGenericType, int itemParameterIndex = 0)
    {
        Registry.AddContainer(openGenericType, itemParameterIndex);
        return this;
    }
}

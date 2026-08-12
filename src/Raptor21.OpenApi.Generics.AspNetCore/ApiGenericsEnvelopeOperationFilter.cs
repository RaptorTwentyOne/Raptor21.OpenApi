using System;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.OpenApi;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Raptor21.OpenApi.Generics.AspNetCore;

/// <summary>
/// Re-declares payload-only responses as the configured envelope, so the document describes the body the
/// service actually sends.
/// </summary>
/// <remarks>
/// Only active when <see cref="OpenApiGenericsOptions.AutoEnvelope"/> is set. It reads the payload type from
/// the API description rather than the schema, so existing <c>[ProducesResponseType]</c> attributes stay
/// exactly as their authors wrote them — no sweeping edit across a large controller surface.
///
/// Left alone: responses with no declared type, media types outside
/// <see cref="OpenApiGenericsOptions.AutoEnvelopeMediaTypes"/> (binary downloads are not enveloped), and
/// actions that already declare an envelope themselves.
/// </remarks>
public sealed class ApiGenericsEnvelopeOperationFilter : IOperationFilter
{
    private readonly OpenApiGenericsOptions _options;

    /// <summary>Creates the filter.</summary>
    public ApiGenericsEnvelopeOperationFilter(OpenApiGenericsOptions options)
        => _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var envelope = _options.AutoEnvelope;
        if (envelope is null || operation.Responses is null || context?.ApiDescription is null)
            return;

        // Closing a multi-parameter envelope would mean inventing values for the other parameters, and there
        // is no honest guess to make. Such an envelope can still be declared explicitly by the action.
        if (envelope.OpenGenericType.GetGenericArguments().Length != 1)
            return;

        foreach (var responseType in context.ApiDescription.SupportedResponseTypes)
        {
            var payload = responseType.Type;

            if (payload is null || payload == typeof(void) || payload == typeof(Task))
                continue;

            // An action annotated with its own result type (FileResult, IActionResult) is describing how it
            // replies, not what it carries. Enveloping that would document a body nobody sends.
            if (IsResultType(payload))
                continue;

            // The action already speaks in envelopes; the schema filter will stamp it.
            if (_options.Registry.TryGetWrapper(payload, out _))
                continue;

            var key = responseType.IsDefaultResponse ? "default" : responseType.StatusCode.ToString();

            if (!operation.Responses.TryGetValue(key, out var response) || response.Content is null)
                continue;

            foreach (var mediaType in _options.AutoEnvelopeMediaTypes)
            {
                if (!response.Content.TryGetValue(mediaType, out var media))
                    continue;

                var enveloped = envelope.OpenGenericType.MakeGenericType(payload);
                media.Schema = context.SchemaGenerator.GenerateSchema(enveloped, context.SchemaRepository);
            }
        }
    }

    private static bool IsResultType(Type type)
        => typeof(IActionResult).IsAssignableFrom(type)
           || typeof(IResult).IsAssignableFrom(type)
           || typeof(IConvertToActionResult).IsAssignableFrom(type);
}

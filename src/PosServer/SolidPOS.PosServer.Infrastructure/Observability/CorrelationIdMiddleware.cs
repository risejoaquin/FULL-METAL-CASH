using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace SolidPOS.PosServer.Infrastructure.Observability;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string RequestIdHeaderName = "X-Request-Id";
    private const int MaxIdentifierLength = 128;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string requestId = NormalizeIdentifier(context.TraceIdentifier, Guid.NewGuid().ToString("N"));
        string correlationId = ResolveCorrelationId(context, requestId);
        context.TraceIdentifier = requestId;
        context.Response.Headers[HeaderName] = correlationId;
        context.Response.Headers[RequestIdHeaderName] = requestId;

        Activity? activity = Activity.Current;
        activity?.SetTag("solidpos.correlation_id", correlationId);
        activity?.SetTag("solidpos.request_id", requestId);

        using (LogContext.PushProperty("correlation_id", correlationId))
        using (LogContext.PushProperty("request_id", requestId))
        using (LogContext.PushProperty("trace_id", activity?.TraceId.ToString() ?? requestId))
        {
            _logger.LogDebug("Correlation context initialized");
            await _next(context);
        }
    }

    public static string NormalizeIdentifier(string? candidate, string fallback)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return fallback;
        }

        string normalized = new(candidate.Trim()
            .Where(static c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ':')
            .Take(MaxIdentifierLength)
            .ToArray());

        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string ResolveCorrelationId(HttpContext context, string fallback)
    {
        string? candidate = context.Request.Headers.TryGetValue(HeaderName, out var value)
            ? value.FirstOrDefault()
            : null;
        return NormalizeIdentifier(candidate, fallback);
    }
}

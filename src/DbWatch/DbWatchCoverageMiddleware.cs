using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbWatch;

internal sealed class DbWatchCoverageMiddleware(
    RequestDelegate _next,
    DbWatchHub _hub,
    IOptions<DbWatchOptions> _options,
    IOptions<JsonOptions> _jsonOptions,
    ILogger<DbWatchCoverageMiddleware> _logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var returnType = InspectedReturnType(context);

        if (returnType is null)
        {
            await _next(context);
            return;
        }

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody, context.RequestAborted);

        try
        {
            Publish(context, returnType, buffer);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "DbWatch could not work out the DTO coverage of {Path}.", context.Request.Path);
        }
    }

    private Type? InspectedReturnType(HttpContext context)
    {
        if (!_hub.IsWatchingCoverage
            || !HttpMethods.IsGet(context.Request.Method)
            || !context.Request.Path.StartsWithSegments(_options.Value.ApiPathPrefix))
        {
            return null;
        }

        var returnType = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>()?.MethodInfo.ReturnType;

        return returnType is not null
            && DbWatchCoverageAnalyzer.CanAnalyze(returnType, _jsonOptions.Value.JsonSerializerOptions)
                ? returnType
                : null;
    }

    private void Publish(HttpContext context, Type returnType, MemoryStream buffer)
    {
        var response = context.Response;

        if (response.StatusCode is < 200 or >= 300
            || buffer.Length == 0
            || buffer.Length > _options.Value.MaxResponseBytes
            || response.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        var report = DbWatchCoverageAnalyzer.Analyze(
            returnType,
            buffer.GetBuffer().AsMemory(0, (int)buffer.Length),
            _jsonOptions.Value.JsonSerializerOptions);

        if (report is not null)
        {
            _hub.PublishCoverage(context, report);
        }
    }
}

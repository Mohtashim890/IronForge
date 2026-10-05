using IronForge.Application.Execution;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace IronForge.Api.Helpers
{
    public sealed class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<GlobalExceptionHandler> _logger;

        public GlobalExceptionHandler(
            ILogger<GlobalExceptionHandler> logger)
        {
            _logger = logger;
        }

        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            var executionContext =
           httpContext.RequestServices
               .GetRequiredService<IExecutionContextAccessor>();

            var context = executionContext.Current;

            _logger.LogError(
                exception,
                "Unhandled exception occurred while processing request. " +
                "Method={Method}, Path={Path}, TraceId={TraceId}, " +
                "CorrelationId={CorrelationId}, TenantId={TenantId}, UserId={UserId}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                context?.TraceId ?? httpContext.TraceIdentifier,
                context?.CorrelationId ?? httpContext.TraceIdentifier,
                context?.TenantId,
                context?.Actor.UserId);

            var response = ApiErrorFactory.Create(
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred.",
                context?.TraceId ?? httpContext.TraceIdentifier);

            httpContext.Response.StatusCode =
                StatusCodes.Status500InternalServerError;

            await httpContext.Response.WriteAsJsonAsync(
                response,
                cancellationToken);

            return true;
        }
    }
}

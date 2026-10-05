using System.Diagnostics;
using IronForge.Application.Execution;

namespace IronForge.Application.Observablity.Tracing
{
    public static class IronForgeActivityExtensions
    {
        public static void SetExecutionContext(
            this Activity activity,
            Execution.ExecutionContext? context)
        {
            if (context == null)
            {
                return;
            }

            if (context.TenantId.HasValue)
            {
                activity.SetTag(
                    "ironforge.tenant.id",
                    context.TenantId.Value);
            }

            if (context.Actor.UserId.HasValue)
            {
                activity.SetTag(
                    "ironforge.user.id",
                    context.Actor.UserId.Value);
            }

            if (!string.IsNullOrWhiteSpace(
                context.Actor.Agent?.AgentId))
            {
                activity.SetTag(
                    "ironforge.agent.id",
                    context.Actor.Agent!.AgentId);
            }

            if (!string.IsNullOrWhiteSpace(
                context.Actor.ClientId))
            {
                activity.SetTag(
                    "ironforge.client.id",
                    context.Actor.ClientId);
            }

            if (!string.IsNullOrWhiteSpace(
                context.CorrelationId))
            {
                activity.SetTag(
                    "ironforge.correlation_id",
                    context.CorrelationId);
            }
        }
    }
}

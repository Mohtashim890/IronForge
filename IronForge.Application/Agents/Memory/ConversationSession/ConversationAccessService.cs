using IronForge.Application.Entities;
using IronForge.Application.Execution;

namespace IronForge.Application.Agents.Memory.ConversationSession
{
    public class ConversationAccessService
        : IConversationAccessService
    {
        private readonly IExecutionContextAccessor _executionContext;

        public ConversationAccessService(
            IExecutionContextAccessor executionContext)
        {
            _executionContext = executionContext;
        }

        public ConversationAccessResult AuthorizeSession(
            AgentSession session)
        {
            var context = _executionContext.Current;

            if (context == null)
            {
                return ConversationAccessResult.Deny(
                    "Execution context is not established.");
            }

            if (!context.TenantId.HasValue)
            {
                return ConversationAccessResult.Deny(
                    "Tenant context is not established.");
            }

            if (context.TenantId.Value != session.TenantId)
            {
                return ConversationAccessResult.Deny(
                    "Conversation does not belong to the current tenant.");
            }

            if (!context.Actor.UserId.HasValue)
            {
                return ConversationAccessResult.Deny(
                    "Authenticated user context is not established.");
            }

            if (context.Actor.UserId.Value != session.UserId)
            {
                return ConversationAccessResult.Deny(
                    "Conversation does not belong to the current user.");
            }

            if (context.Actor.Agent != null)
            {
                if (!string.Equals(
                    context.Actor.Agent.AgentId,
                    session.AgentId,
                    StringComparison.Ordinal))
                {
                    return ConversationAccessResult.Deny(
                        "Conversation does not belong to the current agent.");
                }
            }

            return ConversationAccessResult.Allow();
        }
    }
}
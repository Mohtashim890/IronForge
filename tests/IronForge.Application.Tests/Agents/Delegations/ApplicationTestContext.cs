using IronForge.Application.Agents.Models;
using IronForge.Application.Execution;
using Moq;
using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Application.Tests.Agents.Delegations
{
    public static class ApplicationTestContext
    {
        public const int DefaultUserId = 42;
        public const int DefaultTenantId = 7;

        public static Execution.ExecutionContext Create(
            int? userId = DefaultUserId,
            int? tenantId = DefaultTenantId,
            string? agentId = null,
            Guid? delegationId = null,
            string? clientId = null,
            AgentType agentType = AgentType.External)
        {
            return new Execution.ExecutionContext
            {
                TenantId = tenantId,
                Actor = new ExecutionActor
                {
                    UserId = userId,
                    ClientId = clientId,
                    DelegationId = delegationId,

                    Agent = agentId is null
                        ? null
                        : new AgentIdentity
                        {
                            AgentId = agentId,
                            Name = agentId,
                            Version = "1.0",
                            Type = agentType
                        }
                }
            };
        }

        public static void Set(
            Mock<IExecutionContextAccessor> accessor,
            Execution.ExecutionContext context)
        {
            accessor
                .Setup(x => x.Current)
                .Returns(context);
        }

        public static Execution.ExecutionContext AuthenticatedUser(
            int userId = DefaultUserId,
            int tenantId = DefaultTenantId)
        {
            return Create(
                userId: userId,
                tenantId: tenantId);
        }

        public static Execution.ExecutionContext DelegatedAgent(
            string agentId = "product-agent",
            Guid? delegationId = null,
            int userId = DefaultUserId,
            int tenantId = DefaultTenantId,
            string? clientId = "mcp-client")
        {
            return Create(
                userId: userId,
                tenantId: tenantId,
                agentId: agentId,
                delegationId: delegationId ?? Guid.NewGuid(),
                clientId: clientId,
                agentType: AgentType.External);
        }

        public static Execution.ExecutionContext InternalAgent(
            int userId = DefaultUserId,
            int tenantId = DefaultTenantId)
        {
            return Create(
                userId: userId,
                tenantId: tenantId,
                agentId: "product-agent",
                agentType: AgentType.Internal);
        }
    }
}

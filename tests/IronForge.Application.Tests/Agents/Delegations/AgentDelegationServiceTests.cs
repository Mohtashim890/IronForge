using IronForge.Application.Agents.Authorization;
using IronForge.Application.Agents.Delegations;
using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Auth.Services;
using IronForge.Application.Execution;
using Moq;
using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Application.Tests.Agents.Delegations
{
    public class AgentDelegationServiceTests
    {
        [Fact]
        public async Task ValidateAsync_WithValidDelegation_ReturnsDelegation()
        {
            // Arrange
            var delegationId = Guid.NewGuid();

            var delegation = new AgentDelegation
            {
                DelegationId = delegationId,
                UserId = 42,
                AgentId = "test-agent",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
                Revoked = false
            };

            var store = new Mock<IAgentDelegationStore>();

            store
                .Setup(x => x.GetAsync(
                    delegationId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(delegation);

            var service = CreateServiceForAuthenticatedUser(store: store);

            // Act
            var result = await service.ValidateAsync(delegationId);

            // Assert
            Assert.Same(delegation, result);
        }

        [Fact]
        public async Task ValidateAsync_WhenDelegationDoesNotExist_ThrowsUnauthorizedAccessException()
        {
            // Arrange
            var delegationId = Guid.NewGuid();

            var store = new Mock<IAgentDelegationStore>();

            store
                .Setup(x => x.GetAsync(
                    delegationId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((AgentDelegation?)null);

            var service = CreateServiceForAuthenticatedUser(store: store);

            // Act
            var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => service.ValidateAsync(delegationId));

            // Assert
            Assert.Equal(
                "Delegation is invalid.",
                exception.Message);
        }

        [Fact]
        public async Task ValidateAsync_WhenDelegationIsRevoked_ThrowsUnauthorizedAccessException()
        {
            // Arrange
            var delegationId = Guid.NewGuid();

            var delegation = new AgentDelegation
            {
                DelegationId = delegationId,
                UserId = 42,
                AgentId = "test-agent",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
                Revoked = true
            };

            var store = new Mock<IAgentDelegationStore>();

            store
                .Setup(x => x.GetAsync(
                    delegationId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(delegation);

            var service = CreateServiceForAuthenticatedUser(store: store);

            // Act
            var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => service.ValidateAsync(delegationId));

            // Assert
            Assert.Equal(
                "Delegation has been revoked.",
                exception.Message);
        }

        [Fact]
        public async Task ValidateAsync_WhenDelegationIsExpired_ThrowsUnauthorizedAccessException()
        {
            // Arrange
            var delegationId = Guid.NewGuid();

            var delegation = new AgentDelegation
            {
                DelegationId = delegationId,
                UserId = 42,
                AgentId = "test-agent",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1),
                Revoked = false
            };

            var store = new Mock<IAgentDelegationStore>();

            store
                .Setup(x => x.GetAsync(
                    delegationId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(delegation);

            var service = CreateServiceForAuthenticatedUser(store : store);

            // Act
            var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => service.ValidateAsync(delegationId));

            // Assert
            Assert.Equal(
                "Delegation has expired.",
                exception.Message);
        }

        private static AgentDelegationService CreateServiceForAuthenticatedUser(
            Mock<IExecutionContextAccessor>? executionContext = null,
            Mock<IUserAuthorizationService>? userAuthorization = null,
            Mock<IAgentPermissionRegistry>? agentPermissionRegistry = null,
            Mock<IAgentDelegationStore>? store = null,
            Mock<IAuditService>? auditService = null,
            Mock<IAgentMcpClientAuthorizationService>? mcpClientAuthorizationService = null)
        {
            executionContext ??= new Mock<IExecutionContextAccessor>();
            userAuthorization ??= new Mock<IUserAuthorizationService>();
            agentPermissionRegistry ??= new Mock<IAgentPermissionRegistry>();
            store ??= new Mock<IAgentDelegationStore>();
            auditService ??= new Mock<IAuditService>();
            mcpClientAuthorizationService ??= new Mock<IAgentMcpClientAuthorizationService>();

            ConfigureValidDelegationEnvironment(
                executionContext,
                userAuthorization,
                agentPermissionRegistry,
                mcpClientAuthorizationService);

            return CreateService(
                executionContext,
                userAuthorization,
                agentPermissionRegistry,
                store,
                auditService,
                mcpClientAuthorizationService);
        }

        private static AgentDelegationService CreateService(
            Mock<IExecutionContextAccessor> executionContext,
            Mock<IUserAuthorizationService> userAuthorization,
            Mock<IAgentPermissionRegistry> agentPermissionRegistry,
            Mock<IAgentDelegationStore> store,
            Mock<IAuditService> auditService,
            Mock<IAgentMcpClientAuthorizationService> mcpClientAuthorizationService)
        {
            return new AgentDelegationService(
                executionContext.Object,
                userAuthorization.Object,
                agentPermissionRegistry.Object,
                store.Object,
                auditService.Object,
                mcpClientAuthorizationService.Object);
        }

        private static void ConfigureValidDelegationEnvironment(
            Mock<IExecutionContextAccessor> executionContext,
            Mock<IUserAuthorizationService> userAuthorization,
            Mock<IAgentPermissionRegistry> permissionRegistry,
            Mock<IAgentMcpClientAuthorizationService> clientAuthorization)
        {
            ApplicationTestContext.Set(
                executionContext,
                ApplicationTestContext.AuthenticatedUser());

            permissionRegistry
                .Setup(x => x.GetPermissionsAsync("product-agent"))
                .ReturnsAsync(
                    new HashSet<string>(
                        new[]
                        {
                    "product.read",
                    "product.write"
                        },
                        StringComparer.OrdinalIgnoreCase));

            userAuthorization
                .Setup(x => x.HasPermission(It.IsAny<string>()))
                .Returns(true);

            clientAuthorization
                .Setup(x =>
                    x.IsAuthorizedAsync(
                        "product-agent",
                        "mcp-client",
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }
    }
}

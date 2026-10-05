using IronForge.Application.Agents.Delegations;
using IronForge.Application.Entities;
using IronForge.Application.Persistence;

namespace IronForge.Application.Agents.Delegations
{
    public class DBAgentDelegationStore : IAgentDelegationStore
    {
        private readonly IRepository<AgentDelegationEntity> _delegations;
        private readonly IRepository<AgentDelegationScopeEntity> _scopes;

        public DBAgentDelegationStore(
            IRepository<AgentDelegationEntity> delegations,
            IRepository<AgentDelegationScopeEntity> scopes)
        {
            _delegations = delegations;
            _scopes = scopes;
        }

        public async Task<IReadOnlyList<AgentDelegation>> GetForUserAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            var entities =
               await _delegations.ListAsync(
                   x => x.UserId == userId,
                   cancellationToken);

            var delegationIds = entities
                .Select(x => x.DelegationId)
                .ToList();

            var scopes = delegationIds.Count == 0
                ? new List<AgentDelegationScopeEntity>()
                : await _scopes.ListAsync(
                    x => delegationIds.Contains(x.DelegationId),
                    cancellationToken);

            var scopesByDelegation = scopes
                .GroupBy(x => x.DelegationId)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(s => s.Scope)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase));

            return entities
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(entity => new AgentDelegation
                {
                    DelegationId = entity.DelegationId,
                    UserId = entity.UserId,
                    AgentId = entity.AgentId,
                    ClientId = entity.ClientId,
                    CreatedAtUtc = entity.CreatedAtUtc,
                    ExpiresAtUtc = entity.ExpiresAtUtc,
                    Revoked = entity.Revoked,
                    Scopes = scopesByDelegation.TryGetValue(
                        entity.DelegationId,
                        out var entityScopes)
                        ? entityScopes
                        : new HashSet<string>(
                            StringComparer.OrdinalIgnoreCase)
                })
                .ToList();
        }

        public async Task<AgentDelegation?> GetAsync(
            Guid delegationId,
            CancellationToken cancellationToken = default)
        {
            var entity =
                await _delegations.FirstOrDefaultAsync(
                    x => x.DelegationId == delegationId,
                    cancellationToken);

            if (entity == null)
            {
                return null;
            }

            var scopes = await _scopes.ListAsync(
                x => x.DelegationId == delegationId,
                cancellationToken);

            return new AgentDelegation
            {
                DelegationId = entity.DelegationId,
                UserId = entity.UserId,
                AgentId = entity.AgentId,
                ClientId = entity.ClientId,
                CreatedAtUtc = entity.CreatedAtUtc,
                ExpiresAtUtc = entity.ExpiresAtUtc,
                Revoked = entity.Revoked,
                Scopes = scopes
                    .Select(x => x.Scope)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
            };
        }

        public async Task SaveAsync(
            AgentDelegation delegation,
            CancellationToken cancellationToken = default)
        {
            var existing =
                await _delegations.FirstOrDefaultTrackedAsync(
                    x => x.DelegationId == delegation.DelegationId,
                    cancellationToken);

            if (existing == null)
            {
                var entity = new AgentDelegationEntity
                {
                    DelegationId = delegation.DelegationId,
                    UserId = delegation.UserId,
                    AgentId = delegation.AgentId,
                    ClientId = delegation.ClientId,
                    CreatedAtUtc = delegation.CreatedAtUtc,
                    ExpiresAtUtc = delegation.ExpiresAtUtc,
                    Revoked = delegation.Revoked
                };

                foreach (var scope in delegation.Scopes)
                {
                    _scopes.Add(
                        new AgentDelegationScopeEntity
                        {
                            DelegationId =
                                delegation.DelegationId,
                            Scope = scope
                        });
                }

                _delegations.Add(entity);
            }
            else
            {
                existing.UserId = delegation.UserId;
                existing.AgentId = delegation.AgentId;
                existing.ClientId = delegation.ClientId;
                existing.CreatedAtUtc = delegation.CreatedAtUtc;
                existing.ExpiresAtUtc = delegation.ExpiresAtUtc;
                existing.Revoked = delegation.Revoked;

                var existingScopes = await _scopes.ListAsync(
                    x => x.DelegationId == delegation.DelegationId,
                    cancellationToken);

                foreach (var scope in existingScopes)
                {
                    _scopes.Remove(scope);
                }

                foreach (var scope in delegation.Scopes)
                {
                    _scopes.Add(
                        new AgentDelegationScopeEntity
                        {
                            DelegationId =
                                delegation.DelegationId,
                            Scope = scope
                        });
                }

                //_delegations.Update(existing);
            }

            await _delegations.SaveChangesAsync(
                cancellationToken);
        }

        public async Task DeleteAsync(
            Guid delegationId,
            CancellationToken cancellationToken = default)
        {
            var entity =
                await _delegations.FirstOrDefaultAsync(
                    x => x.DelegationId == delegationId,
                    cancellationToken);

            if (entity == null)
            {
                return;
            }

            var scopes = await _scopes.ListAsync(
                x => x.DelegationId == delegationId,
                cancellationToken);

            foreach (var scope in scopes)
            {
                _scopes.Remove(scope);
            }

            _delegations.Remove(entity);

            await _delegations.SaveChangesAsync(
                cancellationToken);
        }

        public async Task<Guid> RevokeAsync(
            Guid delegationId,
            CancellationToken cancellationToken = default)
        {
            var entity =
                await _delegations.FirstOrDefaultTrackedAsync(
                    x => x.DelegationId == delegationId,
                    cancellationToken);

            if (entity == null)
            {
                return Guid.Empty;
            }

            entity.Revoked = true;

            //_delegations.Update(entity);

            await _delegations.SaveChangesAsync(
                cancellationToken);

            return entity.DelegationId;
        }
    }
}

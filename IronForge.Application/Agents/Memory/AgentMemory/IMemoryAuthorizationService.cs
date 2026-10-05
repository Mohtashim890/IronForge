using IronForge.Application.Auth.Models;

namespace IronForge.Application.Agents.Memory.AgentMemory
{
    public interface IMemoryAuthorizationService
    {
        Task<ResourceAuthorizationResult> AuthorizeCreateAsync(
            MemoryScope scope,
            CancellationToken cancellationToken = default);

        Task<ResourceAuthorizationResult> AuthorizeReadAsync(
            Entities.AgentMemory memory,
            CancellationToken cancellationToken = default);

        Task<ResourceAuthorizationResult> AuthorizeDeleteAsync(
            Entities.AgentMemory memory,
            CancellationToken cancellationToken = default);

        Task<ResourceAuthorizationResult> AuthorizeCollectionReadAsync(
            MemoryScope scope,
            CancellationToken cancellationToken = default);
    }
}
namespace IronForge.Application.Agents.Memory.AgentMemory
{
    public interface IMemoryService
    {
        Task<Entities.AgentMemory> CreateAsync(
            MemoryScope scope,
            MemoryType type,
            string content,
            Guid? sessionId = null,
            DateTime? expiresAtUtc = null,
            CancellationToken cancellationToken = default);

        Task<Entities.AgentMemory?> GetAsync(
            long memoryId,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Entities.AgentMemory>> GetTenantMemoriesAsync(
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Entities.AgentMemory>> GetUserMemoriesAsync(
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Entities.AgentMemory>> GetSessionMemoriesAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default);

        Task DeleteAsync(
            long memoryId,
            CancellationToken cancellationToken = default);
    }
}
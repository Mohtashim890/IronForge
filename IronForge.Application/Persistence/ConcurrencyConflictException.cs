namespace IronForge.Application.Persistence;

/// <summary>
/// A row changed between being read and being saved (optimistic concurrency).
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(Exception innerException)
        : base("The record was changed by another request.", innerException)
    {
    }
}

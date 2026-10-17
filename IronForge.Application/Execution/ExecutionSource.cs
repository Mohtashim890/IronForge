namespace IronForge.Application.Execution;

public enum ExecutionSource
{
    Web,
    Maui,
    Mcp,
    InternalAgent,

    /// <summary>Background jobs and anonymous system callbacks (payment webhooks).</summary>
    Background
}
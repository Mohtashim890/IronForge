namespace IronForge.Application.Agents.Memory.AgentMemory
{
    public enum MemoryScope
    {
        Tenant,
        User,
        Session
    }

    public enum MemorySource
    {
        User,
        Agent,
        System
    }
    public enum MemoryType
    {
        Preference,
        Fact,
        Context,
        Instruction
    }
}

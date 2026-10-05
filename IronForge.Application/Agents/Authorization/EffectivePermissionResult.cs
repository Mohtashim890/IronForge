namespace IronForge.Application.Agents.Authorization
{
    public class EffectivePermissionResult
    {
        public bool Allowed { get; set; }

        public string? Reason { get; set; }
    }
}

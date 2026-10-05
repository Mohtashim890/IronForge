namespace IronForge.Application.Entities
{
    public class AgentPermission
    {
        public int Id { get; set; }

        public int AgentId { get; set; }

        public string Permission { get; set; } = "";

        public Agent Agent { get; set; } = null!;
    }
}

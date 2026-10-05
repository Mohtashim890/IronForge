namespace IronForge.Application.Entities
{
    public class Tenant
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string Slug { get; set; } = "";

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; }
    }
}

namespace IronForge.Application.Entities
{
    public class Product
    {
        public int Id { get; set; }
        public int TenantId { get; set; }

        public string Name { get; set; } = "";

        public decimal Price { get; set; }
        public int OwnerUserId { get; set; }
    }
}

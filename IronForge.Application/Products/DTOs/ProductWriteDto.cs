using System.ComponentModel.DataAnnotations;

namespace IronForge.Application.Products.DTOs
{
    public class ProductWriteDto
    {
        [StringLength(100)]
        public string Name { get; set; } = "";

        [Range(0.01, 1000000)]
        public decimal Price { get; set; }
    }
}

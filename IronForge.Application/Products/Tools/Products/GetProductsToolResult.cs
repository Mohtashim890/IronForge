using IronForge.Application.Products.DTOs;

namespace IronForge.Application.Products.Tools.Products
{
    public class GetProductsToolResult
    {
        public bool Success { get; set; }

        public List<ProductDto> Products { get; set; } = [];

        public string? ErrorMessage { get; set; }

        public int? StatusCode { get; set; }
    }
}

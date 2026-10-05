using IronForge.Application.Products.DTOs;

namespace IronForge.Application.Products.Tools.Products
{
    public class GetProductToolResult
    {
        public bool Success { get; set; }

        public ProductDto? Product { get; set; }

        public string? ErrorMessage { get; set; }

        public int? StatusCode { get; set; }
    }
}

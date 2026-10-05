using IronForge.Application.Products.Services;

namespace IronForge.Application.Products.Tools.Products
{
    public class GetProductsTool : IGetProductsTool
    {
        private readonly IProductService _productService;

        public GetProductsTool(
            IProductService productService)
        {
            _productService = productService;
        }

        public async Task<GetProductsToolResult> ExecuteAsync()
        {
            var result =
                await _productService.GetProductsAsync();

            if (!result.Success)
            {
                return new GetProductsToolResult
                {
                    Success = false,
                    ErrorMessage = result.ErrorMessage,
                    StatusCode = result.StatusCode
                };
            }

            return new GetProductsToolResult
            {
                Success = true,
                Products = result.Data ?? []
            };
        }
    }
}

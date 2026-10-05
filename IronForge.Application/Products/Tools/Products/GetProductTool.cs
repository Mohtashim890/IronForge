using IronForge.Application.Products.Services;

namespace IronForge.Application.Products.Tools.Products
{
    public class GetProductTool : IGetProductTool
    {
        private readonly IProductService _productService;

        public GetProductTool(
            IProductService productService)
        {
            _productService = productService;
        }

        public async Task<GetProductToolResult> ExecuteAsync(
            int id)
        {
            var result =
                await _productService.GetProductAsync(id);

            if (!result.Success)
            {
                return new GetProductToolResult
                {
                    Success = false,
                    ErrorMessage = result.ErrorMessage,
                    StatusCode = result.StatusCode
                };
            }

            return new GetProductToolResult
            {
                Success = true,
                Product = result.Data
            };
        }
    }
}

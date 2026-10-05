using IronForge.Application.Products.Services;

namespace IronForge.Application.Products.Tools.Products;

public class DeleteProductTool : IDeleteProductTool
{
    private readonly IProductService _productService;

    public DeleteProductTool(
        IProductService productService)
    {
        _productService = productService;
    }

    public async Task<DeleteProductToolResult> ExecuteAsync(
        int id)
    {
        var result =
            await _productService.DeleteProductAsync(id);

        return new DeleteProductToolResult
        {
            Success = result.Success,
            ErrorMessage = result.ErrorMessage,
            StatusCode = result.StatusCode
        };
    }
}
using IronForge.Application.Products.DTOs;
using IronForge.Application.Products.Services;

namespace IronForge.Application.Products.Tools.Products;

public class UpdateProductTool : IUpdateProductTool
{
    private readonly IProductService _productService;

    public UpdateProductTool(
        IProductService productService)
    {
        _productService = productService;
    }

    public async Task<UpdateProductToolResult> ExecuteAsync(
        int id,
        ProductWriteDto dto)
    {
        var result =
            await _productService.UpdateProductAsync(
                id,
                dto);

        return new UpdateProductToolResult
        {
            Success = result.Success,
            ErrorMessage = result.ErrorMessage,
            StatusCode = result.StatusCode
        };
    }
}
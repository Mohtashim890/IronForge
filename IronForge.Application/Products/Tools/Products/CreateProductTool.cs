using IronForge.Application.Products.DTOs;
using IronForge.Application.Products.Services;

namespace IronForge.Application.Products.Tools.Products;

public class CreateProductTool : ICreateProductTool
{
    private readonly IProductService _productService;

    public CreateProductTool(
        IProductService productService)
    {
        _productService = productService;
    }

    public async Task<CreateProductToolResult> ExecuteAsync(
        ProductWriteDto dto)
    {
        var result =
            await _productService.CreateProductAsync(dto);

        return new CreateProductToolResult
        {
            Success = result.Success,
            Product = result.Data,
            ErrorMessage = result.ErrorMessage,
            StatusCode = result.StatusCode
        };
    }
}
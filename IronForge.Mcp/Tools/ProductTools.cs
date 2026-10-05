using IronForge.Application.Products.DTOs;
using IronForge.Application.Products.Tools.Products;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace IronForge.Mcp.Tools;

[McpServerToolType]
public class ProductTools
{
    [McpServerTool]
    [Description("Gets the products that the current user is authorized to view.")]
    public async Task<object> GetProducts(
        IGetProductsTool productTool,
        CancellationToken cancellationToken)
    {
        var result =
            await productTool.ExecuteAsync();

        if (!result.Success)
        {
            return new
            {
                Success = false,
                Error = result.ErrorMessage,
                StatusCode = result.StatusCode
            };
        }

        return new
        {
            Success = true,
            Products = result.Products
        };
    }

    [McpServerTool]
    [Description("Gets the specific product by identifier")]
    public async Task<object> GetProduct(
         [Description("The product identifier.")] int id,
        IGetProductTool productTool,
        CancellationToken cancellationToken)
    {
        var result =
            await productTool.ExecuteAsync(id);

        if (!result.Success)
        {
            return new
            {
                Success = false,
                Error = result.ErrorMessage,
                StatusCode = result.StatusCode
            };
        }

        return new
        {
            Success = true,
            Products = result.Product
        };
    }

    [McpServerTool]
    [Description("Creates a new product.")]
    public async Task<object> CreateProduct(
        [Description("The product to create.")] ProductWriteDto dto,
        ICreateProductTool productTool,
        CancellationToken cancellationToken)
    {
        var result =
            await productTool.ExecuteAsync(dto);

        if (!result.Success)
        {
            return new
            {
                Success = false,
                Error = result.ErrorMessage,
                StatusCode = result.StatusCode
            };
        }

        return new
        {
            Success = true,
            Products = result.Product
        };
    }

    [McpServerTool]
    [Description("Updates an existing product.")]
    public async Task<object> UpdateProduct(
        [Description("The product identifier.")] int id,
        [Description("The updated product data.")] ProductWriteDto dto,
        IUpdateProductTool productTool,
        CancellationToken cancellationToken)
    {
        var result =
            await productTool.ExecuteAsync(id, dto);

        if (!result.Success)
        {
            return new
            {
                Success = false,
                Error = result.ErrorMessage,
                StatusCode = result.StatusCode
            };
        }

        return new
        {
            Success = true
        };
    }
}
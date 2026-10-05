using IronForge.Application.Products.DTOs;

namespace IronForge.Application.Products.Tools.Products
{
    public interface ICreateProductTool
    {
        Task<CreateProductToolResult> ExecuteAsync(
       ProductWriteDto dto);
    }
}

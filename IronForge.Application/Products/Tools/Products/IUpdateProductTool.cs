using IronForge.Application.Products.DTOs;

namespace IronForge.Application.Products.Tools.Products
{
    public interface IUpdateProductTool
    {
        Task<UpdateProductToolResult> ExecuteAsync(
       int id,
       ProductWriteDto dto);
    }
}

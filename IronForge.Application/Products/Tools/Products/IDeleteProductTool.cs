namespace IronForge.Application.Products.Tools.Products
{
    public interface IDeleteProductTool
    {
        Task<DeleteProductToolResult> ExecuteAsync(
       int id);
    }
}

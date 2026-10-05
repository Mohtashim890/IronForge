namespace IronForge.Application.Products.Tools.Products
{
    public interface IGetProductTool
    {
        Task<GetProductToolResult> ExecuteAsync(int id);
    }
}

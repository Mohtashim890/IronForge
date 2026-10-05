namespace IronForge.Application.Products.Tools.Products
{
    public interface IGetProductsTool
    {
        Task<GetProductsToolResult> ExecuteAsync();
    }
}

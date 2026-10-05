using IronForge.Application.Commons;
using IronForge.Application.Products.DTOs;

namespace IronForge.Application.Products.Services
{
    public interface IProductService
    {
        Task<ServiceResult<List<ProductDto>>> GetProductsAsync();

        Task<ServiceResult<ProductDto>> GetProductAsync(int id);

        Task<ServiceResult<ProductDto>> CreateProductAsync(ProductWriteDto dto);

        Task<ServiceResult<bool>> UpdateProductAsync(int id, ProductWriteDto dto);

        Task<ServiceResult<bool>> DeleteProductAsync(int id);
    }
}

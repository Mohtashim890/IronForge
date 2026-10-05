using IronForge.Shared.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Services
{
    public interface IProductService
    {
        Task<ServiceResult<List<Product>>> GetProductsAsync();
        Task<ServiceResult<Product>> GetProductByIdAsync(int id);
        Task<ServiceResult<Product>> CreateProductAsync(Product product);
        Task<ServiceResult<bool>> UpdateProductAsync(Product product);
        Task<ServiceResult<bool>> DeleteProductAsync(int id);
    }
}

using IronForge.Shared.Models;
using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text;

namespace IronForge.Shared.Services
{
    public class ProductService : IProductService
    {
        private readonly HttpClient _httpClient;
        public ProductService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<ServiceResult<Product>> CreateProductAsync(Product product)
        {
            try
            {
                var response =
                           await _httpClient.PostAsJsonAsync(
                               "api/products",
                               product);

                if (!response.IsSuccessStatusCode)
                {
                    return ServiceResult<Product>.Fail(
                        "Unable to create product.",
                        (int)response.StatusCode);
                }

                return ServiceResult<Product>.Ok(await response.Content
                .ReadFromJsonAsync<Product>());
            }
            catch (HttpRequestException)
            {
                return ServiceResult<Product>.Fail(
                    "Unable to connect to the API.");
            }
            catch (TaskCanceledException)
            {
                return ServiceResult<Product>.Fail(
                    "The request timed out.");
            }
            catch (Exception)
            {
                return ServiceResult<Product>.Fail("An unexpected error occurred.");
            }
        }

        public async Task<ServiceResult<bool>> DeleteProductAsync(int id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"api/products/{id}");

                if (!response.IsSuccessStatusCode)
                {
                    return ServiceResult<bool>.Fail(
                        "Unable to delete product.",
                        (int)response.StatusCode);
                }

                return ServiceResult<bool>.Ok(true);
            }
            catch (HttpRequestException)
            {
                return ServiceResult<bool>.Fail(
                    "Unable to connect to the API.");
            }
            catch (TaskCanceledException)
            {
                return ServiceResult<bool>.Fail(
                    "The request timed out.");
            }
            catch (Exception)
            {
                return ServiceResult<bool>.Fail("An unexpected error occurred.");
            }
        }

        public async Task<ServiceResult<Product>> GetProductByIdAsync(int id)
        {
            try
            {
                var response = await _httpClient.GetAsync($"api/products/{id}");
                if (!response.IsSuccessStatusCode)
                {
                    return ServiceResult<Product>.Fail("Product not found.");
                }

                return ServiceResult<Product>.Ok(await response.Content
                    .ReadFromJsonAsync<Product>());
            }
            catch (HttpRequestException)
            {
                return ServiceResult<Product>.Fail(
                    "Unable to connect to the API.");
            }
            catch (TaskCanceledException)
            {
                return ServiceResult<Product>.Fail(
                    "The request timed out.");
            }
            catch (Exception)
            {
                return ServiceResult<Product>.Fail("An unexpected error occurred.");
            }

        }

        public async Task<ServiceResult<List<Product>>> GetProductsAsync()
        {
            try
            {
                var response =
                    await _httpClient.GetAsync("api/products");

                if (!response.IsSuccessStatusCode)
                {
                    return ServiceResult<List<Product>>.Fail(
                        "Unable to load products.",
                        (int)response.StatusCode);
                }

                var products =
                    await response.Content.ReadFromJsonAsync<List<Product>>();

                return ServiceResult<List<Product>>.Ok(products ?? []);
            }
            catch (HttpRequestException)
            {
                return ServiceResult<List<Product>>.Fail(
                    "Unable to connect to the API.");
            }
            catch (TaskCanceledException)
            {
                return ServiceResult<List<Product>>.Fail(
                    "The request timed out.");
            }
            catch (Exception)
            {
                return ServiceResult<List<Product>>.Fail(
                    "An unexpected error occurred.");
            }
        }

        public async Task<ServiceResult<bool>> UpdateProductAsync(Product product)
        {
            try
            {
                var response =
                    await _httpClient.PutAsJsonAsync(
                        $"api/products/{product.Id}",
                        product);

                if (!response.IsSuccessStatusCode)
                {
                    return ServiceResult<bool>.Fail(
                        "Unable to update product.",
                        (int)response.StatusCode);
                }

                return ServiceResult<bool>.Ok(true);
            }
            catch (HttpRequestException)
            {
                return ServiceResult<bool>.Fail(
                    "Unable to connect to the API.");
            }
            catch (TaskCanceledException)
            {
                return ServiceResult<bool>.Fail(
                    "The request timed out.");
            }
            catch (Exception)
            {
                return ServiceResult<bool>.Fail("An unexpected error occurred.");
            }
        }
    }
}

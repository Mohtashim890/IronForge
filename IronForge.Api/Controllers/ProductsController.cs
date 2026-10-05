using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IronForge.Application.Products.Services;
using IronForge.Application.Products.DTOs;
using IronForge.Api.Helpers;

namespace IronForge.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ProductsController : ControllerBase
    {
        private readonly IProductService _productService;

        public ProductsController(IProductService productService)
        {
            _productService = productService;
        }

        [HttpGet]
        public async Task<ActionResult<List<ProductDto>>> GetProducts()
        {
            var result =
                await _productService.GetProductsAsync();

            if (!result.Success && result.StatusCode == 403)
            {
                return Forbid(result.ErrorMessage);
            }
            else if (!result.Success && result.StatusCode == 404)
            {
                return NotFound(ApiErrorFactory.Create(
                    StatusCodes.Status404NotFound, 
                    result.ErrorMessage,
                    HttpContext.TraceIdentifier));
            }
            else if (!result.Success)
            {
                return BadRequest(ApiErrorFactory.Create(
                  StatusCodes.Status400BadRequest,
                  result.ErrorMessage,
                  HttpContext.TraceIdentifier));
            }

            return Ok(result.Data);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ProductDto>> GetProduct(int id)
        {
            var  result    =
                await _productService.GetProductAsync(id);

            if (!result.Success && result.StatusCode == 403)
            {
                return Forbid();
            }
            else if (!result.Success && result.StatusCode == 404)
            {
                return NotFound(ApiErrorFactory.Create(
                    StatusCodes.Status404NotFound,
                    result.ErrorMessage,
                    HttpContext.TraceIdentifier));
            }
            else if (!result.Success)
            {
                return BadRequest(ApiErrorFactory.Create(
                  StatusCodes.Status400BadRequest,
                  result.ErrorMessage,
                  HttpContext.TraceIdentifier));
            }

            return Ok(result.Data);
        }

        [HttpPost]
        public async Task<ActionResult<ProductDto>> CreateProduct(
            ProductWriteDto dto)
        {
            var result =
                await _productService.CreateProductAsync(dto);

            if (!result.Success && result.StatusCode == 403)
            {
                return Forbid();
            }
            else if (!result.Success && result.StatusCode == 404)
            {
                return NotFound(ApiErrorFactory.Create(
                    StatusCodes.Status404NotFound,
                    result.ErrorMessage,
                    HttpContext.TraceIdentifier));
            }
            else if (!result.Success)
            {
                return BadRequest(ApiErrorFactory.Create(
                  StatusCodes.Status400BadRequest,
                  result.ErrorMessage,
                  HttpContext.TraceIdentifier));
            }

            return CreatedAtAction(
                nameof(GetProduct),
                new { id = result.Data?.Id },
                result.Data);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(
            int id,
            ProductWriteDto dto)
        {
            var result =
                await _productService.UpdateProductAsync(id, dto);

            if (!result.Success && result.StatusCode == 403)
            {
                return Forbid();
            }
            else if (!result.Success && result.StatusCode == 404)
            {
                return NotFound(ApiErrorFactory.Create(
                    StatusCodes.Status404NotFound,
                    result.ErrorMessage,
                    HttpContext.TraceIdentifier));
            }
            else if (!result.Success)
            {
                return BadRequest(ApiErrorFactory.Create(
                  StatusCodes.Status400BadRequest,
                  result.ErrorMessage,
                  HttpContext.TraceIdentifier));
            }

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            var result =
                await _productService.DeleteProductAsync(id);

            if (!result.Success && result.StatusCode == 403)
            {
                return Forbid();
            }
            else if (!result.Success && result.StatusCode == 404)
            {
                return NotFound(ApiErrorFactory.Create(
                    StatusCodes.Status404NotFound,
                    result.ErrorMessage,
                    HttpContext.TraceIdentifier));
            }
            else if (!result.Success)
            {
                return BadRequest(ApiErrorFactory.Create(
                  StatusCodes.Status400BadRequest,
                  result.ErrorMessage,
                  HttpContext.TraceIdentifier));
            }

            return NoContent();
        }
    }
}

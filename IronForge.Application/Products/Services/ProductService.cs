using IronForge.Application.Auditing.Models;
using IronForge.Application.Auditing.Services;
using IronForge.Application.Commons;
using IronForge.Application.Entities;
using IronForge.Application.Execution;
using IronForge.Application.Observablity.Logging;
using IronForge.Application.Observablity.Metrics;
using IronForge.Application.Persistence;
using IronForge.Application.Products.DTOs;
using Microsoft.Extensions.Logging;

namespace IronForge.Application.Products.Services;

public class ProductService : IProductService
{
    private readonly IRepository<Product> _products;
    private readonly ILogger<ProductService> _logger;
    private readonly IExecutionContextAccessor _executionContext;
    private readonly IProductAuthorizationService _productAuthorization;
    private readonly IAuditService _auditService;

    public ProductService(
        IRepository<Product> products,
        ILogger<ProductService> logger,
        IExecutionContextAccessor executionContext,
        IProductAuthorizationService productAuthorization,
        IAuditService auditService)
    {
        _products = products;
        _logger = logger;
        _executionContext = executionContext;
        _productAuthorization = productAuthorization;
        _auditService = auditService;
    }

    public async Task<ServiceResult<List<ProductDto>>> GetProductsAsync()
    {
        var context = _executionContext.Current;

        if (context == null)
        {
            return ServiceResult<List<ProductDto>>.Fail(
                "Execution context is not established.",
                401);
        }

        var authorization =
            await _productAuthorization.AuthorizeCollectionAsync(
                Permissions.ProductsRead);

        if (!authorization.Allowed)
        {
            return ServiceResult<List<ProductDto>>.Fail(
                authorization.Reason ??
                "You are not authorized to view products.",
                403);
        }

        var products =
            await _products.ListAsync();

        var result =
            products
                .Select(p => new ProductDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Price = p.Price
                })
                .ToList();

        return ServiceResult<List<ProductDto>>.Ok(result);
    }

    public async Task<ServiceResult<ProductDto>> GetProductAsync(int id)
    {
        var product =
            await _products.FirstOrDefaultAsync(
                p => p.Id == id);

        if (product == null)
        {
            return ServiceResult<ProductDto>.Fail(
                "Product not found.",
                404);
        }

        var authorization =
            await _productAuthorization.AuthorizeAsync(
                product,
                Permissions.ProductsRead);

        if (!authorization.Allowed)
        {
            return ServiceResult<ProductDto>.Fail(
                authorization.Reason ??
                "You are not authorized to view this product.",
                403);
        }

        return ServiceResult<ProductDto>.Ok(
            new ProductDto
            {
                Id = product.Id,
                Name = product.Name,
                Price = product.Price
            });
    }

    public async Task<ServiceResult<ProductDto>> CreateProductAsync(
        ProductWriteDto dto)
    {
        var context = _executionContext.Current;

        if (context == null)
        {
            return ServiceResult<ProductDto>.Fail(
                "Execution context is not established.",
                401);
        }

        var authorization =
            await _productAuthorization.AuthorizeCollectionAsync(
                Permissions.ProductsCreate);

        if (!authorization.Allowed)
        {
            await _auditService.RecordAsync(
                AuditCategory.Product,
                "ProductCreateDenied",
                AuditOutcome.Denied,
                resourceType: "Product",
                reason: authorization.Reason);

            return ServiceResult<ProductDto>.Fail(
                authorization.Reason ??
                "You are not authorized to create this product.",
                403);
        }

        if (!context.TenantId.HasValue)
        {
            return ServiceResult<ProductDto>.Fail(
                "Tenant context is not established.",
                403);
        }

        var userId = context.Actor.UserId;

        if (!userId.HasValue)
        {
            return ServiceResult<ProductDto>.Fail(
                "User identity is required to create a product.",
                401);
        }

        var product = new Product
        {
            Name = dto.Name,
            Price = dto.Price,
            OwnerUserId = userId.Value,
            TenantId = context.TenantId.Value
        };

        _products.Add(product);

        await _products.SaveChangesAsync();

        _logger.ProductCreated(
            product.Id,
            userId.Value);

        IronForgeBusinessMetrics.ProductsCreated.Add(1);

        await _auditService.RecordAsync(
            AuditCategory.Product,
            "ProductCreated",
            AuditOutcome.Succeeded,
            resourceType: "Product",
            resourceId: product.Id.ToString());

        return ServiceResult<ProductDto>.Ok(
            new ProductDto
            {
                Id = product.Id,
                Name = product.Name,
                Price = product.Price
            });
    }

    public async Task<ServiceResult<bool>> UpdateProductAsync(
        int id,
        ProductWriteDto dto)
    {
        var product =
            await _products.FirstOrDefaultTrackedAsync(
                p => p.Id == id);

        if (product == null)
        {
            return ServiceResult<bool>.Fail(
                "Product not found.",
                404);
        }

        var authorization =
            await _productAuthorization.AuthorizeAsync(
                product,
                Permissions.ProductsUpdate);

        if (!authorization.Allowed)
        {
            await _auditService.RecordAsync(
                AuditCategory.Product,
                "ProductUpdateDenied",
                AuditOutcome.Denied,
                resourceType: "Product",
                resourceId: product.Id.ToString(),
                reason: authorization.Reason);

            return ServiceResult<bool>.Fail(
                authorization.Reason ??
                "You are not authorized to update this product.",
                403);
        }

        product.Name = dto.Name;
        product.Price = dto.Price;

        //_products.Update(product);

        await _products.SaveChangesAsync();

        _logger.ProductUpdated(product.Id);

        IronForgeBusinessMetrics.ProductsUpdated.Add(1);

        await _auditService.RecordAsync(
            AuditCategory.Product,
            "ProductUpdated",
            AuditOutcome.Succeeded,
            resourceType: "Product",
            resourceId: product.Id.ToString());

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<bool>> DeleteProductAsync(int id)
    {
        var product =
            await _products.FirstOrDefaultAsync(
                p => p.Id == id);

        if (product == null)
        {
            return ServiceResult<bool>.Fail(
                "Product not found.",
                404);
        }

        var authorization =
            await _productAuthorization.AuthorizeAsync(
                product,
                Permissions.ProductsDelete);

        if (!authorization.Allowed)
        {
            await _auditService.RecordAsync(
                AuditCategory.Product,
                "ProductDeleteDenied",
                AuditOutcome.Denied,
                resourceType: "Product",
                resourceId: product.Id.ToString(),
                reason: authorization.Reason);

            return ServiceResult<bool>.Fail(
                authorization.Reason ??
                "You are not authorized to delete this product.",
                403);
        }

        _products.Remove(product);

        await _products.SaveChangesAsync();

        _logger.ProductDeleted(product.Id);

        IronForgeBusinessMetrics.ProductsDeleted.Add(1);

        await _auditService.RecordAsync(
            AuditCategory.Product,
            "ProductDeleted",
            AuditOutcome.Succeeded,
            resourceType: "Product",
            resourceId: product.Id.ToString());

        return ServiceResult<bool>.Ok(true);
    }
}
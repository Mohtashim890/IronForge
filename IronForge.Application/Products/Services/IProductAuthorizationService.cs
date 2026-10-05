using IronForge.Application.Auth.Models;
using IronForge.Application.Entities;

namespace IronForge.Application.Products.Services;

public interface IProductAuthorizationService
{
    Task<ResourceAuthorizationResult> AuthorizeAsync(
        Product product,
        string requiredPermission,
        CancellationToken cancellationToken = default);

    Task<ResourceAuthorizationResult> AuthorizeCollectionAsync(
        string requiredPermission,
        CancellationToken cancellationToken = default);
}
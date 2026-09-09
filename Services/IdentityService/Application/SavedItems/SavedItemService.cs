using IdentityService.Application.Abstractions;
using IdentityService.Domain.SavedItems;

namespace IdentityService.Application.SavedItems;

public sealed class SavedItemService(ISavedItemRepository repository)
{
    public Task<IReadOnlyList<SavedItem>> GetAsync(Guid customerId, CancellationToken cancellationToken) =>
        repository.GetByCustomerAsync(customerId, cancellationToken);

    public Task<bool> SaveAsync(Guid customerId, Guid productId, CancellationToken cancellationToken) =>
        repository.AddAsync(new SavedItem(customerId, productId, DateTime.UtcNow), cancellationToken);

    public Task<bool> RemoveAsync(Guid customerId, Guid productId, CancellationToken cancellationToken) =>
        repository.RemoveAsync(customerId, productId, cancellationToken);
}
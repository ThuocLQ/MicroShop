using IdentityService.Domain.SavedItems;

namespace IdentityService.Application.Abstractions;

public interface ISavedItemRepository
{
    Task<IReadOnlyList<SavedItem>> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task<bool> AddAsync(SavedItem item, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(Guid customerId, Guid productId, CancellationToken cancellationToken = default);
}
namespace IdentityService.Domain.SavedItems;

public sealed class SavedItem
{
    public SavedItem(Guid customerId, Guid productId, DateTime createdAtUtc)
    {
        if (customerId == Guid.Empty) throw new ArgumentException("Customer id is required.", nameof(customerId));
        if (productId == Guid.Empty) throw new ArgumentException("Product id is required.", nameof(productId));
        CustomerId = customerId;
        ProductId = productId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid CustomerId { get; }
    public Guid ProductId { get; }
    public DateTime CreatedAtUtc { get; }
}
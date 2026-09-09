using Dapper;
using IdentityService.Application.Abstractions;
using IdentityService.Domain.SavedItems;

namespace IdentityService.Infrastructure.Persistence;

public sealed class DapperSavedItemRepository(IDbConnectionFactory connectionFactory) : ISavedItemRepository
{
    public async Task<IReadOnlyList<SavedItem>> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<Row>(new CommandDefinition("SELECT CustomerId, ProductId, CreatedAtUtc FROM CustomerSavedItems WHERE CustomerId = @CustomerId ORDER BY CreatedAtUtc DESC, ProductId;", new { CustomerId = customerId }, cancellationToken: cancellationToken));
        return rows.Select(row => new SavedItem(row.CustomerId, row.ProductId, row.CreatedAtUtc)).ToList();
    }

    public async Task<bool> AddAsync(SavedItem item, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition("INSERT INTO CustomerSavedItems (CustomerId, ProductId, CreatedAtUtc) VALUES (@CustomerId, @ProductId, @CreatedAtUtc) ON CONFLICT (CustomerId, ProductId) DO NOTHING;", item, cancellationToken: cancellationToken)) == 1;
    }

    public async Task<bool> RemoveAsync(Guid customerId, Guid productId, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition("DELETE FROM CustomerSavedItems WHERE CustomerId = @CustomerId AND ProductId = @ProductId;", new { CustomerId = customerId, ProductId = productId }, cancellationToken: cancellationToken)) == 1;
    }

    private sealed record Row(Guid CustomerId, Guid ProductId, DateTime CreatedAtUtc);
}
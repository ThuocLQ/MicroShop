using Dapper;
using Npgsql;
using PaymentService.Application.Abstractions;
using PaymentService.Domain.Payments;

namespace PaymentService.Infrastructure.Persistence;

public sealed class DapperPaymentRepository : IPaymentRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DapperPaymentRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Payment> CreateAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO Payments (
                Id,
                OrderId,
                CustomerId,
                Amount,
                Currency,
                Status,
                ProviderTransactionId,
                FailureReason,
                CreatedAtUtc,
                CompletedAtUtc,
                AuthorizedAtUtc,
                CaptureRequestedAtUtc,
                CapturedAtUtc,
                VoidRequestedAtUtc,
                VoidedAtUtc,
                RefundRequestedAtUtc,
                RefundedAtUtc,
                Provider,
                ProviderSessionId,
                PaymentActionIdempotencyKey,
                PaymentActionRequestHash,
                PaymentActionExpiresAtUtc,
                ProviderCheckoutUrl)
            VALUES (
                @Id,
                @OrderId,
                @CustomerId,
                @Amount,
                @Currency,
                @Status,
                @ProviderTransactionId,
                @FailureReason,
                @CreatedAtUtc,
                @CompletedAtUtc,
                @AuthorizedAtUtc,
                @CaptureRequestedAtUtc,
                @CapturedAtUtc,
                @VoidRequestedAtUtc,
                @VoidedAtUtc,
                @RefundRequestedAtUtc,
                @RefundedAtUtc,
                @Provider,
                @ProviderSessionId,
                @PaymentActionIdempotencyKey,
                @PaymentActionRequestHash,
                @PaymentActionExpiresAtUtc,
                @ProviderCheckoutUrl);
            """, ToParameters(payment), cancellationToken: cancellationToken));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            var existingPayment = await GetByOrderIdAsync(payment.OrderId, cancellationToken);
            if (existingPayment is not null)
            {
                return existingPayment;
            }

            throw;
        }

        return payment;
    }

    public async Task<Payment> CreateAsync(
        Payment payment,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        await transaction.Connection!.ExecuteAsync(new CommandDefinition("""
            INSERT INTO Payments (
                Id, OrderId, CustomerId, Amount, Currency, Status, ProviderTransactionId, FailureReason,
                CreatedAtUtc, CompletedAtUtc, AuthorizedAtUtc, CaptureRequestedAtUtc, CapturedAtUtc,
                VoidRequestedAtUtc, VoidedAtUtc, RefundRequestedAtUtc, RefundedAtUtc, Provider,
                ProviderSessionId, PaymentActionIdempotencyKey, PaymentActionRequestHash,
                PaymentActionExpiresAtUtc, ProviderCheckoutUrl)
            VALUES (
                @Id, @OrderId, @CustomerId, @Amount, @Currency, @Status, @ProviderTransactionId, @FailureReason,
                @CreatedAtUtc, @CompletedAtUtc, @AuthorizedAtUtc, @CaptureRequestedAtUtc, @CapturedAtUtc,
                @VoidRequestedAtUtc, @VoidedAtUtc, @RefundRequestedAtUtc, @RefundedAtUtc, @Provider,
                @ProviderSessionId, @PaymentActionIdempotencyKey, @PaymentActionRequestHash,
                @PaymentActionExpiresAtUtc, @ProviderCheckoutUrl);
            """, ToParameters(payment), transaction, cancellationToken: cancellationToken));

        return payment;
    }

    public async Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        return await GetByIdAsync(connection, null, id, cancellationToken);
    }

    public Task<Payment?> GetByIdAsync(
        Guid id,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        return GetByIdAsync(transaction.Connection!, transaction, id, cancellationToken);
    }

    private static async Task<Payment?> GetByIdAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction? transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        var sql = transaction is null
            ? """
            SELECT Id, OrderId, CustomerId, Amount, Currency, Status, ProviderTransactionId, FailureReason, CreatedAtUtc, CompletedAtUtc,
                   AuthorizedAtUtc, CaptureRequestedAtUtc, CapturedAtUtc, VoidRequestedAtUtc, VoidedAtUtc,
                   RefundRequestedAtUtc, RefundedAtUtc, Provider, ProviderSessionId, PaymentActionIdempotencyKey,
                   PaymentActionRequestHash, PaymentActionExpiresAtUtc, ProviderCheckoutUrl
            FROM Payments
            WHERE Id = @Id;
            """
            : """
            SELECT Id, OrderId, CustomerId, Amount, Currency, Status, ProviderTransactionId, FailureReason, CreatedAtUtc, CompletedAtUtc,
                   AuthorizedAtUtc, CaptureRequestedAtUtc, CapturedAtUtc, VoidRequestedAtUtc, VoidedAtUtc,
                   RefundRequestedAtUtc, RefundedAtUtc, Provider, ProviderSessionId, PaymentActionIdempotencyKey,
                   PaymentActionRequestHash, PaymentActionExpiresAtUtc, ProviderCheckoutUrl
            FROM Payments
            WHERE Id = @Id
            FOR UPDATE;
            """;

        var row = await connection.QuerySingleOrDefaultAsync<PaymentRow>(new CommandDefinition(
            sql,
            new { Id = id },
            transaction,
            cancellationToken: cancellationToken));

        return row is null ? null : MapPayment(row);
    }

    public async Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var row = await connection.QuerySingleOrDefaultAsync<PaymentRow>(new CommandDefinition("""
            SELECT Id, OrderId, CustomerId, Amount, Currency, Status, ProviderTransactionId, FailureReason, CreatedAtUtc, CompletedAtUtc,
                   AuthorizedAtUtc, CaptureRequestedAtUtc, CapturedAtUtc, VoidRequestedAtUtc, VoidedAtUtc,
                   RefundRequestedAtUtc, RefundedAtUtc, Provider, ProviderSessionId, PaymentActionIdempotencyKey,
                   PaymentActionRequestHash, PaymentActionExpiresAtUtc, ProviderCheckoutUrl
            FROM Payments
            WHERE OrderId = @OrderId;
            """, new { OrderId = orderId }, cancellationToken: cancellationToken));

        return row is null ? null : MapPayment(row);
    }

    public async Task<Payment?> GetByProviderSessionIdAsync(
        string provider,
        string providerSessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(providerSessionId))
        {
            return null;
        }

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<PaymentRow>(new CommandDefinition("""
            SELECT Id, OrderId, CustomerId, Amount, Currency, Status, ProviderTransactionId, FailureReason, CreatedAtUtc, CompletedAtUtc,
                   AuthorizedAtUtc, CaptureRequestedAtUtc, CapturedAtUtc, VoidRequestedAtUtc, VoidedAtUtc,
                   RefundRequestedAtUtc, RefundedAtUtc, Provider, ProviderSessionId, PaymentActionIdempotencyKey,
                   PaymentActionRequestHash, PaymentActionExpiresAtUtc, ProviderCheckoutUrl
            FROM Payments
            WHERE Provider = @Provider
              AND ProviderSessionId = @ProviderSessionId;
            """, new { Provider = provider.Trim(), ProviderSessionId = providerSessionId.Trim() }, cancellationToken: cancellationToken));

        return row is null ? null : MapPayment(row);
    }
    public async Task<IReadOnlyList<Payment>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<PaymentRow>(new CommandDefinition("""
            SELECT Id, OrderId, CustomerId, Amount, Currency, Status, ProviderTransactionId, FailureReason, CreatedAtUtc, CompletedAtUtc,
                   AuthorizedAtUtc, CaptureRequestedAtUtc, CapturedAtUtc, VoidRequestedAtUtc, VoidedAtUtc,
                   RefundRequestedAtUtc, RefundedAtUtc, Provider, ProviderSessionId, PaymentActionIdempotencyKey,
                   PaymentActionRequestHash, PaymentActionExpiresAtUtc, ProviderCheckoutUrl
            FROM Payments
            ORDER BY CreatedAtUtc DESC
            LIMIT @Limit;
            """, new { Limit = limit }, cancellationToken: cancellationToken));

        return rows.Select(MapPayment).ToList();
    }

    public async Task<Payment?> GetByCustomerAndActionIdempotencyKeyAsync(
        Guid customerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var row = await connection.QuerySingleOrDefaultAsync<PaymentRow>(new CommandDefinition("""
            SELECT Id, OrderId, CustomerId, Amount, Currency, Status, ProviderTransactionId, FailureReason, CreatedAtUtc, CompletedAtUtc,
                   AuthorizedAtUtc, CaptureRequestedAtUtc, CapturedAtUtc, VoidRequestedAtUtc, VoidedAtUtc,
                   RefundRequestedAtUtc, RefundedAtUtc, Provider, ProviderSessionId, PaymentActionIdempotencyKey,
                   PaymentActionRequestHash, PaymentActionExpiresAtUtc, ProviderCheckoutUrl
            FROM Payments
            WHERE CustomerId = @CustomerId
              AND PaymentActionIdempotencyKey = @IdempotencyKey;
            """, new { CustomerId = customerId, IdempotencyKey = idempotencyKey }, cancellationToken: cancellationToken));

        return row is null ? null : MapPayment(row);
    }
    public async Task<bool> UpdateAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        return await UpdateAsync(connection, null, payment, cancellationToken);
    }

    public Task<bool> UpdateAsync(
        Payment payment,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        return UpdateAsync(transaction.Connection!, transaction, payment, cancellationToken);
    }

    private static async Task<bool> UpdateAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction? transaction,
        Payment payment,
        CancellationToken cancellationToken)
    {
        var affectedRows = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE Payments
            SET Status = @Status,
                ProviderTransactionId = @ProviderTransactionId,
                FailureReason = @FailureReason,
                CompletedAtUtc = @CompletedAtUtc,
                AuthorizedAtUtc = @AuthorizedAtUtc,
                CaptureRequestedAtUtc = @CaptureRequestedAtUtc,
                CapturedAtUtc = @CapturedAtUtc,
                VoidRequestedAtUtc = @VoidRequestedAtUtc,
                VoidedAtUtc = @VoidedAtUtc,
                RefundRequestedAtUtc = @RefundRequestedAtUtc,
                RefundedAtUtc = @RefundedAtUtc
            WHERE Id = @Id;
            """, ToParameters(payment), transaction, cancellationToken: cancellationToken));

        return affectedRows > 0;
    }

    private static object ToParameters(Payment payment)
    {
        return new
        {
            payment.Id,
            payment.OrderId,
            payment.CustomerId,
            payment.Amount,
            payment.Currency,
            Status = payment.Status.ToString(),
            payment.ProviderTransactionId,
            payment.FailureReason,
            payment.CreatedAtUtc,
            payment.CompletedAtUtc,
            payment.AuthorizedAtUtc,
            payment.CaptureRequestedAtUtc,
            payment.CapturedAtUtc,
            payment.VoidRequestedAtUtc,
            payment.VoidedAtUtc,
            payment.RefundRequestedAtUtc,
            payment.RefundedAtUtc,
            payment.Provider,
            payment.ProviderSessionId,
            payment.PaymentActionIdempotencyKey,
            payment.PaymentActionRequestHash,
            payment.PaymentActionExpiresAtUtc,
            payment.ProviderCheckoutUrl
        };
    }

    private static Payment MapPayment(PaymentRow row)
    {
        return new Payment(
            row.Id,
            row.OrderId,
            row.CustomerId,
            row.Amount,
            row.Currency,
            Enum.Parse<PaymentStatus>(row.Status),
            row.CreatedAtUtc,
            row.ProviderTransactionId,
            row.FailureReason,
            row.CompletedAtUtc,
            row.AuthorizedAtUtc,
            row.CaptureRequestedAtUtc,
            row.CapturedAtUtc,
            row.VoidRequestedAtUtc,
            row.VoidedAtUtc,
            row.RefundRequestedAtUtc,
            row.RefundedAtUtc,
            row.Provider,
            row.ProviderSessionId,
            row.PaymentActionIdempotencyKey,
            row.PaymentActionRequestHash,
            row.PaymentActionExpiresAtUtc,
            row.ProviderCheckoutUrl);
    }

    private sealed record PaymentRow(
        Guid Id,
        Guid OrderId,
        Guid CustomerId,
        decimal Amount,
        string Currency,
        string Status,
        string? ProviderTransactionId,
        string? FailureReason,
        DateTime CreatedAtUtc,
        DateTime? CompletedAtUtc,
        DateTime? AuthorizedAtUtc,
        DateTime? CaptureRequestedAtUtc,
        DateTime? CapturedAtUtc,
        DateTime? VoidRequestedAtUtc,
        DateTime? VoidedAtUtc,
        DateTime? RefundRequestedAtUtc,
        DateTime? RefundedAtUtc,
        string? Provider,
        string? ProviderSessionId,
        string? PaymentActionIdempotencyKey,
        string? PaymentActionRequestHash,
        DateTime? PaymentActionExpiresAtUtc,
        string? ProviderCheckoutUrl);
}

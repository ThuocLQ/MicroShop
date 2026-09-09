using Dapper;
using OrderingService.Application.Abstractions;
using OrderingService.Domain.OrderPaymentSagas;

namespace OrderingService.Infrastructure.Persistence;

public sealed class DapperOrderPaymentSagaRepository : IOrderPaymentSagaRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DapperOrderPaymentSagaRepository(IDbConnectionFactory connectionFactory) => _connectionFactory = connectionFactory;

    public async Task<IReadOnlyList<OrderPaymentSaga>> GetTimedOutAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<OrderPaymentSagaRow>(new CommandDefinition("""
            SELECT Id, OrderId, PaymentId, State, StartedAtUtc, UpdatedAtUtc, TimeoutAtUtc, LastProcessedEventId, ExpectedInventoryCommandEventId, LastError
            FROM OrderPaymentSagas
            WHERE State IN ('PaymentRequested', 'PaymentAuthorized', 'CaptureRequested', 'CashOnDeliveryRequested')
              AND TimeoutAtUtc <= @NowUtc
            ORDER BY TimeoutAtUtc
            LIMIT @BatchSize;
            """, new { NowUtc = nowUtc, BatchSize = batchSize }, cancellationToken: cancellationToken));

        return rows.Select(Map).ToList();
    }

    public async Task<OrderPaymentSaga?> GetByOrderIdAsync(
        Guid orderId,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var row = await transaction.Connection!.QuerySingleOrDefaultAsync<OrderPaymentSagaRow>(new CommandDefinition("""
            SELECT Id, OrderId, PaymentId, State, StartedAtUtc, UpdatedAtUtc, TimeoutAtUtc, LastProcessedEventId, ExpectedInventoryCommandEventId, LastError
            FROM OrderPaymentSagas
            WHERE OrderId = @OrderId
            FOR UPDATE;
            """, new { OrderId = orderId }, transaction, cancellationToken: cancellationToken));

        return row is null ? null : Map(row);
    }

    public async Task UpsertAsync(
        OrderPaymentSaga saga,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        await transaction.Connection!.ExecuteAsync(new CommandDefinition("""
            INSERT INTO OrderPaymentSagas (
                Id,
                OrderId,
                PaymentId,
                State,
                StartedAtUtc,
                UpdatedAtUtc,
                TimeoutAtUtc,
                LastProcessedEventId,
                ExpectedInventoryCommandEventId,
                LastError)
            VALUES (
                @Id,
                @OrderId,
                @PaymentId,
                @State,
                @StartedAtUtc,
                @UpdatedAtUtc,
                @TimeoutAtUtc,
                @LastProcessedEventId,
                @ExpectedInventoryCommandEventId,
                @LastError)
            ON CONFLICT (OrderId) DO UPDATE
            SET PaymentId = EXCLUDED.PaymentId,
                State = EXCLUDED.State,
                UpdatedAtUtc = EXCLUDED.UpdatedAtUtc,
                TimeoutAtUtc = EXCLUDED.TimeoutAtUtc,
                LastProcessedEventId = EXCLUDED.LastProcessedEventId,
                ExpectedInventoryCommandEventId = EXCLUDED.ExpectedInventoryCommandEventId,
                LastError = EXCLUDED.LastError;
            """, ToParameters(saga), transaction, cancellationToken: cancellationToken));
    }

    private static object ToParameters(OrderPaymentSaga saga)
    {
        return new
        {
            saga.Id,
            saga.OrderId,
            saga.PaymentId,
            State = saga.State.ToString(),
            saga.StartedAtUtc,
            saga.UpdatedAtUtc,
            saga.TimeoutAtUtc,
            saga.LastProcessedEventId,
            saga.ExpectedInventoryCommandEventId,
            saga.LastError
        };
    }

    private static OrderPaymentSaga Map(OrderPaymentSagaRow row)
    {
        return new OrderPaymentSaga(
            row.Id,
            row.OrderId,
            row.PaymentId,
            Enum.Parse<OrderPaymentSagaState>(row.State),
            row.StartedAtUtc,
            row.UpdatedAtUtc,
            row.TimeoutAtUtc,
            row.LastProcessedEventId,
            row.ExpectedInventoryCommandEventId,
            row.LastError);
    }

    private sealed record OrderPaymentSagaRow(
        Guid Id,
        Guid OrderId,
        Guid PaymentId,
        string State,
        DateTime StartedAtUtc,
        DateTime UpdatedAtUtc,
        DateTime TimeoutAtUtc,
        Guid? LastProcessedEventId,
        Guid? ExpectedInventoryCommandEventId,
        string? LastError);
}

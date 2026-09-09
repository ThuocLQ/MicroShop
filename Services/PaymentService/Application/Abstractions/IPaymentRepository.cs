using System.Data;
using PaymentService.Domain.Payments;

namespace PaymentService.Application.Abstractions;

public interface IPaymentRepository
{
    Task<Payment> CreateAsync(Payment payment, CancellationToken cancellationToken = default);
    Task<Payment> CreateAsync(Payment payment, IDbTransaction transaction, CancellationToken cancellationToken = default) =>
        CreateAsync(payment, cancellationToken);
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Payment?> GetByIdAsync(
        Guid id,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);
    Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<Payment?> GetByProviderSessionIdAsync(string provider, string providerSessionId, CancellationToken cancellationToken = default);
    Task<Payment?> GetByCustomerAndActionIdempotencyKeyAsync(
        Guid customerId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Payment>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Payment payment, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(
        Payment payment,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);
}

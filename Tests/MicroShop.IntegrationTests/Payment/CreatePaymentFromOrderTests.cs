using System.Data;
using PaymentService.Application.Abstractions;
using PaymentService.Application.Payments.CreatePayment;
using PaymentService.Application.Payments.Providers;
using PaymentService.Domain.Payments;
using DomainPayment = PaymentService.Domain.Payments.Payment;

namespace MicroShop.IntegrationTests.Payment;

public sealed class CreatePaymentFromOrderTests
{
    [Fact]
    public async Task CreatePayment_UsesAuthoritativeOrderSnapshot_AndReturnsSandboxAction()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var repository = new StubPaymentRepository();
        var handler = new CreatePaymentHandler(repository,
            new StubOrderPaymentClient(new OrderPaymentSnapshot(orderId, customerId, 125_000m, "vnd", "PendingPayment")),
            new StubPaymentProvider());

        var result = await handler.Handle(new CreatePaymentCommand(orderId, customerId, "payment-action-001", null), TestContext.Current.CancellationToken);

        Assert.Equal(orderId, result.Payment.OrderId);
        Assert.Equal(customerId, result.Payment.CustomerId);
        Assert.Equal(125_000m, result.Payment.Amount);
        Assert.Equal("VND", result.Payment.Currency);
        Assert.Equal("Sandbox", result.Action.Provider);
        Assert.Equal("PendingAuthorization", result.Action.PaymentStatus);
        Assert.False(result.IsReplay);
        Assert.Equal(1, repository.CreateCalls);
    }

    [Fact]
    public async Task CreatePayment_WithSameKeyAndIntent_ReplaysTheSameAction()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var repository = new StubPaymentRepository();
        var handler = new CreatePaymentHandler(repository,
            new StubOrderPaymentClient(new OrderPaymentSnapshot(orderId, customerId, 75_000m, "USD", "PendingPayment")),
            new StubPaymentProvider());

        var first = await handler.Handle(new CreatePaymentCommand(orderId, customerId, "payment-action-002", null), TestContext.Current.CancellationToken);
        var replay = await handler.Handle(new CreatePaymentCommand(orderId, customerId, "payment-action-002", null), TestContext.Current.CancellationToken);

        Assert.Equal(first.Payment.Id, replay.Payment.Id);
        Assert.Equal(first.Action.SessionId, replay.Action.SessionId);
        Assert.True(replay.IsReplay);
        Assert.Equal(1, repository.CreateCalls);
    }

    [Fact]
    public async Task CreatePayment_WithDifferentKeyForExistingOrder_Conflicts()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var repository = new StubPaymentRepository();
        var handler = new CreatePaymentHandler(repository,
            new StubOrderPaymentClient(new OrderPaymentSnapshot(orderId, customerId, 75_000m, "USD", "PendingPayment")),
            new StubPaymentProvider());

        await handler.Handle(new CreatePaymentCommand(orderId, customerId, "payment-action-003a", null), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<PaymentActionIdempotencyConflictException>(() =>
            handler.Handle(new CreatePaymentCommand(orderId, customerId, "payment-action-003b", null), TestContext.Current.CancellationToken));
        Assert.Equal(1, repository.CreateCalls);
    }

    [Fact]
    public async Task CreatePayment_ForAnotherCustomersOrder_IsNotAccessibleAndDoesNotCreatePayment()
    {
        var orderId = Guid.NewGuid();
        var repository = new StubPaymentRepository();
        var handler = new CreatePaymentHandler(repository,
            new StubOrderPaymentClient(new OrderPaymentSnapshot(orderId, Guid.NewGuid(), 125_000m, "VND", "PendingPayment")),
            new StubPaymentProvider());

        await Assert.ThrowsAsync<PaymentOrderNotAccessibleException>(() => handler.Handle(
            new CreatePaymentCommand(orderId, Guid.NewGuid(), "payment-action-004", null), TestContext.Current.CancellationToken));
        Assert.Equal(0, repository.CreateCalls);
    }

    private sealed class StubOrderPaymentClient(OrderPaymentSnapshot? order) : IOrderPaymentClient
    {
        public Task<OrderPaymentSnapshot?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult(order);
    }

    private sealed class StubPaymentProvider : IPaymentProvider, IPaymentProviderResolver
    {
        public string Name => "Sandbox";
        public IPaymentProvider Resolve(string? providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName) || string.Equals(providerName, Name, StringComparison.OrdinalIgnoreCase)) return this;
            throw new ArgumentException("Provider is unavailable.", nameof(providerName));
        }

        public IReadOnlyList<PaymentProviderDescriptor> GetAvailableProviders() =>
            [new PaymentProviderDescriptor(Name, true, false, PaymentProviderPolicy.GetSupportedCurrencies(Name))];
        public Task<PaymentProviderAction> CreateActionAsync(PaymentProviderActionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentProviderAction(Name, $"sandbox-session-{request.PaymentId:N}", null, DateTime.UtcNow.AddMinutes(30)));
        public Task<PaymentProviderWebhook?> RequestCaptureAsync(DomainPayment payment, CancellationToken cancellationToken = default) => Task.FromResult<PaymentProviderWebhook?>(null);
        public Task<PaymentProviderWebhook?> RequestVoidAsync(DomainPayment payment, CancellationToken cancellationToken = default) => Task.FromResult<PaymentProviderWebhook?>(null);
        public Task<PaymentProviderWebhook?> RequestRefundAsync(DomainPayment payment, CancellationToken cancellationToken = default) => Task.FromResult<PaymentProviderWebhook?>(null);
    }

    private sealed class StubPaymentRepository : IPaymentRepository
    {
        private DomainPayment? _payment;
        public int CreateCalls { get; private set; }
        public Task<DomainPayment> CreateAsync(DomainPayment payment, CancellationToken cancellationToken = default) { CreateCalls++; _payment = payment; return Task.FromResult(payment); }
        public Task<DomainPayment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_payment?.Id == id ? _payment : null);
        public Task<DomainPayment?> GetByIdAsync(Guid id, IDbTransaction transaction, CancellationToken cancellationToken = default) => Task.FromResult(_payment?.Id == id ? _payment : null);
        public Task<DomainPayment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult(_payment?.OrderId == orderId ? _payment : null);
        public Task<DomainPayment?> GetByProviderSessionIdAsync(string provider, string providerSessionId, CancellationToken cancellationToken = default) => Task.FromResult(_payment?.Provider == provider && _payment.ProviderSessionId == providerSessionId ? _payment : null);
        public Task<DomainPayment?> GetByCustomerAndActionIdempotencyKeyAsync(Guid customerId, string idempotencyKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(_payment?.CustomerId == customerId && _payment.PaymentActionIdempotencyKey == idempotencyKey ? _payment : null);
        public Task<IReadOnlyList<DomainPayment>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DomainPayment>>(_payment is null ? [] : [_payment]);
        public Task<bool> UpdateAsync(DomainPayment payment, CancellationToken cancellationToken = default) { _payment = payment; return Task.FromResult(true); }
        public Task<bool> UpdateAsync(DomainPayment payment, IDbTransaction transaction, CancellationToken cancellationToken = default) { _payment = payment; return Task.FromResult(true); }
    }
}

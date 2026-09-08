namespace PaymentService.Domain.Payments;

public sealed class Payment
{
    public Guid Id { get; }
    public Guid OrderId { get; }
    public Guid CustomerId { get; }
    public decimal Amount { get; }
    public string Currency { get; }
    public PaymentStatus Status { get; private set; }
    public string? ProviderTransactionId { get; private set; }
    public string? Provider { get; }
    public string? ProviderSessionId { get; }
    public string? ProviderCheckoutUrl { get; }
    public string? PaymentActionIdempotencyKey { get; }
    public string? PaymentActionRequestHash { get; }
    public DateTime? PaymentActionExpiresAtUtc { get; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAtUtc { get; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? AuthorizedAtUtc { get; private set; }
    public DateTime? CaptureRequestedAtUtc { get; private set; }
    public DateTime? CapturedAtUtc { get; private set; }
    public DateTime? VoidRequestedAtUtc { get; private set; }
    public DateTime? VoidedAtUtc { get; private set; }
    public DateTime? RefundRequestedAtUtc { get; private set; }
    public DateTime? RefundedAtUtc { get; private set; }

    public Payment(
        Guid id,
        Guid orderId,
        Guid customerId,
        decimal amount,
        string currency,
        PaymentStatus status,
        DateTime createdAtUtc,
        string? providerTransactionId = null,
        string? failureReason = null,
        DateTime? completedAtUtc = null,
        DateTime? authorizedAtUtc = null,
        DateTime? captureRequestedAtUtc = null,
        DateTime? capturedAtUtc = null,
        DateTime? voidRequestedAtUtc = null,
        DateTime? voidedAtUtc = null,
        DateTime? refundRequestedAtUtc = null,
        DateTime? refundedAtUtc = null,
        string? provider = null,
        string? providerSessionId = null,
        string? paymentActionIdempotencyKey = null,
        string? paymentActionRequestHash = null,
        DateTime? paymentActionExpiresAtUtc = null,
        string? providerCheckoutUrl = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Payment id cannot be empty.", nameof(id));
        if (orderId == Guid.Empty) throw new ArgumentException("Order id cannot be empty.", nameof(orderId));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer id cannot be empty.", nameof(customerId));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Currency is required.", nameof(currency));

        Id = id;
        OrderId = orderId;
        CustomerId = customerId;
        Amount = amount;
        Currency = currency.Trim().ToUpperInvariant();
        Status = status;
        ProviderTransactionId = string.IsNullOrWhiteSpace(providerTransactionId) ? null : providerTransactionId.Trim();
        Provider = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim();
        ProviderSessionId = string.IsNullOrWhiteSpace(providerSessionId) ? null : providerSessionId.Trim();
        PaymentActionIdempotencyKey = string.IsNullOrWhiteSpace(paymentActionIdempotencyKey) ? null : paymentActionIdempotencyKey.Trim();
        PaymentActionRequestHash = string.IsNullOrWhiteSpace(paymentActionRequestHash) ? null : paymentActionRequestHash.Trim().ToLowerInvariant();
        PaymentActionExpiresAtUtc = paymentActionExpiresAtUtc;
        ProviderCheckoutUrl = NormalizeCheckoutUrl(providerCheckoutUrl);

        if (PaymentActionIdempotencyKey?.Length > 128)
        {
            throw new ArgumentException("Payment action idempotency key cannot exceed 128 characters.", nameof(paymentActionIdempotencyKey));
        }

        if (PaymentActionRequestHash is not null &&
            (PaymentActionRequestHash.Length != 64 || PaymentActionRequestHash.Any(character => !Uri.IsHexDigit(character))))
        {
            throw new ArgumentException("Payment action request hash must be a SHA-256 hexadecimal value.", nameof(paymentActionRequestHash));
        }

        if (ProviderCheckoutUrl is not null && ProviderSessionId is null)
        {
            throw new ArgumentException("A hosted checkout URL requires a provider session.", nameof(providerCheckoutUrl));
        }

        if (ProviderSessionId is not null && PaymentActionExpiresAtUtc is null)
        {
            throw new ArgumentException("A payment provider session requires an expiry.", nameof(paymentActionExpiresAtUtc));
        }
        FailureReason = string.IsNullOrWhiteSpace(failureReason) ? null : failureReason.Trim();
        CreatedAtUtc = createdAtUtc;
        CompletedAtUtc = completedAtUtc;
        AuthorizedAtUtc = authorizedAtUtc;
        CaptureRequestedAtUtc = captureRequestedAtUtc;
        CapturedAtUtc = capturedAtUtc;
        VoidRequestedAtUtc = voidRequestedAtUtc;
        VoidedAtUtc = voidedAtUtc;
        RefundRequestedAtUtc = refundRequestedAtUtc;
        RefundedAtUtc = refundedAtUtc;
    }

    public void MarkAuthorized(string providerTransactionId, DateTime authorizedAtUtc)
    {
        if (Status is PaymentStatus.Authorized
            or PaymentStatus.CapturePending
            or PaymentStatus.Captured
            or PaymentStatus.VoidPending
            or PaymentStatus.Voided
            or PaymentStatus.RefundPending
            or PaymentStatus.Refunded
            or PaymentStatus.ReconciliationRequired)
        {
            return;
        }

        if (Status != PaymentStatus.PendingAuthorization)
        {
            throw new InvalidOperationException($"Payment in status '{Status}' cannot be authorized.");
        }

        SetProviderTransactionId(providerTransactionId);
        Status = PaymentStatus.Authorized;
        FailureReason = null;
        AuthorizedAtUtc = authorizedAtUtc;
    }

    public void RequestCapture(DateTime requestedAtUtc)
    {
        if (Status is PaymentStatus.CapturePending or PaymentStatus.Captured)
        {
            return;
        }

        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException($"Payment in status '{Status}' cannot be captured.");
        }

        Status = PaymentStatus.CapturePending;
        CaptureRequestedAtUtc = requestedAtUtc;
    }

    public void MarkCaptured(string providerTransactionId, DateTime capturedAtUtc, bool allowProviderAutoCapture = false)
    {
        if (Status is PaymentStatus.Captured or PaymentStatus.RefundPending or PaymentStatus.Refunded)
        {
            return;
        }

        if (Status == PaymentStatus.Voided)
        {
            SetProviderTransactionId(providerTransactionId);
            Status = PaymentStatus.ReconciliationRequired;
            FailureReason = "Provider reported capture after void confirmation; refund reconciliation is required.";
            CapturedAtUtc = capturedAtUtc;
            CompletedAtUtc = capturedAtUtc;
            return;
        }

        // The standard path requires a local capture request. A verified provider
        // auto-capture is the sole exception and is explicitly marked by its adapter.
        if (Status is not (PaymentStatus.CapturePending or PaymentStatus.VoidPending) &&
            !(allowProviderAutoCapture && Status == PaymentStatus.Authorized))
        {
            throw new InvalidOperationException($"Payment in status '{Status}' cannot be captured.");
        }

        SetProviderTransactionId(providerTransactionId);
        Status = PaymentStatus.Captured;
        FailureReason = null;
        CapturedAtUtc = capturedAtUtc;
        CompletedAtUtc = capturedAtUtc;
    }

    public void RequestVoid(DateTime requestedAtUtc)
    {
        if (Status is PaymentStatus.VoidPending or PaymentStatus.Voided)
        {
            return;
        }

        if (Status is not (PaymentStatus.Authorized or PaymentStatus.CapturePending))
        {
            throw new InvalidOperationException($"Payment in status '{Status}' cannot be voided.");
        }

        Status = PaymentStatus.VoidPending;
        VoidRequestedAtUtc = requestedAtUtc;
    }

    public void MarkVoided(DateTime voidedAtUtc)
    {
        if (Status == PaymentStatus.Voided)
        {
            return;
        }

        if (Status != PaymentStatus.VoidPending)
        {
            throw new InvalidOperationException($"Payment in status '{Status}' cannot be marked as voided.");
        }

        Status = PaymentStatus.Voided;
        FailureReason = null;
        VoidedAtUtc = voidedAtUtc;
        CompletedAtUtc = voidedAtUtc;
    }

    public void RequestRefund(DateTime requestedAtUtc)
    {
        if (Status is PaymentStatus.RefundPending or PaymentStatus.Refunded)
        {
            return;
        }

        if (Status is not (PaymentStatus.Captured or PaymentStatus.ReconciliationRequired))
        {
            throw new InvalidOperationException($"Payment in status '{Status}' cannot be refunded.");
        }

        Status = PaymentStatus.RefundPending;
        RefundRequestedAtUtc = requestedAtUtc;
    }

    public void MarkRefunded(DateTime refundedAtUtc)
    {
        if (Status == PaymentStatus.Refunded)
        {
            return;
        }

        if (Status != PaymentStatus.RefundPending)
        {
            throw new InvalidOperationException($"Payment in status '{Status}' cannot be marked as refunded.");
        }

        Status = PaymentStatus.Refunded;
        FailureReason = null;
        RefundedAtUtc = refundedAtUtc;
        CompletedAtUtc = refundedAtUtc;
    }

    // Compatibility entry point for the existing SUCCEEDED webhook.
    public void MarkSucceeded(string providerTransactionId, DateTime completedAtUtc)
    {
        MarkCaptured(providerTransactionId, completedAtUtc);
    }

    public void MarkFailed(string reason, DateTime completedAtUtc)
    {
        if (Status is PaymentStatus.Failed
            or PaymentStatus.VoidPending
            or PaymentStatus.Voided
            or PaymentStatus.RefundPending
            or PaymentStatus.Refunded
            or PaymentStatus.ReconciliationRequired)
        {
            return;
        }

        if (Status != PaymentStatus.PendingAuthorization)
        {
            throw new InvalidOperationException($"Payment in status '{Status}' cannot be marked as failed.");
        }

        Status = PaymentStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Payment failed." : reason.Trim();
        CompletedAtUtc = completedAtUtc;
    }

    private static string? NormalizeCheckoutUrl(string? providerCheckoutUrl)
    {
        if (string.IsNullOrWhiteSpace(providerCheckoutUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(providerCheckoutUrl.Trim(), UriKind.Absolute, out var checkoutUri) ||
            !string.Equals(checkoutUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Provider checkout URL must be an absolute HTTPS URL.", nameof(providerCheckoutUrl));
        }

        return checkoutUri.ToString();
    }
    private void SetProviderTransactionId(string providerTransactionId)
    {
        if (string.IsNullOrWhiteSpace(providerTransactionId))
        {
            throw new ArgumentException("Provider transaction id is required.", nameof(providerTransactionId));
        }

        ProviderTransactionId = providerTransactionId.Trim();
    }
}

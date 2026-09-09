namespace OrderingService.Domain.Orders;

public sealed class Order
{
    private readonly List<OrderItem> _items = [];

    public Guid Id { get; }
    public Guid CustomerId { get; }
    public DateTime CreatedAtUtc { get; }
    public OrderStatus Status { get; private set; }
    public string Currency { get; }
    public string? IdempotencyKey { get; }
    public string? CheckoutRequestHash { get; }
    public long? CheckoutBasketVersion { get; }
    public Guid? CheckoutBasketId { get; }
    public string? DiscountCode { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public Guid? DiscountReservationId { get; private set; }
    public OrderAddressSnapshot? ShippingAddress { get; }
    public IReadOnlyList<OrderItem> Items => _items;
    public decimal SubtotalAmount => _items.Sum(item => item.TotalPrice);
    public decimal TotalAmount => SubtotalAmount - DiscountAmount;

    public Order(
        Guid id,
        Guid customerId,
        DateTime createdAtUtc,
        OrderStatus status,
        string? idempotencyKey = null,
        string currency = "USD",
        string? checkoutRequestHash = null,
        long? checkoutBasketVersion = null,
        Guid? checkoutBasketId = null,
        OrderAddressSnapshot? shippingAddress = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Order id cannot be empty.", nameof(id));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer id cannot be empty.", nameof(customerId));
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Currency is required.", nameof(currency));

        idempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (idempotencyKey?.Length > 128)
        {
            throw new ArgumentException("Idempotency key cannot exceed 128 characters.", nameof(idempotencyKey));
        }

        checkoutRequestHash = string.IsNullOrWhiteSpace(checkoutRequestHash)
            ? null
            : checkoutRequestHash.Trim().ToLowerInvariant();
        if (checkoutRequestHash is not null &&
            (checkoutRequestHash.Length != 64 || checkoutRequestHash.Any(character => !Uri.IsHexDigit(character))))
        {
            throw new ArgumentException("Checkout request hash must be a SHA-256 hexadecimal value.", nameof(checkoutRequestHash));
        }

        if (checkoutBasketVersion is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(checkoutBasketVersion), "Checkout basket version must be greater than zero.");
        }

        if (checkoutBasketId == Guid.Empty)
        {
            throw new ArgumentException("Checkout basket id cannot be empty.", nameof(checkoutBasketId));
        }

        Id = id;
        CustomerId = customerId;
        CreatedAtUtc = createdAtUtc;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CheckoutRequestHash = checkoutRequestHash;
        CheckoutBasketVersion = checkoutBasketVersion;
        CheckoutBasketId = checkoutBasketId;
        Currency = currency.Trim().ToUpperInvariant();
        ShippingAddress = shippingAddress?.Normalize();
    }

    public void AddItem(OrderItem item)
    {
        _items.Add(item);
    }

    public void ApplyDiscount(string couponCode, decimal discountAmount)
    {
        if (string.IsNullOrWhiteSpace(couponCode))
        {
            throw new ArgumentException("Discount code is required.", nameof(couponCode));
        }

        if (discountAmount <= 0 || discountAmount > SubtotalAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(discountAmount), "Discount amount must be greater than zero and cannot exceed the order subtotal.");
        }

        DiscountCode = couponCode.Trim().ToUpperInvariant();
        DiscountAmount = decimal.Round(discountAmount, 2, MidpointRounding.AwayFromZero);
    }

    public void AttachDiscountReservation(Guid reservationId)
    {
        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException("Discount reservation id is required.", nameof(reservationId));
        }

        if (string.IsNullOrWhiteSpace(DiscountCode) || DiscountAmount <= 0)
        {
            throw new InvalidOperationException("A discount must be applied before attaching its reservation.");
        }

        DiscountReservationId = reservationId;
    }

    public bool MarkPendingPayment()
    {
        if (Status == OrderStatus.PendingPayment)
        {
            return false;
        }

        if (Status != OrderStatus.Pending)
        {
            throw new InvalidOperationException($"Order cannot move from {Status} to {OrderStatus.PendingPayment}.");
        }

        Status = OrderStatus.PendingPayment;
        return true;
    }

    public bool MarkPaid()
    {
        if (Status == OrderStatus.Paid)
        {
            return false;
        }

        if (Status is OrderStatus.Cancelled or OrderStatus.PaymentFailed)
        {
            throw new InvalidOperationException($"Order cannot move from {Status} to {OrderStatus.Paid}.");
        }

        if (Status is not (OrderStatus.Pending or OrderStatus.PendingPayment))
        {
            throw new InvalidOperationException($"Order cannot be paid while it is {Status}.");
        }

        Status = OrderStatus.Paid;
        return true;
    }

    public bool MarkPaymentFailed()
    {
        if (Status is OrderStatus.PaymentFailed or OrderStatus.Cancelled)
        {
            return false;
        }

        if (Status == OrderStatus.Paid)
        {
            return false;
        }

        if (Status is not (OrderStatus.Pending or OrderStatus.PendingPayment))
        {
            throw new InvalidOperationException($"Order cannot fail payment while it is {Status}.");
        }

        Status = OrderStatus.PaymentFailed;
        return true;
    }

    public bool Cancel()
    {
        return CancelBeforeFulfillment();
    }

    public bool CancelBeforeFulfillment()
    {
        if (Status == OrderStatus.Cancelled)
        {
            return false;
        }

        if (Status is not (OrderStatus.Pending or OrderStatus.PendingPayment or OrderStatus.PaymentFailed))
        {
            throw new InvalidOperationException(
                $"Order cannot be cancelled while it is {Status}. Paid or fulfilled orders require a dedicated refund or return workflow.");
        }

        Status = OrderStatus.Cancelled;
        return true;
    }

    public bool MoveToFulfillmentStatus(OrderStatus targetStatus)
    {
        if (targetStatus is not (OrderStatus.Confirmed or OrderStatus.Shipped or OrderStatus.Delivered))
        {
            throw new ArgumentOutOfRangeException(nameof(targetStatus), "Fulfillment target must be Confirmed, Shipped, or Delivered.");
        }

        if (Status == targetStatus)
        {
            return false;
        }

        var isAllowedTransition = (Status, targetStatus) switch
        {
                        (OrderStatus.Paid, OrderStatus.Confirmed) => true,
            (OrderStatus.PendingPayment, OrderStatus.Confirmed) => true,
            (OrderStatus.Confirmed, OrderStatus.Shipped) => true,
            (OrderStatus.Shipped, OrderStatus.Delivered) => true,
            _ => false
        };

        if (!isAllowedTransition)
        {
            throw new InvalidOperationException($"Order cannot move from {Status} to {targetStatus} in the fulfillment workflow.");
        }

        Status = targetStatus;
        return true;
    }

    public bool MarkRefunded()
    {
        if (Status == OrderStatus.Refunded)
        {
            return false;
        }

        if (Status != OrderStatus.Paid)
        {
            throw new InvalidOperationException($"Order cannot be refunded while it is {Status}.");
        }

        Status = OrderStatus.Refunded;
        return true;
    }
}

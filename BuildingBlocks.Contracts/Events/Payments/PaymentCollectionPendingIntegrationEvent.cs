namespace BuildingBlocks.Contracts.Events.Payments;

// Cash-on-delivery was selected. No funds have been authorized or captured.
public sealed record PaymentCollectionPendingIntegrationEvent : PaymentOperationCompletedIntegrationEvent;
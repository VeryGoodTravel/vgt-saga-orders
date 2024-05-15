using vgt_saga_serialization;

namespace vgt_saga_orders.Orchestrator.SagaEvents;

/// <inheritdoc/>
public struct PaymentAnswer : IEvent
{
    /// <inheritdoc/>
    public Guid TransactionId { get; set; }
    /// <inheritdoc/>
    public SagaState? State { get; set; }
    /// <inheritdoc/>
    public bool Answer { get; set; }
}
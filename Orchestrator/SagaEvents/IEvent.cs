using vgt_saga_serialization;

namespace vgt_saga_orders.Orchestrator.SagaEvents;

/// <summary>
/// Event command specifying changes to the DB object
/// </summary>
public interface IEvent
{
    /// <summary>
    /// SAGA TransactionID used as the stream definition in the event store
    /// </summary>
    public Guid TransactionId { get; set; }
    
    /// <summary>
    /// State of the saga to save, used as a command type
    /// </summary>
    public SagaState? State { get; set; }
    
    /// <summary>
    /// Answer of the service to update the db with
    /// </summary>
    public bool Answer { get; set; }
    
}
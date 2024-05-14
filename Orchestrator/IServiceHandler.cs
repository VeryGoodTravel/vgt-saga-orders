using System.Threading.Channels;
using vgt_saga_serialization;

namespace vgt_saga_orders.Orchestrator;

/// <summary>
/// Orchestrator handlers of the services in the SAGA architecture,
/// each service has its own handler.
/// </summary>
public interface IServiceHandler
{
    /// <summary>
    /// Messages received from the specified service
    /// </summary>
    public Channel<Message> Messages { get; }
    /// <summary>
    /// Messages that need to be sent out to the queues
    /// </summary>
    public Channel<Message> Publish { get; }
    
    /// <summary>
    /// current request handled
    /// </summary>
    public Message Request { get; }
    
    /// <summary>
    /// Task handling requests of the service
    /// </summary>
    public Task RequestsTask { get; set; }
    
    /// <summary>
    /// Cancellation token allowing a graceful exit of the class
    /// </summary>
    public CancellationToken Token { get; }
    
}
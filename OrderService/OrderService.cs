using System.Text;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using NEventStore;
using NEventStore.Serialization.Json;
using Newtonsoft.Json;
using NLog;
using Npgsql;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using vgt_saga_orders.Models;
using vgt_saga_serialization;
using vgt_saga_serialization.MessageBodies;

namespace vgt_saga_orders.OrderService;

/// <summary>
/// Saga Orchestrator;
/// handles all saga transactions of user orders.
/// </summary>
public class OrderService : IDisposable
{
    private readonly OrderQueueHandler _queues;
    private readonly Logger _logger;
    private readonly IConfiguration _config;
    private readonly Utils _jsonUtils;
    private readonly IStoreEvents _eventStore;

    private readonly List<MessageType> _keys = [MessageType.OrderReply, MessageType.OrderRequest];
    private readonly Channel<TransactionBody> _backendMessages;
    private readonly Channel<Message> _orchestratorMessages;
    private readonly Channel<Message> _publish;
    private readonly OrderHandler _orderHandler;
    
    /// <summary>
    /// Allows tasks cancellation from the outside of the class
    /// </summary>
    public CancellationToken Token { get; } = new();

    /// <summary>
    /// Constructor of the OrderService class.
    /// Initializes OrderService object.
    /// Creates, initializes and opens connections to the database and rabbitmq
    /// based on configuration data present and handled by specified handling objects.
    /// Throws propagated exceptions if the configuration data is nowhere to be found.
    /// </summary>
    /// <param name="config"> Configuration with the connection params </param>
    /// <param name="lf"> Logger factory to use by the event store </param>
    /// <exception cref="ArgumentException"> Which variable is missing in the configuration </exception>
    /// <exception cref="BrokerUnreachableException"> Couldn't establish connection with RabbitMQ </exception>
    public OrderService(IConfiguration config, ILoggerFactory lf)
    {
        _logger = LogManager.GetCurrentClassLogger();
        _config = config;

        _jsonUtils = new Utils(_logger);
        
        var connStr = SecretUtils.GetConnectionString(_config, "DB_NAME_SAGA", _logger);
        var options = new DbContextOptions<SagaDbContext>();
        var writeDb = new SagaDbContext(options, connStr);
        
        _logger.Debug("Creating tasks message channels");
        _backendMessages = Channel.CreateUnbounded<TransactionBody>(new UnboundedChannelOptions()
            { SingleReader = true, SingleWriter = true, AllowSynchronousContinuations = true });
        _orchestratorMessages = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions()
            { SingleReader = true, SingleWriter = true, AllowSynchronousContinuations = true });
        _publish = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions()
            { SingleReader = true, SingleWriter = true, AllowSynchronousContinuations = true });
        _logger.Debug("Tasks message channels created");

        Task.Run(RabbitPublisher);

        
        _orderHandler = new OrderHandler(_orchestratorMessages, _backendMessages, _publish, writeDb, _logger);

        _queues = new OrderQueueHandler(_config, _logger);
        
        _queues.AddRepliesConsumer(SagaOrdersEventHandler);
        _queues.AddBackendConsumer(BackendOrdersEventHandler);
    }

    /// <summary>
    /// Publishes made messages to the right queues
    /// </summary>
    private async Task RabbitPublisher()
    {
        while (await _publish.Reader.WaitToReadAsync(Token))
        {
            var message = await _publish.Reader.ReadAsync(Token);

            _logger.Debug("Recieved message {msg} {id}", message.MessageType.ToString(), message.TransactionId);
            if (message.MessageType != MessageType.BackendReply && message.MessageType != MessageType.BackendRequest)
            {
                _logger.Debug("Sending to the backend {msg} {id} {state}", message.MessageType.ToString(), message.TransactionId, message.State);
                _queues.PublishToBackend(JsonConvert.SerializeObject((BackendReply)message.Body));
            }
            else
            {
                _logger.Debug("Sending to the orchestrator {msg} {id} {state}", message.MessageType.ToString(), message.TransactionId, message.State);
                _queues.PublishToOrchestrator( _jsonUtils.Serialize(message));
            }
        }
    }

    /// <summary>
    /// Event Handler that hooks to the event of the queue consumer.
    /// Handles incoming replies from the RabbitMQ and routes them to the appropriate tasks.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="ea"></param>
    private void SagaOrdersEventHandler(object? sender, BasicDeliverEventArgs ea)
    {
        _logger.Debug("Received response | Tag: {tag}", ea.DeliveryTag);
        var body = ea.Body.ToArray();

        var reply = _jsonUtils.Deserialize(body);

        if (reply == null) return;

        var message = reply.Value;

        // send message reply to the appropriate task
        var result = _orchestratorMessages.Writer.TryWrite(message);
        
        if (result) _logger.Debug("Replied routed successfuly to {type} handler", message.MessageType.ToString());
        else _logger.Warn("Something went wrong in routing to {type} handler", message.MessageType.ToString());

        _queues.PublishTagResponse(ea, result);
    }
    
    /// <summary>
    /// Event Handler that hooks to the event of the queue consumer.
    /// Handles incoming replies from the RabbitMQ and routes them to the appropriate tasks.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="ea"></param>
    private void BackendOrdersEventHandler(object? sender, BasicDeliverEventArgs ea)
    {
        _logger.Debug("Received response | Tag: {tag}", ea.DeliveryTag);
        var body = ea.Body.ToArray();

        var str = Encoding.UTF8.GetString(body);
        _logger.Info("Received response | {tag}", str);
        
        var reply = JsonConvert.DeserializeObject<TransactionBody?>(str);

        if (reply == null)
        {
            _logger.Warn("Received response | nulll after conversion");
            return;
        }

        var message = reply.Value;

        // send message reply to the appropriate task
        var result = _backendMessages.Writer.TryWrite(message);
        
        if (result) _logger.Debug("Replied routed successfuly to backend handler");
        else _logger.Warn("Something went wrong in routing to backend handler");

        _queues.PublishBackendTagResponse(ea, result);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _logger.Debug("Disposing");
        _queues.Dispose();
    }
}
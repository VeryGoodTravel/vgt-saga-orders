using System.Data.Common;
using System.Threading.Channels;
using NEventStore;
using NEventStore.Serialization.Json;
using NLog;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Scaffolding.Internal;
using Npgsql.Internal;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using vgt_saga_orders.Orchestrator.ServiceHandlers;
using vgt_saga_serialization;

namespace vgt_saga_orders.Orchestrator;

/// <summary>
/// Saga Orchestrator;
/// handles all saga transactions of user orders.
/// </summary>
public class Orchestrator : IDisposable
{
    private readonly RepliesQueueHandler _queues;
    private readonly Logger _logger;
    private readonly Utils _jsonUtils;
    private readonly OrchOrderHandler _orchOrderHandler;
    private readonly OrchBookHandler _orchBookHandler;
    private readonly OrchPaymentHandler _orchPaymentHandler;
    private readonly IStoreEvents _eventStore;

    private readonly List<MessageType> _keys =
    [
        MessageType.OrderRequest, MessageType.PaymentRequest, MessageType.HotelRequest, MessageType.FlightRequest
    ];

    private readonly Dictionary<MessageType, Channel<Message>> _repliesChannels = [];

    private readonly Channel<Message> _publish;

    /// <summary>
    /// Allows tasks cancellation from the outside of the class
    /// </summary>
    public CancellationToken Token { get; } = new();

    /// <summary>
    /// Constructor of the Orchestrator class.
    /// Initializes Orchestrator object.
    /// Creates, initializes and opens connections to the database and rabbitmq
    /// based on configuration data present and handled by specified handling objects.
    /// Throws propagated exceptions if the configuration data is nowhere to be found.
    /// </summary>
    /// <param name="config"> Configuration with the connection params </param>
    /// <param name="lf"> Logger factory to use for the Event Store </param>
    /// <exception cref="ArgumentException"> Which variable is missing in the configuration </exception>
    /// <exception cref="BrokerUnreachableException"> Couldn't establish connection with RabbitMQ </exception>
    public Orchestrator(IConfiguration config, ILoggerFactory lf)
    {
        _logger = LogManager.GetCurrentClassLogger();
        var config1 = config;

        _jsonUtils = new Utils(_logger);
        CreateChannels();

        var connStr = SecretUtils.GetConnectionString(config1, "DB_NAME_SAGA", _logger);

        //var db = new DbProviderFactory();
        //NpgsqlFactory.Instance.CreateConnection();
        ///DbProviderFactory factory =
        //    DbProviderFactories.GetFactory("Npgsql");
        

        _eventStore = Wireup.Init()
            .WithLoggerFactory(lf)
            .UsingInMemoryPersistence()
            //.UsingSqlPersistence(NpgsqlFactory.Instance,connStr)
            .InitializeStorageEngine()
            .UsingJsonSerialization()
            .Compress()
            .Build();

        _logger.Debug("After eventstore");
        
        _publish = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions()
            { SingleReader = true, SingleWriter = true, AllowSynchronousContinuations = true });

        _orchOrderHandler = new OrchOrderHandler(_repliesChannels[MessageType.OrderRequest], _publish, _eventStore, connStr, _logger);
        _orchBookHandler = new OrchBookHandler(_repliesChannels[MessageType.HotelRequest], _publish, _eventStore, connStr, _logger);
        _orchPaymentHandler = new OrchPaymentHandler(_repliesChannels[MessageType.PaymentRequest], _publish, _eventStore, connStr, _logger);

        Task.Run(RabbitPublisher);
        
        _queues = new RepliesQueueHandler(config1, _logger);
        
        _queues.AddRepliesConsumer(SagaRepliesEventHandler);
    }

    /// <summary>
    /// Publishes made messages to the right queues
    /// </summary>
    private async Task RabbitPublisher()
    {
        _logger.Debug("-----------------Rabbit publisher starting");
        while (await _publish.Reader.WaitToReadAsync(Token))
        {
            _logger.Debug("-----------------Rabbit publisher message");
            var message = await _publish.Reader.ReadAsync(Token);

            _logger.Debug("Recieved message {msg} {id}", message.MessageType.ToString(), message.TransactionId);
            
            switch (message.MessageType)
            {
                case MessageType.HotelRequest or MessageType.HotelReply:
                    _queues.PublishToHotel(_jsonUtils.Serialize(message));
                    _logger.Debug("Sending to hotel {msg} {id} {state}", message.MessageType.ToString(), message.TransactionId, message.State);
                    break;
                case MessageType.FlightRequest or MessageType.FlightReply:
                    _queues.PublishToFlight(_jsonUtils.Serialize(message));
                    _logger.Debug("Sending to flight {msg} {id} {state}", message.MessageType.ToString(), message.TransactionId, message.State);

                    break;
                case MessageType.OrderRequest or MessageType.OrderReply or MessageType.BackendRequest or MessageType.BackendReply:
                    _queues.PublishToOrders(_jsonUtils.Serialize(message));
                    _logger.Debug("Sending to Orders {msg} {id} {state}", message.MessageType.ToString(), message.TransactionId, message.State);

                    break;
                case MessageType.PaymentRequest or MessageType.PaymentReply:
                    _queues.PublishToPayment(_jsonUtils.Serialize(message));
                    _logger.Debug("Sending to Payment {msg} {id} {state}", message.MessageType.ToString(), message.TransactionId, message.State);

                    break;
            }
        }
    }

    /// <summary>
    /// Event Handler that hooks to the event of the queue consumer.
    /// Handles incoming replies from the RabbitMQ and routes them to the appropriate tasks.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="ea"></param>
    private void SagaRepliesEventHandler(object? sender, BasicDeliverEventArgs ea)
    {
        _logger.Debug("Received response | Tag: {tag}", ea.DeliveryTag);
        var body = ea.Body.ToArray();
        
        var reply = _jsonUtils.Deserialize(body);

        if (reply == null) return;

        var message = reply.Value;
        
        _logger.Debug("Received response parsed | {tag} | {s} | {a}", message.TransactionId, message.MessageType, message.State);

        // send message reply to the appropriate task
        var result = message.MessageType switch
        {
            MessageType.OrderRequest or MessageType.OrderReply or MessageType.BackendReply or MessageType.BackendRequest
                => _repliesChannels[MessageType.OrderRequest].Writer.TryWrite(message),
            MessageType.HotelReply or MessageType.HotelRequest or MessageType.FlightReply or MessageType.FlightRequest
                => _repliesChannels[MessageType.HotelRequest].Writer.TryWrite(message),
            MessageType.PaymentReply or MessageType.PaymentRequest 
                => _repliesChannels[MessageType.PaymentRequest].Writer.TryWrite(message),
            _ => false
        };

        if (result) _logger.Debug("Replie routed successfuly to {type} handler", message.MessageType.ToString());
        else _logger.Warn("Something went wrong in routing to {type} handler", message.MessageType.ToString());

        _queues.PublishTagResponse(ea, result);
    }

    /// <summary>
    /// Creates async channels to send received messages with to the tasks handling them.
    /// Channels are stored in the dictionary MessageType - Channel
    /// </summary>
    private void CreateChannels()
    {
        _logger.Debug("Creating tasks message channels");
        foreach (var key in _keys)
        {
            _repliesChannels[key] = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions()
                { SingleReader = true, SingleWriter = true, AllowSynchronousContinuations = true });
        }

        _logger.Debug("Tasks message channels created");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _logger.Debug("Disposing");
        _queues.Dispose();
    }
}
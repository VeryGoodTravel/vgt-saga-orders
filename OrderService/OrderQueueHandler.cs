using System.Text;
using NLog;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace vgt_saga_orders.OrderService;

/// <summary>
/// Class handling RabbitMQ connections, messages and events;
/// all concerning SAGA transactions with an orchestrator.
/// Configuration data needed for this class is as follows.
/// <p>
/// <list type="bullet">
///     <listheader><term>Rabbit connection:</term></listheader>
///     <item><term>RABBIT_HOST</term> <description> - Address of the rabbit server.</description></item>
///     <item><term>RABBIT_VIRT_HOST</term> <description> - Virtual host of the rabbit server.</description></item>
///     <item><term>RABBIT_PORT</term> <description> - Port of the rabbit server.</description></item>
///     <item><term>RABBIT_USR</term> <description> - Username to login with.</description></item>
///     <item><term>RABBIT_PASSWORD</term> <description> - User password to login with.</description></item>
/// </list>
/// and
/// <list type="bullet">
///     <listheader><term>Queue names:</term></listheader>
///     <item><term>RABBIT_REPLIES</term> <description> - Queue of the replies sent back to the orchestrator.</description></item>
///     <item><term>RABBIT_ORDER</term> <description> - Queue of the requests sent by the orchestrator to the order service.</description></item>
/// </list>
/// </p>
/// </summary>
public class OrderQueueHandler : IDisposable
{
    private const string LoggerPrefix = "OrderQueue| ";
    private readonly ConnectionFactory _factory;
    private readonly IConnection _connection;
    private readonly Logger _logger;

    // channels of the queues
    private readonly IModel _sagaReplies;
    private readonly IModel _sagaOrder;
    private readonly IModel _backendRequests;
    private readonly IModel _backendReplies;

    // replies consumer
    private EventingBasicConsumer? _consumer;
    // backend consumer
    private EventingBasicConsumer? _backendConsumer;

    private List<string> _queueNames;

    /// <summary>
    /// Constructor of the RabbitMQ handling class.
    /// Initializes RabbitMQ handling object.
    /// Creates connection string/factory based on configuration data present
    /// with exceptions thrown if the data is nowhere to be found.
    /// </summary>
    /// <param name="config"> Configuration with the connection params </param>
    /// <param name="log"> logger to log to </param>
    /// <exception cref="ArgumentException"> Which variable is missing in the configuration </exception>
    /// <exception cref="BrokerUnreachableException"> Couldn't establish connection </exception>
    public OrderQueueHandler(IConfiguration config, Logger log)
    {
        _logger = log;
        _logger.Debug("{p}Initializing RabbitMq connections", LoggerPrefix);
        while (_connection is not { IsOpen: true })
        {
            try
            {
                _factory = GetConnectionFactoryFromConfig(config);
                _connection = _factory.CreateConnection();
            }
            catch (BrokerUnreachableException e)
            {
                _logger.Error(
                    "{p}Couldn't connect to the RabbitMq server. Check connection string and/or connection {e}",
                    LoggerPrefix, e);
                Task.Delay(100).Wait();
            }
        }

        _logger.Debug("{p}Connected to the RabbitMq server", LoggerPrefix);

        _queueNames = GetQueuesFromConfig(config);

        _sagaReplies = _connection.CreateModel();
        _sagaReplies.QueueDeclare(_queueNames[0],
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object>());

        _sagaOrder = _connection.CreateModel();
        _sagaOrder.QueueDeclare(_queueNames[1],
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object>());

        _backendRequests = _connection.CreateModel();
        _backendRequests.QueueDeclare(_queueNames[2],
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object>());

        _backendReplies = _connection.CreateModel();
        _backendReplies.ExchangeDeclare(_queueNames[3], 
            ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            arguments: new Dictionary<string, object>());

        _logger.Debug("{p}Initialized RabbitMq queues", LoggerPrefix);
        _logger.Info("{p}Initialized RabbitMq", LoggerPrefix);
    }

    /// <summary>
    /// Publish saga message to Orchestrator queue
    /// </summary>
    /// <param name="body"> json body of the message to send </param>
    public void PublishToOrchestrator(string body)
    {
        _logger.Info("{p}Publishing a message to orchestrator", LoggerPrefix);
        _logger.Debug("{p}Response: {res}", LoggerPrefix, body);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        
        _sagaReplies.BasicPublish(string.Empty, _queueNames[0], null, bodyBytes);
    }
    
    /// <summary>
    /// Publish the message to Backend exchange
    /// answers requests from the backend
    /// </summary>
    /// <param name="body"> json body of the message to send </param>
    public void PublishToBackend(string body)
    {
        _logger.Info("{p}Publishing a message to orchestrator", LoggerPrefix);
        _logger.Debug("{p}Response: {res}", LoggerPrefix, body);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        
        _sagaReplies.BasicPublish(_queueNames[3], string.Empty, null, bodyBytes);
    }

    /// <summary>
    /// Handles RabbitMQ message tag and posts the acceptance or rejection,
    /// </summary>
    /// <param name="ea"> tag to answer </param>
    /// <param name="state"> ack/reject </param>
    public void PublishTagResponse(BasicDeliverEventArgs ea, bool state)
    {
        if (state) _sagaOrder.BasicAck(ea.DeliveryTag, false);
        else _sagaOrder.BasicReject(ea.DeliveryTag, false);
    }
    
    /// <summary>
    /// Handles RabbitMQ message tag and posts the acceptance or rejection,
    /// </summary>
    /// <param name="ea"> tag to answer </param>
    /// <param name="state"> ack/reject </param>
    public void PublishBackendTagResponse(BasicDeliverEventArgs ea, bool state)
    {
        if (state) _backendRequests.BasicAck(ea.DeliveryTag, false);
        else _backendRequests.BasicReject(ea.DeliveryTag, false);
    }

    /// <summary>
    /// Create queue consumer and hook to the event specifying incoming requests.
    /// </summary>
    /// <param name="handler"> handler to assign to the consumer event </param>
    public void AddRepliesConsumer(EventHandler<BasicDeliverEventArgs> handler)
    {
        _consumer = new EventingBasicConsumer(_sagaOrder);
        _logger.Debug("{p}Added Replies consumer", LoggerPrefix);
        _consumer.Received += handler;
        _logger.Debug("{p}Added Replies event handler", LoggerPrefix);
    }
    
    /// <summary>
    /// Create queue consumer and hook to the event specifying incoming requests.
    /// </summary>
    /// <param name="handler"> handler to assign to the consumer event </param>
    public void AddBackendConsumer(EventHandler<BasicDeliverEventArgs> handler)
    {
        _backendConsumer = new EventingBasicConsumer(_backendRequests);
        _logger.Debug("{p}Added Backend consumer", LoggerPrefix);
        _backendConsumer.Received += handler;
        _logger.Debug("{p}Added Backend event handler", LoggerPrefix);
    }

    /// <summary>
    /// Get the list of all saga queues defined in the configuration.
    /// Logs, Creates and Throws ArgumentError if a queue name is not present.
    /// </summary>
    /// <param name="config"> Configuration to take the values from </param>
    /// <returns> List of queue names </returns>
    /// <exception cref="ArgumentException"> Which variable is missing </exception>
    private List<string> GetQueuesFromConfig(IConfiguration config)
    {
        _logger.Error(config.GetValue<string?>("RABBIT_REPLIES"));
        var result = new List<string>
        {
            string.IsNullOrEmpty(config.GetValue<string?>("RABBIT_REPLIES"))
                ? ThrowException<string>("RABBIT_REPLIES")
                : config.GetValue<string?>("RABBIT_REPLIES")!,
            string.IsNullOrEmpty(config.GetValue<string?>("RABBIT_ORDER"))
                ? ThrowException<string>("RABBIT_ORDER")
                : config.GetValue<string?>("RABBIT_ORDER")!,
            string.IsNullOrEmpty(config.GetValue<string?>("BACKEND_REQUESTS"))
                ? ThrowException<string>("BACKEND_REQUESTS")
                : config.GetValue<string?>("BACKEND_REQUESTS")!,
            string.IsNullOrEmpty(config.GetValue<string?>("BACKEND_REPLIES"))
                ? ThrowException<string>("BACKEND_REPLIES")
                : config.GetValue<string?>("BACKEND_REPLIES")!,
        };

        return result;
    }

    /// <summary>
    /// Creates connection factory to the RabbitMQ
    /// based on the data specified in the configuration file or env variables
    /// </summary>
    /// <param name="config"> Configuration to use </param>
    /// <returns> ConnectionFactory with specified connection params </returns>
    /// <exception cref="ArgumentException"> Which variable is missing </exception>
    private ConnectionFactory GetConnectionFactoryFromConfig(IConfiguration config)
    {
        var host = string.IsNullOrEmpty(config.GetValue<string>("RABBIT_HOST"))
            ? ThrowException<string>("RABBIT_HOST")
            : config.GetValue<string>("RABBIT_HOST")!;
        var virtHost = string.IsNullOrEmpty(config.GetValue<string>("RABBIT_VIRT_HOST"))
            ? ThrowException<string>("RABBIT_VIRT_HOST")
            : config.GetValue<string>("RABBIT_VIRT_HOST")!;
        var port = config.GetValue<int?>("RABBIT_PORT") ?? ThrowException<int>("RABBIT_PORT");
        var usr = string.IsNullOrEmpty(config.GetValue<string>("RABBIT_USR"))
            ? ThrowException<string>("RABBIT_USR")
            : config.GetValue<string>("RABBIT_USR")!;
        var pass = string.IsNullOrEmpty(config.GetValue<string>("RABBIT_PASSWORD"))
            ? ThrowException<string>("RABBIT_PASSWORD")
            : config.GetValue<string>("RABBIT_PASSWORD")!;

        return new ConnectionFactory
        {
            HostName = host,
            Port = port,
            UserName = usr,
            Password = pass,
            VirtualHost = virtHost,
            RequestedHeartbeat = TimeSpan.FromSeconds(60),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(6000)
        };
    }

    /// <summary>
    /// Logs, creates and throws the exception that the specified variable is not present in the config
    /// </summary>
    /// <param name="argument"> Variable not present </param>
    /// <typeparam name="T"> type to "return" </typeparam>
    /// <returns></returns>
    /// <exception cref="ArgumentException"> Which variable is missing </exception>
    private T ThrowException<T>(string argument)
    {
        _logger.Error("{p}Couldn't load the data needed from env variables for the connection. Var: {e}", LoggerPrefix,
            argument);
        throw new ArgumentException(argument);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _sagaReplies.Close();
        _sagaOrder.Close();
        _backendRequests.Close();
        _backendReplies.Close();

        _sagaReplies.Dispose();
        _sagaOrder.Dispose();
        _backendRequests.Dispose();
        _backendReplies.Dispose();
        
        _connection.Close();
        _connection.Dispose();
    }
}
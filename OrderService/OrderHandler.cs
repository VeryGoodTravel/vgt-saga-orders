using System.Threading.Channels;
using Azure.Core;
using Microsoft.EntityFrameworkCore;
using NEventStore;
using NLog;
using vgt_saga_orders.Models;
using vgt_saga_serialization;
using vgt_saga_serialization.MessageBodies;

namespace vgt_saga_orders.OrderService;

/// <summary>
/// Handles saga orders beginning, end and failures
/// Creates the appropriate saga messages
/// Handles the data in messages
/// </summary>
public class OrderHandler
{

    /// <summary>
    /// Messages from the backend to handle
    /// </summary>
    public Channel<TransactionBody> BackendMessages;

    /// <summary>
    /// Messages from the orchestrator to handle
    /// </summary>
    public Channel<Message> OrchestratorMessages;

    /// <summary>
    /// Messages that need to be sent out to the queues
    /// </summary>
    public Channel<Message> Publish;
    
    /// <summary>
    /// current request handled
    /// </summary>
    public TransactionBody CurrentRequest { get; set; }
    
    private Logger _logger;

    private readonly SagaDbContext _db;
    
    /// <summary>
    /// Task of the requests handler
    /// </summary>
    public Task RequestsTask { get; set; }
    
    /// <summary>
    /// Task of the Replies handler
    /// </summary>
    public Task RepliesTask { get; set; }

    /// <summary>
    /// Token allowing tasks cancellation from the outside of the class
    /// </summary>
    public CancellationToken Token { get; } = new();
    private SemaphoreSlim DbLock { get; } = new SemaphoreSlim(1, 1);

    /// <summary>
    /// Default constructor of the order handler class
    /// that handles data and prepares messages concerning saga orders beginning, end and failure
    /// </summary>
    /// <param name="replies"> Queue with the replies from the orchestrator </param>
    /// <param name="requests"> Queue with the requests to the orchestrator </param>
    /// <param name="publish"> Queue with messages that need to be published to RabbitMQ </param>
    /// <param name="db"> Database used </param>
    /// <param name="log"> logger to log to </param>
    public OrderHandler(SagaDbContext db, Logger log)
    {
        _logger = log;
        
        OrchestratorMessages = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions()
            { SingleReader = true, SingleWriter = true, AllowSynchronousContinuations = true });
        BackendMessages = Channel.CreateUnbounded<TransactionBody>(new UnboundedChannelOptions()
            { SingleReader = true, SingleWriter = true, AllowSynchronousContinuations = true });
        Publish = Channel.CreateUnbounded<Message>(new UnboundedChannelOptions()
            { SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = true });
        
        _db = db;

        _logger.Debug("Starting tasks handling the messages");
        RequestsTask = Task.Run(HandleRequests);
        RepliesTask = Task.Run(HandleReplies);
        _logger.Debug("Tasks handling the messages started");
    }

    private async Task HandleRequests()
    {
        while (await BackendMessages.Reader.WaitToReadAsync(Token))
        {
            CurrentRequest = await BackendMessages.Reader.ReadAsync(Token);
            _logger.Debug("Handling backend message | TransactionID {tId}", CurrentRequest.TransactionId);
            
            var trans = new Transaction()
            {
                TransactionId = CurrentRequest.TransactionId,
                OfferId = CurrentRequest.OfferId,
                BookFrom = CurrentRequest.BookFrom,
                BookTo = CurrentRequest.BookTo,
                TripFrom = CurrentRequest.TripFrom,
                TripTo = CurrentRequest.TripTo,
                HotelName = CurrentRequest.HotelName,
                RoomType = CurrentRequest.RoomType,
                AdultCount = CurrentRequest.AdultCount,
                OldChildren = CurrentRequest.OldChildren,
                MidChildren = CurrentRequest.MidChildren,
                LesserChildren = CurrentRequest.LesserChildren
            };
            await DbLock.WaitAsync(Token);
            await _db.Transactions.AddAsync(trans, Token);
            await _db.SaveChangesAsync(Token);
            DbLock.Release();
            _logger.Debug("Created a transaction entity in the db");
            
            var sagaHotel = new Message()
            {
                TransactionId = CurrentRequest.TransactionId,
                MessageId = 0,
                CreationDate = DateTime.Now,
                MessageType = MessageType.HotelRequest,
                State = SagaState.Begin,
                Body = new HotelRequest()
                {
                    Temporary = true,
                    RoomType = trans.RoomType,
                    HotelName = trans.HotelName,
                    BookFrom = trans.BookFrom,
                    BookTo = trans.BookTo,
                    TemporaryDateTime = DateTime.MinValue
                }
            };
            await Publish.Writer.WaitToWriteAsync();
            if(!Publish.Writer.TryWrite(sagaHotel)) {_logger.Debug("not working channel hotel");}
            _logger.Debug("Sent a saga message concerning hotel to the orchestrator");
            
            var sagaFlight = new Message()
            {
                TransactionId = CurrentRequest.TransactionId,
                MessageId = 0,
                CreationDate = DateTime.Now,
                MessageType = MessageType.FlightRequest,
                State = SagaState.Begin,
                Body = new FlightRequest()
                {
                    Temporary = true,
                    CityFrom = trans.TripFrom,
                    CityTo = trans.TripTo,
                    BookFrom = trans.BookFrom,
                    BookTo = trans.BookTo,
                    TemporaryDateTime = DateTime.MinValue,
                    PassangerCount = trans.AdultCount + trans.LesserChildren + trans.MidChildren + trans.OldChildren
                }
            };
            await Publish.Writer.WaitToWriteAsync();
            if(!Publish.Writer.TryWrite(sagaFlight)) {_logger.Debug("not working channel flight");}
            _logger.Debug("Sent a saga message concerning flight to the orchestrator");
        }
    }
    
    // TODO: change the end result to work
    private async Task HandleReplies()
    {
        while (await OrchestratorMessages.Reader.WaitToReadAsync(Token))
        {
            var reply = await OrchestratorMessages.Reader.ReadAsync(Token);
            if (reply.Body == null) continue;
            
            await DbLock.WaitAsync(Token);
            var dbData = await _db.Transactions.FirstOrDefaultAsync(p => p.TransactionId == reply.TransactionId, Token);
            DbLock.Release();
            
            if (dbData == null) continue;

            if (reply.State == SagaState.SagaSuccess)
            {
                reply.State = SagaState.SagaSuccess;
                reply.MessageType = MessageType.BackendReply;
                reply.Body = new BackendReply()
                {
                    Answer = SagaAnswer.Success,
                    OfferId = dbData.OfferId,
                    TransactionId = reply.TransactionId
                };
                
                await Publish.Writer.WriteAsync(reply, Token);
                continue;
            }

            // send it to the payment service
            if (dbData.FullBookFlight != null && dbData.FullBookHotel != null &&
                !dbData.FullBookFlight.Value && !dbData.FullBookHotel.Value ||
                dbData.TempBookFlight != null && dbData.TempBookHotel != null &&
                !dbData.TempBookFlight.Value && !dbData.TempBookHotel.Value)
            {
                reply.State = SagaState.SagaFail;
                reply.MessageType = MessageType.BackendReply;
                reply.Body = new BackendReply()
                {
                    Answer = SagaAnswer.HotelAndFlightFailure,
                    OfferId = dbData.OfferId,
                    TransactionId = reply.TransactionId
                };
                
                await Publish.Writer.WriteAsync(reply, Token);
                continue;
            }
            
            // send it to the payment service
            if (dbData.FullBookFlight != null && dbData.FullBookHotel != null &&
                 dbData.FullBookFlight.Value && !dbData.FullBookHotel.Value ||
                 dbData.TempBookFlight != null && dbData.TempBookHotel != null &&
                 dbData.TempBookFlight.Value && !dbData.TempBookHotel.Value)
            {
                reply.State = SagaState.SagaFail;
                reply.MessageType = MessageType.BackendReply;
                reply.Body = new BackendReply()
                {
                    Answer = SagaAnswer.HotelFailure,
                    OfferId = dbData.OfferId,
                    TransactionId = reply.TransactionId
                };
                
                await Publish.Writer.WriteAsync(reply, Token);
                continue;
            }
            
            // send it to the payment service
            if (dbData.FullBookFlight != null && dbData.FullBookHotel != null &&
                !dbData.FullBookFlight.Value && dbData.FullBookHotel.Value ||
                dbData.TempBookFlight != null && dbData.TempBookHotel != null &&
                !dbData.TempBookFlight.Value && dbData.TempBookHotel.Value)
            {
                reply.State = SagaState.SagaFail;
                reply.MessageType = MessageType.BackendReply;
                reply.Body = new BackendReply()
                {
                    Answer = SagaAnswer.FlightFailure,
                    OfferId = dbData.OfferId,
                    TransactionId = reply.TransactionId
                };
                
                await Publish.Writer.WriteAsync(reply, Token);
                continue;
            }
        }
    }
}
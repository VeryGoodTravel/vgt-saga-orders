using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using NEventStore;
using NLog;
using vgt_saga_orders.Models;
using vgt_saga_orders.Orchestrator.SagaEvents;
using vgt_saga_serialization;
using vgt_saga_serialization.MessageBodies;

namespace vgt_saga_orders.Orchestrator.ServiceHandlers;

/// <inheritdoc />
public class OrchPaymentHandler : IServiceHandler
{
    /// <inheritdoc />
    public Channel<Message> Messages { get; }

    /// <inheritdoc />
    public Channel<Message> Publish { get; }
    /// <inheritdoc />
    public Message Request { get; set; }
    
    /// <inheritdoc />
    public Task RequestsTask { get; set; }
    
    private IStoreEvents EventStore { get; }
    
    private Logger _logger;

    /// <inheritdoc />
    public CancellationToken Token { get; } = new();

    private SemaphoreSlim DbLock { get; } = new SemaphoreSlim(1, 1);
    private SagaDbContext Db { get; }

    /// <summary>
    /// Creates Orchestrator tasks handling Payment service
    /// Saves, Changes and Routes messages from and to OrderService
    /// </summary>
    /// <param name="replies"> Replies to the order service </param>
    /// <param name="requests"> Requests from the order service </param>
    /// <param name="publish"> Messages that need to be sent to the broker </param>
    /// <param name="eventStore"> Event sourcing </param>
    /// <param name="log"> logger to use </param>
    public OrchPaymentHandler(Channel<Message> messages, Channel<Message> publish, IStoreEvents eventStore, string conn, Logger log)
    {
        _logger = log;
        Messages = messages;
        EventStore = eventStore;
        Publish = publish;
        
        var options = new DbContextOptions<SagaDbContext>();
        Db = new SagaDbContext(options, conn);
        
        _logger.Debug("Starting tasks handling the messages");
        RequestsTask = Task.Run(HandleRequests);
        _logger.Debug("Tasks handling the messages started");
    }

    private async Task HandleRequests()
    {
        while (await Messages.Reader.WaitToReadAsync(Token))
        {
            var req = await Messages.Reader.ReadAsync(Token);
            
            var answer = new PaymentAnswer()
            {
                TransactionId = Request.TransactionId,
                State = Request.State,
                Answer = Request.State == SagaState.PaymentAccept
            };
            await AppendToStream(answer);

            await DbLock.WaitAsync(Token);
            var dbData = await Db.Transactions.FirstOrDefaultAsync(p => p.TransactionId == Request.TransactionId, Token);
            DbLock.Release();
            
            await HandlePayment(dbData);
            await HandleTempBookings(dbData);

        }
    }
    
    private async Task HandleTempBookings(Transaction? dbData)
    {
        if (dbData == null) return;
        if (Request.MessageType is not (MessageType.HotelReply or MessageType.HotelRequest or MessageType.FlightRequest or MessageType.FlightReply)) return;

        // send it to the payment service
        if (dbData.TempBookHotel != null && dbData.TempBookHotel.Value &&
            Request.State == SagaState.FlightTimedAccept || dbData.TempBookFlight != null &&
            dbData.TempBookFlight.Value && Request.State == SagaState.HotelTimedAccept)
        {
            var payment = new Message()
            {
                TransactionId = Request.TransactionId,
                MessageId = Request.MessageId + 1,
                CreationDate = Request.CreationDate,
                MessageType = MessageType.PaymentRequest,
                State = null,
                Body = new PaymentRequest()
            };
            await Publish.Writer.WriteAsync(payment, Token);
            return;
        }

        // update db only
        if (dbData.TempBookFlight == null || dbData.TempBookHotel == null)
        {
            if (dbData.TempBookFlight == null &&
                Request.State is SagaState.FlightTimedAccept or SagaState.FlightTimedFail)
            {
                if (Request.State is SagaState.FlightTimedAccept)
                {
                    var answer = new FlightTempBooked()
                    {
                        TransactionId = Request.TransactionId,
                        State = Request.State,
                        Answer = Request.State == SagaState.FlightTimedAccept
                    };
                    await AppendToStream(answer);
                }else if (Request.State is SagaState.FlightTimedFail)
                {
                    var answer = new FlightTempBooked()
                    {
                        TransactionId = Request.TransactionId,
                        State = Request.State,
                        Answer = Request.State == SagaState.FlightTimedRollback
                    };
                    await AppendToStream(answer);
                }
            }
            if (dbData.TempBookHotel == null &&
                Request.State is SagaState.HotelTimedAccept or SagaState.HotelTimedFail)
            {
                if (Request.State is SagaState.HotelTimedAccept)
                {
                    var answer = new HotelTempBooked()
                    {
                        TransactionId = Request.TransactionId,
                        State = Request.State,
                        Answer = Request.State == SagaState.HotelTimedAccept
                    };
                    await AppendToStream(answer);
                }else if (Request.State is SagaState.FlightTimedFail)
                {
                    var answer = new HotelTempBooked()
                    {
                        TransactionId = Request.TransactionId,
                        State = Request.State,
                        Answer = Request.State == SagaState.HotelTimedRollback
                    };
                    await AppendToStream(answer);
                }
            }
            return;
        }

        // rollback
        var hotel = new Message()
        {
            TransactionId = Request.TransactionId,
            MessageId = Request.MessageId + 1,
            CreationDate = Request.CreationDate,
            MessageType = MessageType.HotelRequest,
            State = SagaState.HotelTimedRollback,
            Body = new HotelRequest()
            {
                Temporary = true,
                RoomType = dbData.RoomType,
                BookTo = dbData.BookTo,
                BookFrom = dbData.BookFrom,
                HotelName = dbData.HotelName,
            }
        };
        var flight = new Message()
        {
            TransactionId = Request.TransactionId,
            MessageId = Request.MessageId + 1,
            CreationDate = Request.CreationDate,
            MessageType = MessageType.FlightRequest,
            State = SagaState.FlightTimedRollback,
            Body = new FlightRequest()
            {
                Temporary = true,
                BookFrom = dbData.BookFrom,
                BookTo = dbData.BookTo,
                CityFrom = dbData.TripFrom,
                CityTo = dbData.TripTo,
            }
        };
        

        await Publish.Writer.WriteAsync(hotel, Token);
        await Publish.Writer.WriteAsync(flight, Token);
    }

    private async Task HandlePayment(Transaction? dbData)
    {
        if (dbData == null) return;
        if (Request.MessageType is not (MessageType.PaymentRequest or MessageType.PaymentReply)) return;
        Message hotel;
        Message flight;

        if (Request.State == SagaState.PaymentAccept)
        {
            hotel = new Message()
            {
                TransactionId = Request.TransactionId,
                MessageId = Request.MessageId + 1,
                CreationDate = Request.CreationDate,
                MessageType = MessageType.HotelRequest,
                State = SagaState.PaymentAccept,
                Body = new HotelRequest()
                {
                    Temporary = false,
                    RoomType = dbData.RoomType,
                    BookTo = dbData.BookTo,
                    BookFrom = dbData.BookFrom,
                    HotelName = dbData.HotelName,
                }
            };
            flight = new Message()
            {
                TransactionId = Request.TransactionId,
                MessageId = Request.MessageId + 1,
                CreationDate = Request.CreationDate,
                MessageType = MessageType.FlightRequest,
                State = SagaState.PaymentAccept,
                Body = new FlightRequest()
                {
                    Temporary = false,
                    BookFrom = dbData.BookFrom,
                    BookTo = dbData.BookTo,
                    CityFrom = dbData.TripFrom,
                    CityTo = dbData.TripTo,
                }
            };
        }
        else
        {
            hotel = new Message()
            {
                TransactionId = Request.TransactionId,
                MessageId = Request.MessageId + 1,
                CreationDate = Request.CreationDate,
                MessageType = MessageType.HotelRequest,
                State = SagaState.PaymentFailed,
                Body = new HotelRequest()
            };
            flight = new Message()
            {
                TransactionId = Request.TransactionId,
                MessageId = Request.MessageId + 1,
                CreationDate = Request.CreationDate,
                MessageType = MessageType.FlightRequest,
                State = SagaState.PaymentFailed,
                Body = new FlightRequest()
            };
        }

        await Publish.Writer.WriteAsync(hotel, Token);
        await Publish.Writer.WriteAsync(flight, Token);
    }
    
    private async Task AppendToStream(IEvent mess)
    {
        using var stream = EventStore.OpenStream(mess.TransactionId, 0, int.MaxValue);
        
        stream.Add(new EventMessage { Body = mess });
        stream.CommitChanges(Guid.NewGuid());
        await DbLock.WaitAsync(Token);
        var dbData = await Db.Transactions.FirstOrDefaultAsync(p => p.TransactionId == mess.TransactionId, Token);
        if (dbData == null || mess.State == null)
        {
            DbLock.Release();
            return;
        }

        dbData.State = mess.State.Value;
        dbData.Payment = mess.Answer;
        await Db.SaveChangesAsync(Token);
        DbLock.Release();
    }
    
    private IEnumerable<Message> LoadFromStream(Guid transaction)
    {
        using var stream = EventStore.OpenStream(transaction);
        return stream.CommittedEvents.Select(p => (Message)p.Body);
    }
}
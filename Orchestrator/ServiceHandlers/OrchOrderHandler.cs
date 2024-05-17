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
public class OrchOrderHandler : IServiceHandler
{
    /// <inheritdoc />
    public Channel<Message> Messages { get; }

    /// <inheritdoc />
    public Channel<Message> Publish { get; }
    /// <inheritdoc />
    
    /// <inheritdoc />
    public Task RequestsTask { get; set; }
    
    private IStoreEvents EventStore { get; }
    
    private Logger _logger;

    /// <inheritdoc />
    public CancellationToken Token { get; } = new();
    private SemaphoreSlim DbLock { get; } = new SemaphoreSlim(1, 1);
    private SagaDbContext Db { get; }


    /// <summary>
    /// Creates Orchestrator tasks handling Order service
    /// Saves, Changes and Routes messages from and to OrderService
    /// </summary>
    /// <param name="replies"> Replies to the order service </param>
    /// <param name="requests"> Requests from the order service </param>
    /// <param name="publish"> Messages that need to be sent to the broker </param>
    /// <param name="eventStore"> Event sourcing </param>
    /// <param name="log"> logger to use </param>
    public OrchOrderHandler(Channel<Message> messages, Channel<Message> publish, IStoreEvents eventStore, string conn, Logger log)
    {
        _logger = log;
        Messages = messages;
        EventStore = eventStore;
        Publish = publish;
        
        var options = new DbContextOptionsBuilder<SagaDbContext>();
        options.UseNpgsql(conn);
        Db = new SagaDbContext(options.Options);
        
        _logger.Debug("Starting tasks handling the messages");
        RequestsTask = Task.Run(HandleRequests);
        _logger.Debug("Tasks handling the messages started");
    }

    private async Task HandleRequests()
    {
        while (await Messages.Reader.WaitToReadAsync(Token))
        {
            var reply = await Messages.Reader.ReadAsync(Token);
            
            switch (reply.State)
            {
                case SagaState.Begin:
                    await Publish.Writer.WriteAsync(reply, Token);
                    break;
                //AppendToStream(Request);
                case SagaState.HotelFullFail or SagaState.HotelFullAccept or SagaState.FlightFullAccept or SagaState.FlightFullFail or SagaState.HotelTimedRollback or SagaState.FlightTimedRollback:

                    IEvent @event = reply.State switch
                    {
                        SagaState.HotelFullFail or SagaState.HotelFullAccept => new HotelFullBooked()
                        {
                            Answer = reply.State == SagaState.HotelFullAccept,
                            State = reply.State,
                            TransactionId = reply.TransactionId
                        },
                        SagaState.HotelTimedRollback => new HotelTempRollback()
                        {
                            Answer = reply.State == SagaState.HotelTimedAccept,
                            State = reply.State,
                            TransactionId = reply.TransactionId
                        },
                        SagaState.FlightTimedRollback => new FlightTempRollback()
                        {
                            Answer = reply.State == SagaState.FlightTimedAccept,
                            State = reply.State,
                            TransactionId = reply.TransactionId
                        },
                        SagaState.FlightFullFail or SagaState.FlightFullAccept => new FlightFullBooked()
                        {
                            Answer = reply.State == SagaState.FlightFullAccept,
                            State = reply.State,
                            TransactionId = reply.TransactionId
                        },
                        _ => throw new ArgumentOutOfRangeException()
                    };
                    await AppendToStream(@event);
                    
                    await DbLock.WaitAsync(Token);
                    var dbData = await Db.Transactions.FirstOrDefaultAsync(p => p.TransactionId == reply.TransactionId, Token);
                    DbLock.Release();
                    
                    if (dbData == null) continue;
                    
                    // send it to the payment service
                    if (dbData.FullBookHotel != null && dbData.FullBookHotel.Value &&
                        reply.State == SagaState.FlightFullAccept || 
                        dbData.FullBookFlight != null && dbData.FullBookFlight.Value && 
                        reply.State == SagaState.HotelFullAccept)
                    {
                        reply.State = SagaState.SagaSuccess;
                        reply.MessageType = MessageType.OrderReply;
                        reply.Body = new OrderReply();
                        
                        await Publish.Writer.WriteAsync(reply, Token);
                        continue;
                    }
                    
                    // send it to the payment service
                    if (dbData.FullBookFlight != null && !dbData.FullBookFlight.Value && 
                        reply.State == SagaState.HotelFullFail || 
                        dbData.FullBookHotel != null && !dbData.FullBookHotel.Value &&
                        reply.State == SagaState.FlightFullFail ||
                        dbData.TempBookFlight != null && !dbData.TempBookFlight.Value && 
                        reply.State is SagaState.HotelTimedRollback or SagaState.HotelTimedFail || 
                        dbData.TempBookHotel != null && !dbData.TempBookHotel.Value &&
                        reply.State is SagaState.FlightTimedRollback or SagaState.FlightTimedFail)
                    {
                        reply.State = SagaState.SagaFail;
                        reply.MessageType = MessageType.OrderReply;
                        reply.Body = new OrderReply();
                        
                        await Publish.Writer.WriteAsync(reply, Token);
                        continue;
                    }
                    //AppendToStream(Request);
                    break;
            }


            //LoadFromStream(Request.TransactionId);
        }
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
        switch (mess.State.Value)
        {
            case SagaState.HotelTimedRollback:
                dbData.TempBookHotel = mess.Answer;
                break;
            case SagaState.HotelFullAccept or SagaState.HotelFullFail:
                dbData.FullBookHotel = mess.Answer;
                break;
            case SagaState.FlightTimedRollback:
                dbData.TempBookFlight = mess.Answer;
                break;
            case SagaState.FlightFullAccept or SagaState.FlightFullFail:
                dbData.FullBookFlight = mess.Answer;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        await Db.SaveChangesAsync(Token);
        DbLock.Release();
    }
    
    private IEnumerable<Message> LoadFromStream(Guid transaction)
    {
        using var stream = EventStore.OpenStream(transaction);
        return stream.CommittedEvents.Select(p => (Message)p.Body);
    }
}
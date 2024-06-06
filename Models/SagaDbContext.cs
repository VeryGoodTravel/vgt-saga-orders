using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using vgt_saga_serialization;

namespace vgt_saga_orders.Models;

/// <inheritdoc />
public class SagaDbContext : DbContext
{
    private string _connectionString;
    
    /// <summary>
    /// Set of Database Transaction entities mapped to Transaction objects
    /// </summary>
    public DbSet<Transaction> Transactions { get; set; }

    /// <inheritdoc />
    public SagaDbContext(DbContextOptions<SagaDbContext> options)
        : base(options)
    {
    }
    /// <inheritdoc />
    public SagaDbContext(DbContextOptions<SagaDbContext> options, string conn)
        : base(options)
    {
        _connectionString = conn;
    }
    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!_connectionString.IsNullOrEmpty())
        {
            options.UseNpgsql(_connectionString);
        }
        
        base.OnConfiguring(options);
    }
}

/// <summary>
/// Transaction object representing an object from the database
/// </summary>
public class Transaction()
{
    /// <summary>
    /// Guid of the SAGA transaction
    /// </summary>
    [Key]
    public Guid TransactionId { get; set; }
    
    /// <summary>
    /// ID of the offer as specified by the backend
    /// </summary>
    public string OfferId { get; set; }
    
    /// <summary>
    /// Date to book the hotel from (also the date of the flight)
    /// </summary>
    public DateTime BookFrom { get; set; }
    
    /// <summary>
    /// Date to book the hotel to (also the date of the return flight)
    /// </summary>
    public DateTime BookTo { get; set; }
    
    /// <summary>
    /// City of the airport the clients are going to fly off
    /// </summary>
    public string TripFrom { get; set; }
    
    /// <summary>
    /// City of the airport the clients are going to fly to
    /// </summary>
    public string TripTo { get; set; }
    
    /// <summary>
    /// Hotel specified in the offer
    /// </summary>
    public string HotelName { get; set; }
    
    /// <summary>
    /// Room type selected
    /// </summary>
    public string RoomType { get; set; }
    
    /// <summary>
    /// How many adults in the booking request
    /// </summary>
    public int AdultCount { get; set; }
    
    /// <summary>
    /// How many children under 18yo in the booking request
    /// </summary>
    public int OldChildren { get; set; }
    
    /// <summary>
    /// How many children under 10yo in the booking request
    /// </summary>
    public int MidChildren { get; set; }
    
    /// <summary>
    /// How many children under 3yo in the booking request
    /// </summary>
    public int LesserChildren { get; set; }
    
    // saga data --------------------------------
    
    public SagaState State { get; set; }
    
    public bool? TempBookHotel { get; set; }
    
    public bool? TempBookFlight { get; set; }
    
    public bool? FullBookHotel { get; set; }
    
    public bool? FullBookFlight { get; set; }
    
    public bool? Payment { get; set; }
}

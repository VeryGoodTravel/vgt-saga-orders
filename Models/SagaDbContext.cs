using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
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
    // {
    //     _connectionString = connectionString;
    // }
    //
    // /// <inheritdoc />
    // protected override void OnConfiguring(DbContextOptionsBuilder options)
    //     => options.UseNpgsql(_connectionString);
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
    /// Id of the offer as specified by the backend (verifies if another transaction for this 
    /// </summary>
    public int OfferId { get; set; }
    
    public SagaState State { get; set; }
    
    public DateTime BookFrom { get; set; }
    
    public DateTime BookTo { get; set; }
    
    public string TripFrom { get; set; }
    
    public string HotelName { get; set; }
    
    public string RoomType { get; set; }
    
    public int AdultCount { get; set; }
    
    public int OldChildren { get; set; }
    
    public int MidChildren { get; set; }
    
    public int LesserChildren { get; set; }
    
    public bool? TempBookHotel { get; set; }
    
    public bool? TempBookFlight { get; set; }
    
    public bool? FullBookHotel { get; set; }
    
    public bool? FullBookFlight { get; set; }
    
    public bool? Payment { get; set; }
}

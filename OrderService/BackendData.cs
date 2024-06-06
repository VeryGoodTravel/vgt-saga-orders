using vgt_saga_serialization;

namespace vgt_saga_orders.OrderService;

/// <summary>
/// Transaction object representing an object from the database
/// </summary>
public record struct TransactionBody()
{
    /// <summary>
    /// Guid of the SAGA transaction
    /// </summary>
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
}
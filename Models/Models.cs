using System;
using System.Collections.Generic;

namespace BusTicketApp.Models
{
    public class BusTrip
    {
        public int Id { get; set; }
        public string Route { get; set; }
        public DateTime DepartureTime { get; set; }
        public decimal Price { get; set; }
        public List<Seat> Seats { get; set; } = new List<Seat>();
    }

    public class Seat
    {
        public int Number { get; set; }
        public bool IsBooked { get; set; }
    }

    public class Ticket
    {
        public string TicketNumber { get; set; }
        public string Route { get; set; }
        public DateTime DepartureTime { get; set; }
        public int SeatNumber { get; set; }
        public string PassengerName { get; set; }
        public decimal Price { get; set; }
    }
}

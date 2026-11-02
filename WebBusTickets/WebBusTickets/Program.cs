using WebBusTickets.Models;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Регистрируем репозиторий как Singleton (один экземпляр на все приложение)
builder.Services.AddSingleton<BusTripRepository>();

builder.Services.AddCors(options => options.AddPolicy("AllowAll",
    p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

app.UseCors("AllowAll");
app.UseDefaultFiles();
app.UseStaticFiles();

// === ВЕБ-АДРЕСА (API ENDPOINTS) ===

// 1. Получить список всех рейсов
app.MapGet("/api/trips", (string? search, BusTripRepository repo) =>
{
    var trips = repo.GetTrips();
    if (!string.IsNullOrEmpty(search))
    {
        trips = trips.Where(t => t.Route.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
    }
    return Results.Ok(trips);
});

// 2. Купить билет
app.MapPost("/api/tickets/buy", (BuyTicketRequest request, BusTripRepository repo) =>
{
    if (string.IsNullOrWhiteSpace(request.PassengerName))
        return Results.BadRequest("Имя пассажира обязательно");

    var result = repo.BuyTicket(request.TripId, request.SeatNumber, request.PassengerName);

    return result != null ? Results.Ok(result) : Results.BadRequest("Рейс не найден или место занято");
});

// 3. Получить историю проданных билетов
app.MapGet("/api/tickets", (BusTripRepository repo) => Results.Ok(repo.GetTickets()));

// 4. Добавить новый рейс
app.MapPost("/api/admin/trips", (BusTrip newTrip, BusTripRepository repo) =>
{
    var createdTrip = repo.AddTrip(newTrip);
    return Results.Ok(createdTrip);
});

// 5. Обновить данные существующего рейса
app.MapPut("/api/admin/trips/{id:int}", (int id, BusTrip updatedTrip, BusTripRepository repo) =>
{
    var trip = repo.UpdateTrip(id, updatedTrip);
    return trip != null ? Results.Ok(trip) : Results.BadRequest("Рейс не найден");
});

// 6. Удалить/отменить рейс
app.MapDelete("/api/admin/trips/{id:int}", (int id, BusTripRepository repo) =>
{
    var success = repo.DeleteTrip(id);
    return success ? Results.Ok(new { message = "Рейс успешно отменен" }) : Results.BadRequest("Рейс не найден");
});

app.Run();

// === ЕДИНЫЙ ПОТОКОБЕЗОПАСНЫЙ РЕПОЗИТОРИЙ ДАННЫХ ===
public class BusTripRepository
{
    private const string DbFileName = "bus_database.json";
    private readonly object _lock = new();
    private DatabaseData _data = new();

    public BusTripRepository()
    {
        LoadData();
    }

    public List<BusTrip> GetTrips()
    {
        lock (_lock)
        {
            CheckAndRenewExpiredTrips();
            return _data.Trips;
        }
    }

    public List<Ticket> GetTickets()
    {
        lock (_lock)
        {
            return _data.SoldTickets;
        }
    }

    public Ticket? BuyTicket(int tripId, int seatNumber, string passengerName)
    {
        lock (_lock)
        {
            CheckAndRenewExpiredTrips();
            var trip = _data.Trips.FirstOrDefault(t => t.Id == tripId);
            if (trip == null) return null;

            var seat = trip.Seats.FirstOrDefault(s => s.Number == seatNumber);
            if (seat == null || seat.IsBooked) return null;

            seat.IsBooked = true;
            var ticket = new Ticket
            {
                TicketNumber = "TKT-" + Guid.NewGuid().ToString()[..8].ToUpper(),
                Route = trip.Route,
                DepartureTime = trip.DepartureTime,
                SeatNumber = seat.Number,
                PassengerName = passengerName,
                Price = trip.Price
            };

            _data.SoldTickets.Add(ticket);
            SaveData();
            return ticket;
        }
    }

    public BusTrip AddTrip(BusTrip newTrip)
    {
        lock (_lock)
        {
            newTrip.Id = _data.Trips.Count > 0 ? _data.Trips.Max(t => t.Id) + 1 : 1;

            // Если места не переданы админом, создаем дефолтные 40
            if (newTrip.Seats == null || newTrip.Seats.Count == 0)
            {
                newTrip.Seats = Enumerable.Range(1, 40).Select(i => new Seat { Number = i, IsBooked = false }).ToList();
            }

            _data.Trips.Add(newTrip);
            SaveData();
            return newTrip;
        }
    }

    public BusTrip? UpdateTrip(int id, BusTrip updatedTrip)
    {
        lock (_lock)
        {
            var trip = _data.Trips.FirstOrDefault(t => t.Id == id);
            if (trip == null) return null;

            trip.Route = updatedTrip.Route;
            trip.DepartureTime = updatedTrip.DepartureTime;
            trip.Price = updatedTrip.Price;

            SaveData();
            return trip;
        }
    }

    public bool DeleteTrip(int id)
    {
        lock (_lock)
        {
            var trip = _data.Trips.FirstOrDefault(t => t.Id == id);
            if (trip == null) return false;

            _data.Trips.Remove(trip);
            SaveData();
            return true;
        }
    }

    private void CheckAndRenewExpiredTrips()
    {
        bool isChanged = false;
        DateTime now = DateTime.Now;

        foreach (var trip in _data.Trips)
        {
            while (trip.DepartureTime <= now)
            {
                trip.DepartureTime = trip.DepartureTime.AddHours(1);
                foreach (var seat in trip.Seats) seat.IsBooked = false;
                isChanged = true;
            }
        }

        if (isChanged) SaveData();
    }

    private void LoadData()
    {
        if (!File.Exists(DbFileName))
        {
            _data = new DatabaseData { Trips = CreateDefaultTrips() };
            SaveData();
            return;
        }

        try
        {
            var json = File.ReadAllText(DbFileName);
            if (string.IsNullOrWhiteSpace(json))
            {
                _data = new DatabaseData { Trips = CreateDefaultTrips() };
            }
            else
            {
                _data = JsonSerializer.Deserialize<DatabaseData>(json) ?? new DatabaseData { Trips = CreateDefaultTrips() };
            }
        }
        catch
        {
            _data = new DatabaseData { Trips = CreateDefaultTrips() };
        }
    }

    private void SaveData()
    {
        var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(DbFileName, json);
    }

    private List<BusTrip> CreateDefaultTrips()
    {
        return new List<BusTrip>
        {
            new() { Id = 1, Route = "Москва - Санкт-Петербург", DepartureTime = DateTime.Now.AddMinutes(30), Price = 1500, Seats = Enumerable.Range(1, 40).Select(i => new Seat { Number = i, IsBooked = false }).ToList() },
            new() { Id = 2, Route = "Казань - Нижний Новгород", DepartureTime = DateTime.Now.AddHours(2), Price = 1200, Seats = Enumerable.Range(1, 30).Select(i => new Seat { Number = i, IsBooked = false }).ToList() }
        };
    }
}

// === ВСПОМОГАТЕЛЬНЫЕ КЛАССЫ ===
public class DatabaseData
{
    public List<BusTrip> Trips { get; set; } = new();
    public List<Ticket> SoldTickets { get; set; } = new();
}

public class BuyTicketRequest
{
    public int TripId { get; set; }
    public int SeatNumber { get; set; }
    public string PassengerName { get; set; } = "";
}

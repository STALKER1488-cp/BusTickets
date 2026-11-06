using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.IO;
using System.Linq;
using WebBusTickets.Models;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 1. РЕГИСТРАЦИЯ СЕРВИСОВ И АУTЕНТИФИКАЦИИ
// ==========================================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
// НАСТРОЙКА JSON-СЕРИАЛИЗАТОРА: Принудительно отдаем camelCase на фронтенд
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
// Регистрируем репозиторий как Singleton
builder.Services.AddSingleton<BusTripRepository>();
builder.Services.AddRazorPages();
// Регистрируем репозиторий как Singleton
builder.Services.AddSingleton<BusTripRepository>();
builder.Services.AddRazorPages();

builder.Services.AddCors(options => options.AddPolicy("AllowAll",
    p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

// ==========================================
// 2. НАСТРОЙКА MIDDLEWARE И КОНФИГУРАЦИИ
// ==========================================
app.UseCors("AllowAll");
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// ==========================================
// 3. API ЭНДПОИНТЫ: АУТЕНТИФИКАЦИЯ И РЕГИСТРАЦИЯ
// ==========================================

// Регистрация нового пользователя
app.MapPost("/api/auth/register", async (User newUser) =>
{
    string dbPath = Path.Combine(Directory.GetCurrentDirectory(), "bus_database.json");

    if (newUser == null || string.IsNullOrWhiteSpace(newUser.Email) || string.IsNullOrWhiteSpace(newUser.Password))
    {
        return Results.BadRequest("Email и пароль не могут быть пустыми.");
    }

    BusDatabase database = new BusDatabase();

    if (File.Exists(dbPath))
    {
        try
        {
            string jsonText = await File.ReadAllTextAsync(dbPath);
            // Явно указываем тип данных <BusDatabase> для десериализации строки
            var existingData = JsonSerializer.Deserialize<BusDatabase>(jsonText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (existingData != null) database = existingData;
        }
        catch (Exception) { }
    }

    if (database.Users == null) database.Users = new List<User>();

    bool userExists = database.Users.Any(u => u.Email.Equals(newUser.Email, StringComparison.OrdinalIgnoreCase));
    if (userExists)
    {
        return Results.BadRequest("Пользователь с таким Email уже зарегистрирован в системе.");
    }

    database.Users.Add(newUser);

    var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    string updatedJson = JsonSerializer.Serialize(database, jsonOptions);
    await File.WriteAllTextAsync(dbPath, updatedJson);

    return Results.Ok("Регистрация успешно завершена! Теперь вы можете войти.");
});

// Авторизация пользователя
app.MapPost("/api/auth/login", async (LoginRequest req, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest("Заполните все поля");

    if (req.Email.ToLower() == "admin@test.com" && req.Password == "admin123")
    {
        var claims = new List<Claim> { new Claim(ClaimTypes.Name, req.Email) };
        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));
        return Results.Ok(new { email = req.Email });
    }

    string dbPath = Path.Combine(Directory.GetCurrentDirectory(), "bus_database.json");
    if (File.Exists(dbPath))
    {
        try
        {
            string jsonText = await File.ReadAllTextAsync(dbPath);
            var database = JsonSerializer.Deserialize<BusDatabase>(jsonText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var user = database?.Users?.FirstOrDefault(u => u.Email.Equals(req.Email, StringComparison.OrdinalIgnoreCase) && u.Password == req.Password);
            if (user != null)
            {
                 var claims = new List<Claim> { new Claim(ClaimTypes.Name, req.Email) };
                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));
                return Results.Ok(new { email = user.Email });
            }
        }
        catch (Exception) { }
    }

    return Results.BadRequest("Неверная почта или пароль");
});

app.MapPost("/api/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok();
});

app.MapGet("/api/auth/status", (ClaimsPrincipal user) =>
{
    if (user.Identity?.IsAuthenticated == true)
        return Results.Ok(new { isAuthenticated = true, email = user.Identity.Name });
    return Results.Ok(new { isAuthenticated = false });
});

// ==========================================
// 4. API ЭНДПОИНТЫ: РЕЙСЫ И БИЛЕТЫ
// ==========================================

app.MapGet("/api/trips", (string? search, BusTripRepository repo) =>
{
    var trips = repo.GetTrips();
    if (!string.IsNullOrEmpty(search))
    {
        trips = trips.Where(t => t.Route.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
    }
    return Results.Ok(trips);
});

app.MapPost("/api/tickets/buy", (BuyTicketRequest request, BusTripRepository repo) =>
{
    if (string.IsNullOrWhiteSpace(request.PassengerName))
        return Results.BadRequest("Имя пассажира обязательно");

    var result = repo.BuyTicket(request.TripId, request.SeatNumber, request.PassengerName);
    return result != null ? Results.Ok(result) : Results.BadRequest("Рейс не найден или место занято");
}).RequireAuthorization();

app.MapGet("/api/tickets", (BusTripRepository repo) =>
{
    return Results.Ok(repo.GetTickets());
}).RequireAuthorization();

app.MapPost("/api/admin/trips", (BusTrip newTrip, BusTripRepository repo) =>
{
    if (string.IsNullOrWhiteSpace(newTrip.Route)) return Results.BadRequest("Название маршрута не может быть пустым.");
    if (newTrip.DepartureTime <= DateTime.Now) return Results.BadRequest("Дата отправления должна быть в будущем.");
    if (newTrip.Price <= 0) return Results.BadRequest("Стоимость билета должна быть больше нуля.");

    var createdTrip = repo.AddTrip(newTrip);
    return Results.Ok(createdTrip);
}).RequireAuthorization();

app.MapPut("/api/admin/trips/{id:int}", (int id, BusTrip updatedTrip, BusTripRepository repo) =>
{
    var trip = repo.UpdateTrip(id, updatedTrip);
    return trip != null ? Results.Ok(trip) : Results.BadRequest("Рейс не найден");
}).RequireAuthorization();

app.MapDelete("/api/admin/trips/{id:int}", (int id, BusTripRepository repo) =>
{
    var success = repo.DeleteTrip(id);
    return success ? Results.Ok(new { message = "Рейс успешно отменен" }) : Results.BadRequest("Рейс не найден");
}).RequireAuthorization();

app.MapRazorPages();

app.Run();

// ==========================================
// 5. КЛАССЫ ДАННЫХ И РЕПОЗИТОРИЙ
// ==========================================

public class LoginRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

public class BuyTicketRequest
{
    public int TripId { get; set; }
    public int SeatNumber { get; set; }
    public string PassengerName { get; set; } = "";
}

public class BusTripRepository
{
    private const string DbFileName = "bus_database.json";
    private readonly object _lock = new object();
    private DatabaseData _data = new DatabaseData();

    public BusTripRepository()
    {
        LoadData();
    }

    private void LoadData()
    {
        lock (_lock)
        {
            string dbPath = Path.Combine(Directory.GetCurrentDirectory(), DbFileName);
            if (!File.Exists(dbPath))
            {
                _data = new DatabaseData { Trips = CreateDefaultTrips() };
                SaveData();
                return;
            }

            try
            {
                string json = File.ReadAllText(dbPath);
                if (!string.IsNullOrWhiteSpace(json) && json.Trim() != "{}")
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var parsed = JsonSerializer.Deserialize<DatabaseData>(json, options);
                    if (parsed != null)
                    {
                        _data = parsed;
                    }
                }
            }
            catch { }

            if (_data.Trips == null || _data.Trips.Count == 0) _data.Trips = CreateDefaultTrips();
            if (_data.SoldTickets == null) _data.SoldTickets = new List<Ticket>();
            if (_data.Users == null) _data.Users = new List<User>();

            if (_data.Trips != null && _data.SoldTickets != null)
            {
                foreach (var trip in _data.Trips)
                {
                    // Полностью очищаем коллекцию Seats, если она пуста или повреждена
                    if (trip.Seats == null || trip.Seats.Count == 0)
                    {
                        trip.Seats = Enumerable.Range(1, 40).Select(i => new Seat { Number = i, IsBooked = false }).ToList();
                    }

                    // Сбрасываем статус всех мест в false перед сверкой
                    foreach (var seat in trip.Seats) seat.IsBooked = false;

                    // Сравниваем даты с точностью до минут, игнорируя миллисекунды, секунды и DateTimeKind
                    var bookedSeatsForTrip = _data.SoldTickets
                        .Where(t => t.Route.Equals(trip.Route, StringComparison.OrdinalIgnoreCase) &&
                                    t.DepartureTime.Year == trip.DepartureTime.Year &&
                                    t.DepartureTime.Month == trip.DepartureTime.Month &&
                                    t.DepartureTime.Day == trip.DepartureTime.Day &&
                                    t.DepartureTime.Hour == trip.DepartureTime.Hour &&
                                    t.DepartureTime.Minute == trip.DepartureTime.Minute)
                        .Select(t => t.SeatNumber)
                        .ToList();

                    // Отмечаем эти места как занятые
                    foreach (var seat in trip.Seats)
                    {
                        if (bookedSeatsForTrip.Contains(seat.Number))
                        {
                            seat.IsBooked = true;
                        }
                    }
                }
            }
        }
    }



    private void SaveData()
    {
        string dbPath = Path.Combine(Directory.GetCurrentDirectory(), DbFileName);

        // Создаем список для бэкапа пользователей
        var existingUsers = new System.Collections.Generic.List<User>();

        if (File.Exists(dbPath))
        {
            try
            {
                string jsonText = File.ReadAllText(dbPath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var db = JsonSerializer.Deserialize<BusDatabase>(jsonText, options);

                // Извлекаем ТОЛЬКО пользователей из файла, чтобы не затереть их профили
                if (db != null && db.Users != null)
                {
                    existingUsers = db.Users;
                }
            }
            catch { }
        }

        // Привязываем сохраненных пользователей к текущим актуальным рейсам и билетам
        _data.Users = existingUsers;

        // Сохраняем ВСЁ (актуальные рейсы со статусами мест, билеты и профили) обратно в файл
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        string updatedJson = JsonSerializer.Serialize(_data, jsonOptions);
        File.WriteAllText(dbPath, updatedJson);
    }


    public List<BusTrip> GetTrips()
    {
        lock (_lock)
        {
            LoadData();
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
    public Ticket BuyTicket(int tripId, int seatNumber, string passengerName)
    {
        lock (_lock)
        {
            CheckAndRenewExpiredTrips();

            BusTrip trip = null;
            foreach (var t in _data.Trips) { if (t.Id == tripId) { trip = t; break; } }
            if (trip == null) return null;

            // Изменяем поиск места на цикл FOR по индексу, чтобы жестко перезаписать значение в списке
            Seat seat = null;
            for (int i = 0; i < trip.Seats.Count; i++)
            {
                if (trip.Seats[i].Number == seatNumber)
                {
                    seat = trip.Seats[i];
                    break;
                }
            }

            if (seat == null || seat.IsBooked) return null;

            // Напрямую меняем статус места в объекте репозитория
            seat.IsBooked = true;

            var ticket = new Ticket
            {
                TicketNumber = "TKT-" + Guid.NewGuid().ToString().ToUpper().Substring(0, 8),
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
                foreach (var seat in trip.Seats) seat.IsBooked = true;
                isChanged = true;
            }
        }

        if (isChanged) SaveData();
    }

    // ВОЗВРАЩАЕМ МЕТОД СТРОГО ВНУТРЬ КЛАССА РЕПОЗИТОРИЯ
    private List<BusTrip> CreateDefaultTrips()
    {
        var trips = new List<BusTrip>();

        var trip1 = new BusTrip { Id = 1, Route = "Москва - Санкт-Петербург", DepartureTime = DateTime.Now.AddMinutes(30), Price = 1500 };
        for (int i = 1; i <= 40; i++) trip1.Seats.Add(new Seat { Number = i, IsBooked = false });
        trips.Add(trip1);

        var trip2 = new BusTrip { Id = 2, Route = "Казань - Нижний Новгород", DepartureTime = DateTime.Now.AddHours(2), Price = 1200 };
        for (int i = 1; i <= 40; i++) trip2.Seats.Add(new Seat { Number = i, IsBooked = false });
        trips.Add(trip2);

        return trips;
    }
} // <-- Эта скобка закрывает класс BusTripRepository. Ниже неё идут только DatabaseData, BusTrip и т.д.

public class DatabaseData
{
    public System.Collections.Generic.List<BusTrip> Trips { get; set; } = new System.Collections.Generic.List<BusTrip>();
    public System.Collections.Generic.List<Ticket> SoldTickets { get; set; } = new System.Collections.Generic.List<Ticket>();
    public System.Collections.Generic.List<User> Users { get; set; } = new System.Collections.Generic.List<User>();
}

public class BusTrip
{
    public int Id { get; set; }
    public string Route { get; set; } = "";
    public DateTime DepartureTime { get; set; }
    public double Price { get; set; }
    public System.Collections.Generic.List<Seat> Seats { get; set; } = new System.Collections.Generic.List<Seat>();
}

public class Seat
{
    public int Number { get; set; }
    public bool IsBooked { get; set; }
}
public class Ticket
{
    public string TicketNumber { get; set; } = "";
    public string Route { get; set; } = "";
    public DateTime DepartureTime { get; set; }
    public int SeatNumber { get; set; }
    public string PassengerName { get; set; } = "";
    public double Price { get; set; }
}
public class User
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
public class BusDatabase
{
    public System.Collections.Generic.List<User> Users { get; set; } = new System.Collections.Generic.List<User>();
}
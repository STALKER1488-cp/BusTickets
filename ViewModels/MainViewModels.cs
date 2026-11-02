using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using BusTicketApp.Models;

namespace BusTicketApp.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // Имя файла, который будет лежать в папке с программой
        private readonly string _dbFileName = "bus_database.json";

        private BusTrip _selectedTrip;
        private Seat _selectedSeat;
        private string _passengerName;

        public ObservableCollection<BusTrip> Trips { get; set; } = new ObservableCollection<BusTrip>();
        public ObservableCollection<Ticket> SoldTickets { get; set; } = new ObservableCollection<Ticket>();

        public BusTrip SelectedTrip
        {
            get => _selectedTrip;
            set { _selectedTrip = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentSeats)); }
        }

        public Seat SelectedSeat
        {
            get => _selectedSeat;
            set { _selectedSeat = value; OnPropertyChanged(); }
        }

        public string PassengerName
        {
            get => _passengerName;
            set { _passengerName = value; OnPropertyChanged(); }
        }

        public List<Seat> CurrentSeats => SelectedTrip?.Seats;

        public ICommand BuyTicketCommand { get; }

        public MainViewModel()
        {
            LoadData(); // Загружаем данные из файла
            BuyTicketCommand = new RelayCommand(ExecuteBuyTicket, CanBuyTicket);
        }

        // Класс-контейнер для сохранения всего состояния в один файл
        private class DatabaseData
        {
            public List<BusTrip> Trips { get; set; }
            public List<Ticket> SoldTickets { get; set; }
        }

        private void LoadData()
        {
            // Если файла базы данных еще нет (первый запуск), создаем начальные рейсы
            if (!File.Exists(_dbFileName))
            {
                CreateDefaultTrips();
                SaveData(); // Сразу сохраняем их в файл
                return;
            }

            try
            {
                // Читаем весь текст из файла JSON
                string jsonString = File.ReadAllText(_dbFileName);

                // Превращаем текст обратно в объекты C#
                var data = JsonSerializer.Deserialize<DatabaseData>(jsonString);

                if (data != null)
                {
                    Trips = new ObservableCollection<BusTrip>(data.Trips ?? new List<BusTrip>());
                    SoldTickets = new ObservableCollection<Ticket>(data.SoldTickets ?? new List<Ticket>());
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки базы данных: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                CreateDefaultTrips();
            }
        }

        private void SaveData()
        {
            try
            {
                var dataToSave = new DatabaseData
                {
                    Trips = Trips.ToList(),
                    SoldTickets = SoldTickets.ToList()
                };

                // Настройки, чтобы JSON красиво форматировался с отступами, а не писался в одну строку
                var options = new JsonSerializerOptions { WriteIndented = true };

                // Переводим объекты в строку JSON
                string jsonString = JsonSerializer.Serialize(dataToSave, options);

                // Записываем строку в файл (если файла нет, он создастся автоматически)
                File.WriteAllText(_dbFileName, jsonString);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения данных: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CreateDefaultTrips()
        {
            Trips.Clear();

            // Получаем сегодняшнюю дату со временем 00:00:00
            DateTime today = DateTime.Today;

            Trips.Add(new BusTrip
            {
                Id = 1,
                Route = "Москва — Санкт-Петербург",
                DepartureTime = today.AddHours(14).AddMinutes(30), // Сегодня в 14:30
                Price = 1500
            });

            Trips.Add(new BusTrip
            {
                Id = 2,
                Route = "Краснодар — Сочи",
                DepartureTime = today.AddDays(1).AddHours(08).AddMinutes(00), // Завтра утром в 08:00
                Price = 800
            });

            Trips.Add(new BusTrip
            {
                Id = 3,
                Route = "Новосибирск — Томск",
                DepartureTime = today.AddDays(2).AddHours(21).AddMinutes(15), // Послезавтра вечером в 21:15
                Price = 1200
            });

            // Автоматическая генерация 20 мест для каждого рейса
            foreach (var trip in Trips)
            {
                for (int i = 1; i <= 20; i++)
                {
                    trip.Seats.Add(new Seat { Number = i, IsBooked = i % 7 == 0 });
                }
            }
        }

        private bool CanBuyTicket(object obj)
        {
            if (obj is Seat seat) return !seat.IsBooked;
            return SelectedTrip != null && SelectedSeat != null && !SelectedSeat.IsBooked && !string.IsNullOrWhiteSpace(PassengerName);
        }

        private void ExecuteBuyTicket(object obj)
        {
            if (obj is Seat seat)
            {
                SelectedSeat = seat;
                OnPropertyChanged(nameof(SelectedSeat));
                return;
            }

            SelectedSeat.IsBooked = true;

            var ticket = new Ticket
            {
                TicketNumber = "BUS-" + Guid.NewGuid().ToString().Substring(0, 8).ToUpper(),
                Route = SelectedTrip.Route,
                DepartureTime = SelectedTrip.DepartureTime,
                SeatNumber = SelectedSeat.Number,
                PassengerName = PassengerName,
                Price = SelectedTrip.Price
            };

            SoldTickets.Add(ticket);

            // КРИТИЧЕСКИ ВАЖНО: сохраняем обновленные списки в JSON файл после каждой покупки
            SaveData();

            PassengerName = string.Empty;
            SelectedSeat = null;

            OnPropertyChanged(nameof(CurrentSeats));
            MessageBox.Show($"Билет {ticket.TicketNumber} успешно оформлен и сохранен в БД!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object parameter) => _execute(parameter);
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}

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
using CommunityToolkit.Mvvm.Input;

namespace BusTicketApp.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly string _dbFileName = "bus_database.json";

        private BusTrip? _selectedTrip;
        private Seat? _selectedSeat;
        private string _passengerName = string.Empty;
        private string _searchRouteQuery = string.Empty;
        private bool _isAdminMode;
        private DateTime? _newDepartureTime;

        // Полный список рейсов для внутреннего хранения
        private List<BusTrip> _allTrips = new List<BusTrip>();

        // Коллекции для интерфейса
        public ObservableCollection<BusTrip> FilteredTrips { get; set; } = new ObservableCollection<BusTrip>();
        public ObservableCollection<Ticket> SoldTickets { get; set; } = new ObservableCollection<Ticket>();

        public BusTrip? SelectedTrip
        {
            get => _selectedTrip;
            set
            {
                _allTrips = _allTrips;
                _selectedTrip = value;
                OnPropertyChanged();

      

                // 2. Только после этого берем уже обновленное (актуальное) время
                if (_selectedTrip != null)
                {
                    NewDepartureTime = _selectedTrip.DepartureTime;
                }

                OnPropertyChanged(nameof(CurrentSeats));

                // ПРИНУДИТЕЛЬНО ЗАСТАВЛЯЕМ ВСЕ КНОПКИ ПЕРЕПРОВЕРИТЬ СВОЙ СТАТУС "АКТИВНА/СЕРАЯ":
                if (UpdateTripTimeCommand is RelayCommand<object> updateCmd) updateCmd.NotifyCanExecuteChanged();
                if (CancelTripCommand is RelayCommand<object> cancelCmd) cancelCmd.NotifyCanExecuteChanged();
            }
        }

        public Seat? SelectedSeat
        {
            get => _selectedSeat;
            set
            {
                _selectedSeat = value;
                OnPropertyChanged();
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        public string PassengerName
        {
            get => _passengerName;
            set
            {
                _passengerName = value;
                OnPropertyChanged();
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        // Поисковый запрос
        public string SearchRouteQuery
        {
            get => _searchRouteQuery;
            set
            {
                _searchRouteQuery = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        // Режим Администратора
        public bool IsAdminMode
        {
            get => _isAdminMode;
            set { _isAdminMode = value; OnPropertyChanged(); }
        }

        // Новое время для обновления рейса
        public DateTime? NewDepartureTime
        {
            get => _newDepartureTime;
            set
            {
                _newDepartureTime = value;
                OnPropertyChanged();
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        public List<Seat>? CurrentSeats => SelectedTrip?.Seats;

        // Команды
        public ICommand BuyTicketCommand { get; }
        public ICommand UpdateTripTimeCommand { get; }
        public ICommand ToggleAdminModeCommand { get; }
        public ICommand AddTripCommand { get; }
        public ICommand CancelTripCommand { get; }

        // Реализация интерфейса INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainViewModel()
        {
            LoadData();
            BuyTicketCommand = new RelayCommand<object>(ExecuteBuyTicket, CanBuyTicket);
            UpdateTripTimeCommand = new RelayCommand<object>(ExecuteUpdateTripTime, CanUpdateTripTime);
            ToggleAdminModeCommand = new RelayCommand<object>(obj => IsAdminMode = !IsAdminMode);
            AddTripCommand = new RelayCommand<object>(ExecuteAddTrip);
            CancelTripCommand = new RelayCommand<object>(ExecuteCancelTrip, CanCancelTrip);
        }

        private class DatabaseData
        {
            public List<BusTrip>? Trips { get; set; }
            public List<Ticket>? SoldTickets { get; set; }
        }

        private void ApplyFilter()
        {
            FilteredTrips.Clear();
            var query = SearchRouteQuery?.Trim().ToLower();

            foreach (var trip in _allTrips)
            {
                if (string.IsNullOrEmpty(query) || (trip.Route != null && trip.Route.ToLower().Contains(query)))
                {
                    FilteredTrips.Add(trip);
                }
            }
        }

        private void CheckAndRenewExpiredTrips()
        {
            bool isChanged = false;
            DateTime now = DateTime.Now;

            foreach (var trip in _allTrips)
            {
                // ЕСЛИ ЭТОТ РЕЙС СЕЙЧАС ВЫБРАН АДМИНИСТРАТОРОМ ДЛЯ РЕДАКТИРОВАНИЯ — НЕ ТРОГАЕМ ЕГО АВТОПРОДЛЕНИЕМ!
                if (SelectedTrip != null && trip.Id == SelectedTrip.Id)
                {
                    continue;
                }

                while (trip.DepartureTime <= now)
                {
                    trip.DepartureTime = trip.DepartureTime.AddHours(1);

                    if (trip.Seats != null)
                    {
                        foreach (var seat in trip.Seats)
                        {
                            seat.IsBooked = false;
                        }
                    }

                    isChanged = true;
                }
            }

            if (isChanged)
            {
                SaveData();
                ApplyFilter();
                OnPropertyChanged(nameof(FilteredTrips));
                OnPropertyChanged(nameof(CurrentSeats));
                OnPropertyChanged(nameof(SoldTickets));

                // Убираем отсюда автоматическое переприсвоение NewDepartureTime,
                // чтобы оно не затирало то, что администратор пишет руками в текстовое поле
            }
        }

        private void LoadData()
        {
            if (!File.Exists(_dbFileName))
            {
                CreateDefaultTrips();
                SaveData();
                return;
            }

            try
            {
                string jsonString = File.ReadAllText(_dbFileName);
                var data = JsonSerializer.Deserialize<DatabaseData>(jsonString);

                if (data != null)
                {
                    _allTrips = data.Trips ?? new List<BusTrip>();
                    SoldTickets = new ObservableCollection<Ticket>(data.SoldTickets ?? new List<Ticket>());
                    CheckAndRenewExpiredTrips();
                    ApplyFilter();
                    OnPropertyChanged(nameof(SoldTickets));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                CreateDefaultTrips();
            }
        }

        private void SaveData()
        {
            try
            {
                var dataToSave = new DatabaseData
                {
                    Trips = _allTrips,
                    SoldTickets = SoldTickets.ToList()
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(dataToSave, options);
                File.WriteAllText(_dbFileName, jsonString);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CreateDefaultTrips()
        {
            _allTrips.Clear();
            DateTime today = DateTime.Today;

            _allTrips.Add(new BusTrip { Id = 1, Route = "Москва — Санкт-Петербург", DepartureTime = today.AddHours(14).AddMinutes(30), Price = 1500, Seats = new System.Collections.Generic.List<Seat>() });
            _allTrips.Add(new BusTrip { Id = 2, Route = "Краснодар — Сочи", DepartureTime = today.AddDays(1).AddHours(8), Price = 800, Seats = new System.Collections.Generic.List<Seat>() });
            _allTrips.Add(new BusTrip { Id = 3, Route = "Новосибирск — Томск", DepartureTime = today.AddDays(2).AddHours(21).AddMinutes(15), Price = 1200, Seats = new System.Collections.Generic.List<Seat>() });

            foreach (var trip in _allTrips)
            {
                if (trip.Seats == null) trip.Seats = new System.Collections.Generic.List<Seat>();
                for (int i = 1; i <= 20; i++)
                {
                    trip.Seats.Add(new Seat { Number = i, IsBooked = i % 7 == 0 });
                }
            }
            ApplyFilter();
        }

        private bool CanBuyTicket(object? obj)
        {
            if (obj is Seat seat) return !seat.IsBooked;
            return SelectedTrip != null && SelectedSeat != null && !SelectedSeat.IsBooked && !string.IsNullOrWhiteSpace(PassengerName);
        }

        private void ExecuteBuyTicket(object? obj)
        {
            if (obj is Seat seat && SelectedSeat != seat)
            {
                SelectedSeat = seat;
                OnPropertyChanged(nameof(SelectedSeat));
                return;
            }

            if (string.IsNullOrWhiteSpace(PassengerName))
            {
                MessageBox.Show("Пожалуйста, введите ФИО пассажира перед покупкой билета!", "Ошибка оформления", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (SelectedSeat == null || SelectedTrip == null) return;

            SelectedSeat.IsBooked = true;

            var ticket = new Ticket
            {
                TicketNumber = "BUS-" + Guid.NewGuid().ToString().Substring(0, 8).ToUpper(),
                TripId = SelectedTrip.Id,
                Route = SelectedTrip.Route ?? "Неизвестный маршрут",
                DepartureTime = SelectedTrip.DepartureTime,
                SeatNumber = SelectedSeat.Number,
                PassengerName = PassengerName,
                Price = SelectedTrip.Price
            };
            SoldTickets.Add(ticket);
            SaveData();
            PassengerName = string.Empty;
            SelectedSeat = null;
            CheckAndRenewExpiredTrips();
            OnPropertyChanged(nameof(CurrentSeats));
            OnPropertyChanged(nameof(SelectedSeat));
            MessageBox.Show($"Билет {ticket.TicketNumber} успешно оформлен!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private bool CanUpdateTripTime(object? obj)
        {
            return SelectedTrip != null && NewDepartureTime != null;
        }
        private void ExecuteUpdateTripTime(object? obj)
        {
            if (SelectedTrip == null || NewDepartureTime == null) return;

            // 1. Напрямую меняем время у выбранного рейса
            SelectedTrip.DepartureTime = NewDepartureTime.Value;

            // 2. Сохраняем изменения в JSON-файл
            SaveData();

            // 3. Чтобы WPF заметил изменение времени конкретного элемента в списке, 
            // нам нужно обновить коллекцию FilteredTrips. 
            // Вместо Clear() и полного перезапуска, просто временно сбросим и вернем индекс,
            // либо обновим отображение через ApplyFilter():
            var currentSelected = SelectedTrip;
            ApplyFilter();
            SelectedTrip = FilteredTrips.FirstOrDefault(t => t.Id == currentSelected.Id);

            MessageBox.Show("Время рейса успешно изменено!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void ExecuteAddTrip(object? obj)
        {
            int newId = _allTrips.Any() ? _allTrips.Max(t => t.Id) + 1 : 1;
            var newTrip = new BusTrip
            {
                Id = newId,
                Route = "Новый маршрут — Укажите направление",
                DepartureTime = DateTime.Now.AddDays(1),
                Price = 1000,
                Seats = new System.Collections.Generic.List<Seat>()
            };
            for (int i = 1; i <= 20; i++)
            {
                newTrip.Seats.Add(new Seat { Number = i, IsBooked = false });
            }
            _allTrips.Add(newTrip);
            SaveData();
            ApplyFilter();
            SelectedTrip = newTrip;
            MessageBox.Show("Новый рейс успешно добавлен! Измените его название, цену и время в панели администратора.", "Администрирование", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private bool CanCancelTrip(object? obj)
        {
            return SelectedTrip != null;
        }
        private void ExecuteCancelTrip(object? obj)
        {
            if (SelectedTrip == null) return;
            var result = MessageBox.Show(
            $"Вы уверены, что хотите отменить и удалить рейс '{SelectedTrip.Route}'?",
            "Подтверждение удаления",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                _allTrips.Remove(SelectedTrip);
                SaveData();
                ApplyFilter();
                SelectedTrip = null;
            }
        }
    }
}
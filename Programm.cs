using System;
using System.Windows;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockPriceTracker
{
    // Модели данных
    public class Ticker
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(10)]
        public string Symbol { get; set; } = string.Empty;
    }

    public class Price
    {
        [Key]
        public int Id { get; set; }

        public int TickerId { get; set; }

        [ForeignKey(nameof(TickerId))]
        public Ticker? Ticker { get; set; }

        public decimal StockPrice { get; set; }

        public DateTime Date { get; set; }
    }

    public class TodaysCondition
    {
        [Key]
        public int Id { get; set; }

        public int TickerId { get; set; }

        [ForeignKey(nameof(TickerId))]
        public Ticker? Ticker { get; set; }

        [MaxLength(20)]
        public string State { get; set; } = string.Empty;

        public DateTime Date { get; set; }
    }

    // Контекст базы данных
    public class StockDbContext : DbContext
    {
        public DbSet<Ticker> Tickers { get; set; }
        public DbSet<Price> Prices { get; set; }
        public DbSet<TodaysCondition> TodaysConditions { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer(
                "Server=localhost,1433;Database=StockTrackerDb;User Id=SA;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Encrypt=False;"
            );
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Ticker>()
                .HasIndex(t => t.Symbol)
                .IsUnique();
        }
    }

    // Модель данных API
    public class StockData
    {
        public double[]? o { get; set; }
        public double[]? h { get; set; }
        public double[]? l { get; set; }
        public double[]? c { get; set; }
        public long[]? v { get; set; }
    }

    // Сервис для работы с акциями
    public class StockService
    {
        private readonly HttpClient _httpClient;
        private const string API_TOKEN = "RWhhYmZtRy1qVUIwcUJrdnB3TjE5aXVjdnZPLVJ1WXFkS2dqR2pCQXBfdz0";

        public StockService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {API_TOKEN}");
        }

        public async Task InitializeDatabaseAsync()
        {
            using var context = new StockDbContext();
            await context.Database.EnsureCreatedAsync();
        }

        public async Task PreloadStockDataAsync()
        {
            string tickerFilePath = Path.Combine(AppContext.BaseDirectory, "ticker.txt");

            if (!File.Exists(tickerFilePath))
            {
                throw new FileNotFoundException("Файл ticker.txt не найден");
            }

            var tickers = await File.ReadAllLinesAsync(tickerFilePath);
            tickers = tickers.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();

            foreach (var ticker in tickers)
            {
                try
                {
                    await FetchAndSaveStockDataAsync(ticker.Trim().ToUpper());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка загрузки для {ticker}: {ex.Message}");
                }
            }
        }

        public async Task FetchAndSaveStockDataAsync(string ticker)
        {
            using var context = new StockDbContext();
            var existingTicker = await context.Tickers
                .FirstOrDefaultAsync(t => t.Symbol == ticker);

            if (existingTicker != null) return;

            var stockData = await FetchStockDataAsync(ticker);
            await SaveStockDataToDatabase(ticker, stockData);
            await AnalyzeStockPerformanceAsync(ticker);
        }

        private async Task<StockData> FetchStockDataAsync(string ticker)
        {
            string fromDate = DateTime.Now.AddMonths(-1).ToString("yyyy-MM-dd");
            string toDate = DateTime.Now.ToString("yyyy-MM-dd");

            string url = $"https://api.marketdata.app/v1/stocks/candles/D/{ticker}/?from={fromDate}&to={toDate}&format=json&adjusted=true";

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<StockData>();
            return data ?? throw new Exception("Не удалось получить данные");
        }

        private async Task SaveStockDataToDatabase(string tickerSymbol, StockData stockData)
        {
            using var context = new StockDbContext();

            var ticker = new Ticker { Symbol = tickerSymbol };
            context.Tickers.Add(ticker);

            if (stockData.c != null)
            {
                for (int i = 0; i < stockData.c.Length; i++)
                {
                    var price = new Price
                    {
                        Ticker = ticker,
                        StockPrice = (decimal)stockData.c[i],
                        Date = DateTime.Now.AddDays(-stockData.c.Length + i + 1)
                    };
                    context.Prices.Add(price);
                }
            }

            await context.SaveChangesAsync();
        }

        private async Task AnalyzeStockPerformanceAsync(string tickerSymbol)
        {
            using var context = new StockDbContext();

            var ticker = await context.Tickers
                .FirstOrDefaultAsync(t => t.Symbol == tickerSymbol);

            if (ticker == null) return;

            var prices = await context.Prices
                .Where(p => p.TickerId == ticker.Id)
                .OrderByDescending(p => p.Date)
                .Take(2)
                .ToListAsync();

            if (prices.Count < 2) return;

            string state = prices[0].StockPrice > prices[1].StockPrice
                ? "increased"
                : prices[0].StockPrice < prices[1].StockPrice
                    ? "decreased"
                    : "stable";

            var todaysCondition = new TodaysCondition
            {
                Ticker = ticker,
                State = state,
                Date = DateTime.Now
            };

            context.TodaysConditions.Add(todaysCondition);
            await context.SaveChangesAsync();
        }

        public async Task<string> GetStockPerformanceAsync(string tickerSymbol)
        {
            using var context = new StockDbContext();

            var ticker = await context.Tickers
                .FirstOrDefaultAsync(t => t.Symbol == tickerSymbol);

            if (ticker == null)
            {
                try
                {
                    await FetchAndSaveStockDataAsync(tickerSymbol);
                    ticker = await context.Tickers
                        .FirstOrDefaultAsync(t => t.Symbol == tickerSymbol);
                }
                catch
                {
                    return "Не удалось загрузить данные для тикера";
                }
            }

            if (ticker == null) return "Тикер не найден";

            var latestCondition = await context.TodaysConditions
                .Where(c => c.TickerId == ticker.Id)
                .OrderByDescending(c => c.Date)
                .FirstOrDefaultAsync();

            return latestCondition?.State ?? "Нет данных";
        }
    }

    public partial class MainWindow : Window
    {
        private readonly StockService _stockService;

        public MainWindow()
        {
            InitializeComponent();
            _stockService = new StockService();
            txtTicker.GotFocus += RemovePlaceholder;
            txtTicker.LostFocus += AddPlaceholder;
            AddPlaceholder(null, null);

            // Асинхронная предварительная загрузка данных при старте
            Loaded += MainWindow_Loaded;
        }

        private void RemovePlaceholder(object sender, RoutedEventArgs e)
        {
            if (txtTicker.Text == "Введите тикер")
                txtTicker.Text = "";
        }

        private void AddPlaceholder(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtTicker.Text))
                txtTicker.Text = "Введите тикер";
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Инициализация базы данных
                await _stockService.InitializeDatabaseAsync();

                // Предварительная загрузка данных
                await _stockService.PreloadStockDataAsync();

                txtResult.Text = "Данные предварительно загружены";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка предварительной загрузки: {ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void CheckStock_Click(object sender, RoutedEventArgs e)
        {
            string ticker = txtTicker.Text.Trim().ToUpper();
            if (ticker == "ВВЕДИТЕ ТИКЕР")
            {
                MessageBox.Show("Пожалуйста, введите тикер",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            try
            {
                var performance = await _stockService.GetStockPerformanceAsync(ticker);
                txtResult.Text = $"Состояние акции {ticker}: {performance}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    // Точка входа приложения
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
    }

    // Точка входа в приложение
    internal static class Program
    {
        [STAThread]
        public static void Main()
        {
            var app = new App();
            app.Run();
        }
    }
}
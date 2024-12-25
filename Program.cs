using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace StockPriceTCPServer
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

        [Required]
        public decimal StockPrice { get; set; }

        public DateTime Date { get; set; }
    }

    // Контекст базы данных
    public class StockDbContext : DbContext
    {
        public DbSet<Ticker> Tickers { get; set; }
        public DbSet<Price> Prices { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer(
                "Server=localhost,1433;Database=StockTrackerDb;User Id=SA;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Encrypt=False;"
            );
        }
    }

    // TCP Сервер
    class StockPriceServer
    {
        private TcpListener _server;
        private const int PORT = 8888;

        public StockPriceServer()
        {
            _server = new TcpListener(IPAddress.Loopback, PORT);
        }

        public void Start()
        {
            try
            {
                _server.Start();
                Console.WriteLine($"Сервер запущен на порту {PORT}");

                while (true)
                {
                    TcpClient client = _server.AcceptTcpClient();
                    Thread clientThread = new Thread(new ParameterizedThreadStart(HandleClient));
                    clientThread.Start(client);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сервера: {ex.Message}");
            }
            finally
            {
                _server.Stop();
            }
        }

        private void HandleClient(object obj)
        {
            TcpClient tcpClient = (TcpClient)obj;
            NetworkStream stream = tcpClient.GetStream();

            try
            {
                byte[] buffer = new byte[1024];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                string ticker = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim().ToUpper();

                Console.WriteLine($"Получен запрос для тикера: {ticker}");

                // Получаем последнюю цену из базы данных
                decimal lastPrice = GetLastStockPrice(ticker);

                // Отправляем ответ клиенту
                byte[] response = Encoding.ASCII.GetBytes(lastPrice.ToString());
                stream.Write(response, 0, response.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка обработки клиента: {ex.Message}");
                byte[] errorResponse = Encoding.ASCII.GetBytes("ERROR");
                stream.Write(errorResponse, 0, errorResponse.Length);
            }
            finally
            {
                tcpClient.Close();
            }
        }

        private decimal GetLastStockPrice(string ticker)
        {
            using (var context = new StockDbContext())
            {
                var tickerEntity = context.Tickers
                    .FirstOrDefault(t => t.Symbol == ticker);

                if (tickerEntity == null)
                {
                    Console.WriteLine($"Тикер {ticker} не найден");
                    return 0;
                }

                var lastPrice = context.Prices
                    .Where(p => p.TickerId == tickerEntity.Id)
                    .OrderByDescending(p => p.Date)
                    .FirstOrDefault();

                return lastPrice?.StockPrice ?? 0;
            }
        }
    }

    // TCP Клиент
    class Program
    {
        static void Main()
        {
            // Запускаем сервер в отдельном потоке
            Thread serverThread = new Thread(() =>
            {
                var server = new StockPriceServer();
                server.Start();
            });
            serverThread.Start();

            // Даем серверу время запуститься
            Thread.Sleep(1000);

            // Клиентская часть
            while (true)
            {
                Console.Write("Введите тикер: ");
                string ticker = Console.ReadLine().Trim().ToUpper();

                try
                {
                    TcpClient client = new TcpClient();
                    client.Connect(IPAddress.Loopback, 8888);

                    NetworkStream stream = client.GetStream();

                    // Отправляем тикер
                    byte[] data = Encoding.ASCII.GetBytes(ticker);
                    stream.Write(data, 0, data.Length);

                    // Получаем ответ
                    byte[] buffer = new byte[1024];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);

                    Console.WriteLine($"Последняя цена для {ticker}: {response}");

                    client.Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка: {ex.Message}");
                }
            }
        }
    }
}
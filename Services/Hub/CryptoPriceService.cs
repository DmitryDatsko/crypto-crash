using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json.Linq;

namespace CryptoCrush.Services.Hub;

public class CryptoPriceService: BackgroundService
{
    private readonly ClientWebSocket _client = new();
    private static readonly Uri Uri = new("wss://stream.bybit.com/v5/public/spot");
    private const string Topic = "tickers.BTCUSDT";
    private decimal _price;
    public decimal Price
    {
        get => _price;
        private set
        {
            _price = value;
            OnPriceChanged?.Invoke(this, value);
        }
    }
    public event EventHandler<decimal>? OnPriceChanged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _client.ConnectAsync(Uri, stoppingToken);

            string subscribeMessage = $@"{{
                ""op"": ""subscribe"",
                ""args"": [""{Topic}""]
            }}";

            await _client.SendAsync(
                new ArraySegment<byte>(Encoding.UTF8.GetBytes(subscribeMessage)),
                WebSocketMessageType.Text,
                true, CancellationToken.None);

            var buffer = new byte[8192];

            while (_client.State == WebSocketState.Open)
            {
                var result = await _client.ReceiveAsync(new ArraySegment<byte>(buffer), stoppingToken);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    JObject json = JObject.Parse(message);

                    if (json["topic"]?.ToString() == Topic)
                    {
                        var spot = json["data"];
                        if (spot != null)
                        {
                            var closePrice = spot["lastPrice"]?.ToString();

                            if (decimal.TryParse(closePrice, CultureInfo.InvariantCulture, out var price))
                            {
                                Price = price;
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            if (_client.State == WebSocketState.Open || _client.State == WebSocketState.CloseReceived)
            {
                await _client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Service stopping", stoppingToken);
            }

            _client.Dispose();
        }
    }
}
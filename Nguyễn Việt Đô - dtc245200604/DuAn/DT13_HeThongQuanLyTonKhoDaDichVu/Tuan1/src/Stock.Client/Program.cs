using System.Net.Http.Json;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
var url = args.ElementAtOrDefault(0) ?? "http://127.0.0.1:5183";
var productId = args.ElementAtOrDefault(1) ?? "CAP-USB-C";
var quantityText = args.ElementAtOrDefault(2) ?? "2";
if (!Uri.TryCreate(url, UriKind.Absolute, out var address) ||
    address.Scheme is not ("http" or "https") ||
    !int.TryParse(quantityText, out var quantity) || quantity <= 0)
{
    Console.Error.WriteLine("Cách dùng: Stock.Client [URL API] [mã sản phẩm] [số lượng > 0]");
    return 1;
}

using var http = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };
try
{
    Console.WriteLine("Sản phẩm trong kho:");
    foreach (var product in await ReadProducts(http))
        Console.WriteLine($"- {product.ProductId}: {product.Name} (còn {product.AvailableQuantity})");

    Console.WriteLine($"Đặt {quantity} x {productId}");
    using var created = await http.PostAsJsonAsync("/orders", new CreateOrderRequest(productId, quantity));
    Console.WriteLine($"HTTP {(int)created.StatusCode} POST /orders");
    if (!created.IsSuccessStatusCode)
    {
        var error = await created.Content.ReadFromJsonAsync<OrderError>();
        Console.Error.WriteLine(error?.Error ?? "Không tạo được đơn hàng.");
        if (error?.OrderId is Guid rejectedId)
            Console.Error.WriteLine($"Đơn {rejectedId}: {error.Status}");
        return 1;
    }

    if (created.Headers.Location is not Uri location)
    {
        Console.Error.WriteLine("API không trả địa chỉ tra cứu đơn hàng.");
        return 1;
    }

    using var found = await http.GetAsync(location);
    Console.WriteLine($"HTTP {(int)found.StatusCode} GET {found.RequestMessage?.RequestUri?.AbsolutePath}");
    found.EnsureSuccessStatusCode();
    var order = await found.Content.ReadFromJsonAsync<Order>();
    if (order is null)
    {
        Console.Error.WriteLine("API không trả thông tin đơn hàng.");
        return 1;
    }
    Console.WriteLine($"Đơn {order.Id}: {order.Quantity} x {order.ProductId} - {order.Status}");

    foreach (var product in await ReadProducts(http))
        if (product.ProductId == order.ProductId)
            Console.WriteLine($"Còn {product.AvailableQuantity} {product.ProductId} trong kho.");
    return 0;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Lỗi HTTP: {ex.Message}");
    return 1;
}
catch (TaskCanceledException)
{
    Console.Error.WriteLine("API không phản hồi đúng thời hạn.");
    return 1;
}

static async Task<Product[]> ReadProducts(HttpClient http)
{
    using var response = await http.GetAsync("/products");
    Console.WriteLine($"HTTP {(int)response.StatusCode} GET /products");
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<Product[]>() ?? [];
}

record Product(string ProductId, string Name, int AvailableQuantity);
record CreateOrderRequest(string ProductId, int Quantity);
record Order(Guid Id, string ProductId, int Quantity, string Status);
record OrderError(string Error, Guid? OrderId, string? Status);

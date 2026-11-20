var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var products = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase)
{
    ["CAP-USB-C"] = new("CAP-USB-C", "Cáp USB-C", 5),
    ["CHUOT-USB"] = new("CHUOT-USB", "Chuột USB", 8),
    ["DE-KE-LAPTOP"] = new("DE-KE-LAPTOP", "Đế kê laptop", 3)
};
var orders = new Dictionary<Guid, Order>();
var stockLock = new object();

app.MapGet("/products", () =>
{
    lock (stockLock)
        return Results.Ok(products.Values.ToArray());
});

app.MapPost("/orders", (CreateOrderRequest request) =>
{
    var productId = request.ProductId?.Trim();
    if (string.IsNullOrEmpty(productId) || request.Quantity <= 0)
        return Results.BadRequest(new { error = "Cần nhập mã sản phẩm và số lượng lớn hơn 0." });

    // Kiểm tra và trừ tồn trong cùng một khóa để hai đơn không lấy cùng số hàng.
    lock (stockLock)
    {
        if (!products.TryGetValue(productId, out var product))
            return Results.NotFound(new { error = "Không tìm thấy sản phẩm." });

        var id = Guid.NewGuid();
        if (request.Quantity > product.AvailableQuantity)
        {
            var rejected = new Order(id, product.ProductId, request.Quantity, "Rejected");
            orders.Add(id, rejected);
            return Results.BadRequest(new { error = "Không đủ hàng trong kho.", orderId = id, status = rejected.Status });
        }

        var accepted = new Order(id, product.ProductId, request.Quantity, "Accepted");
        orders.Add(id, accepted);
        products[product.ProductId] = product with { AvailableQuantity = product.AvailableQuantity - request.Quantity };
        return Results.Created($"/orders/{id}", accepted);
    }
});

app.MapGet("/orders/{id:guid}", (Guid id) =>
{
    lock (stockLock)
        return orders.TryGetValue(id, out var order)
            ? Results.Ok(order)
            : Results.NotFound(new { error = "Không tìm thấy đơn hàng." });
});

app.Run();

record Product(string ProductId, string Name, int AvailableQuantity);
record CreateOrderRequest(string? ProductId, int Quantity);
record Order(Guid Id, string ProductId, int Quantity, string Status);

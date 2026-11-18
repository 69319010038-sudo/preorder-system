using System.Net.Http.Json;
using System.Text.Json;

namespace PreOrder.App;

// ---- DTO ฝั่งแอป (ตรงกับ JSON ของ API) ----
public record MenuDto(Guid Id, string Name, decimal Price, bool IsAvailable);
public record RoundDto(Guid Id, string Name, DateTimeOffset OpenAt, DateTimeOffset CloseAt, string State);
public record RoundsDto(DateTimeOffset ServerTime, List<RoundDto> Rounds);
public record ItemDto(Guid MenuId, int Quantity, decimal UnitPrice, decimal LineTotal);
public record OrderDto(string Code, string CustomerName, Guid RoundId, string Status, List<ItemDto> Items, decimal Total);
public record ItemReq(Guid MenuId, int Quantity);

public class ApiException(string message) : Exception(message);

public class ApiClient
{
    // Android emulator ใช้ http://10.0.2.2:5000 ; เครื่องจริงใช้ IP/URL ของเซิร์ฟเวอร์
    private static readonly string BaseUrl = DeviceInfo.Platform == DevicePlatform.Android
        ? "http://10.0.2.2:5000" : "http://localhost:5000";

    private readonly HttpClient http = new() { BaseAddress = new Uri(BaseUrl) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<List<MenuDto>> GetMenusAsync() => Get<List<MenuDto>>("/menus");
    public Task<RoundsDto> GetRoundsAsync() => Get<RoundsDto>("/rounds");
    public Task<OrderDto> GetOrderAsync(string code) => Get<OrderDto>($"/orders/{code}");

    public Task<OrderDto> CreateOrderAsync(string name, Guid roundId, List<ItemReq> items) =>
        Send<OrderDto>(HttpMethod.Post, "/orders", new { customerName = name, roundId, items });

    public Task<OrderDto> UpdateOrderAsync(string code, string name, List<ItemReq> items) =>
        Send<OrderDto>(HttpMethod.Put, $"/orders/{code}", new { customerName = name, items });

    public Task<OrderDto> CancelOrderAsync(string code) =>
        Send<OrderDto>(HttpMethod.Post, $"/orders/{code}/cancel", null);

    private async Task<T> Get<T>(string url) => await Read<T>(await http.GetAsync(url));

    private async Task<T> Send<T>(HttpMethod method, string url, object? body)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body, options: Json);
        return await Read<T>(await http.SendAsync(req));
    }

    private static async Task<T> Read<T>(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode) return (await res.Content.ReadFromJsonAsync<T>(Json))!;
        string msg = "เกิดข้อผิดพลาด";
        try { msg = (await res.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("message").GetString() ?? msg; } catch { }
        throw new ApiException(msg);    // เช่น "รอบนี้ปิดรับคำสั่งซื้อแล้ว" จาก Server
    }
}

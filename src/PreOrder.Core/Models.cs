namespace PreOrder.Core;

public enum OrderStatus { Active, Cancelled }

public sealed class Menu
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; } = true;
}

public sealed class Round
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset OpenAt { get; set; }
    public DateTimeOffset CloseAt { get; set; }
}

public sealed class OrderItem
{
    public Guid Id { get; set; }
    public Guid MenuId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }          // ราคา ณ เวลาสั่ง
    public decimal LineTotal => Quantity * UnitPrice;
}

public sealed class Order
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public Guid RoundId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<OrderItem> Items { get; set; } = new();
    public decimal Total => Items.Sum(i => i.LineTotal);   // คำนวณ ไม่เก็บเป็นฟิลด์
}

// ---- คำขอจาก Client ----
public sealed record MenuRequest(string Name, decimal Price, bool IsAvailable = true);
public sealed record OrderItemRequest(Guid MenuId, int Quantity);
public sealed record CreateOrderRequest(string CustomerName, Guid RoundId, List<OrderItemRequest> Items);
public sealed record UpdateOrderRequest(string CustomerName, List<OrderItemRequest> Items);

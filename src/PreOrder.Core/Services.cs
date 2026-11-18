using System.Security.Cryptography;

namespace PreOrder.Core;

public interface IRepository
{
    Task<IReadOnlyList<Menu>> GetMenusAsync();
    Task<Menu?> GetMenuAsync(Guid id);
    Task AddMenuAsync(Menu m);
    Task UpdateMenuAsync(Menu m);
    Task<bool> DeleteMenuAsync(Guid id);
    Task<bool> IsMenuUsedAsync(Guid id);
    Task<IReadOnlyList<Round>> GetRoundsAsync();
    Task<Round?> GetRoundAsync(Guid id);
    Task AddOrderAsync(Order o);
    Task<Order?> GetOrderByCodeAsync(string code);
    Task UpdateOrderAsync(Order o);   // แทนที่รายการอาหารทั้งหมด + ชื่อ + สถานะ + เวลาแก้ไข
}

/// <summary>จัดการเมนู: ห้ามทำหลัง "รอบล่าสุดที่เริ่มแล้ว" ปิดรับ</summary>
public sealed class MenuService(IRepository repo, TimeProvider clock)
{
    public Task<IReadOnlyList<Menu>> ListAsync() => repo.GetMenusAsync();

    public async Task<Menu> AddAsync(MenuRequest req)
    {
        await EnsureManageableAsync();
        Validate(req);
        var m = new Menu { Id = Guid.NewGuid(), Name = req.Name.Trim(), Price = req.Price, IsAvailable = req.IsAvailable };
        await repo.AddMenuAsync(m);
        return m;
    }

    public async Task<Menu> UpdateAsync(Guid id, MenuRequest req)
    {
        await EnsureManageableAsync();
        Validate(req);
        var m = await repo.GetMenuAsync(id) ?? throw new DomainException(ErrorCode.NotFound, "ไม่พบเมนู");
        m.Name = req.Name.Trim(); m.Price = req.Price; m.IsAvailable = req.IsAvailable;
        await repo.UpdateMenuAsync(m);
        return m;
    }

    public async Task DeleteAsync(Guid id)
    {
        await EnsureManageableAsync();
        if (await repo.IsMenuUsedAsync(id))
            throw new DomainException(ErrorCode.Conflict, "เมนูนี้ถูกสั่งไปแล้ว ลบไม่ได้ (ให้ปิดการขายแทน)");
        if (!await repo.DeleteMenuAsync(id)) throw new DomainException(ErrorCode.NotFound, "ไม่พบเมนู");
    }

    private async Task EnsureManageableAsync()
    {
        var now = clock.GetUtcNow();
        var current = (await repo.GetRoundsAsync()).Where(r => r.OpenAt <= now).MaxBy(r => r.OpenAt);
        if (current is not null && RoundRules.GetState(current, now) == RoundState.Closed)
            throw new DomainException(ErrorCode.RoundClosed, "รอบปิดรับแล้ว ห้ามจัดการเมนู");
    }

    private static void Validate(MenuRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) throw new DomainException(ErrorCode.Validation, "ต้องระบุชื่อเมนู");
        if (r.Price < 0) throw new DomainException(ErrorCode.Validation, "ราคาต้องไม่ติดลบ");
    }
}

/// <summary>คำสั่งซื้อ: เพิ่ม/แก้ไข/ยกเลิก ได้เฉพาะตอนรอบเปิดรับ (ตามเวลา Server)</summary>
public sealed class OrderService(IRepository repo, TimeProvider clock)
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // ตัดตัวที่สับสน 0/O 1/I

    public async Task<Order> CreateAsync(CreateOrderRequest req)
    {
        var now = clock.GetUtcNow();                       // เวลา Server เท่านั้น
        var round = await repo.GetRoundAsync(req.RoundId) ?? throw new DomainException(ErrorCode.NotFound, "ไม่พบรอบ");
        RoundRules.EnsureOpen(round, now);
        ValidateName(req.CustomerName);

        var order = new Order
        {
            Id = Guid.NewGuid(), Code = await NewCodeAsync(), CustomerName = req.CustomerName.Trim(),
            RoundId = round.Id, Status = OrderStatus.Active, CreatedAt = now, UpdatedAt = now,
            Items = await BuildItemsAsync(req.Items, existing: null)
        };
        await repo.AddOrderAsync(order);
        return order;
    }

    public async Task<Order> GetAsync(string code) => await FindAsync(code);

    public async Task<Order> UpdateAsync(string code, UpdateOrderRequest req)
    {
        var now = clock.GetUtcNow();
        var order = await FindAsync(code);
        await EnsureEditableAsync(order, now);
        ValidateName(req.CustomerName);

        order.CustomerName = req.CustomerName.Trim();
        order.Items = await BuildItemsAsync(req.Items, existing: order.Items);
        order.UpdatedAt = now;
        await repo.UpdateOrderAsync(order);
        return order;
    }

    public async Task<Order> CancelAsync(string code)
    {
        var now = clock.GetUtcNow();
        var order = await FindAsync(code);
        await EnsureEditableAsync(order, now);
        order.Status = OrderStatus.Cancelled;
        order.UpdatedAt = now;
        await repo.UpdateOrderAsync(order);
        return order;
    }

    // ---- helpers ----
    private async Task<Order> FindAsync(string code) =>
        await repo.GetOrderByCodeAsync((code ?? "").Trim().ToUpperInvariant())
        ?? throw new DomainException(ErrorCode.NotFound, "ไม่พบคำสั่งซื้อ หรือรหัสไม่ถูกต้อง");

    private async Task EnsureEditableAsync(Order order, DateTimeOffset now)
    {
        if (order.Status == OrderStatus.Cancelled)
            throw new DomainException(ErrorCode.Conflict, "คำสั่งซื้อนี้ถูกยกเลิกแล้ว");
        var round = await repo.GetRoundAsync(order.RoundId) ?? throw new DomainException(ErrorCode.NotFound, "ไม่พบรอบ");
        RoundRules.EnsureOpen(round, now);
    }

    private async Task<List<OrderItem>> BuildItemsAsync(List<OrderItemRequest>? reqs, List<OrderItem>? existing)
    {
        if (reqs is null || reqs.Count == 0) throw new DomainException(ErrorCode.Validation, "ต้องมีรายการอาหารอย่างน้อย 1 รายการ");
        if (reqs.Any(r => r.Quantity <= 0)) throw new DomainException(ErrorCode.Validation, "จำนวนต้องมากกว่า 0");

        var items = new List<OrderItem>();
        foreach (var g in reqs.GroupBy(r => r.MenuId))          // รวมเมนูซ้ำเป็นบรรทัดเดียว
        {
            var menu = await repo.GetMenuAsync(g.Key) ?? throw new DomainException(ErrorCode.NotFound, "ไม่พบเมนูที่สั่ง");
            var old = existing?.FirstOrDefault(i => i.MenuId == g.Key);
            if (!menu.IsAvailable && old is null)
                throw new DomainException(ErrorCode.Validation, $"เมนู {menu.Name} ปิดขายอยู่");
            items.Add(new OrderItem
            {
                Id = old?.Id ?? Guid.NewGuid(), MenuId = g.Key, Quantity = g.Sum(r => r.Quantity),
                UnitPrice = old?.UnitPrice ?? menu.Price       // เมนูเดิมคงราคา ณ เวลาสั่งเดิม
            });
        }
        return items;
    }

    private async Task<string> NewCodeAsync()
    {
        while (true)
        {
            var code = string.Create(8, 0, (span, _) =>
            { for (int i = 0; i < span.Length; i++) span[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]; });
            if (await repo.GetOrderByCodeAsync(code) is null) return code;
        }
    }

    private static void ValidateName(string? n)
    {
        if (string.IsNullOrWhiteSpace(n)) throw new DomainException(ErrorCode.Validation, "ต้องระบุชื่อผู้สั่ง");
    }
}

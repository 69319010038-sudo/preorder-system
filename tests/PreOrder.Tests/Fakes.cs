using PreOrder.Core;

namespace PreOrder.Tests;

public sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class FakeRepo : IRepository
{
    public List<Menu> Menus = new();
    public List<Round> Rounds = new();
    public List<Order> Orders = new();

    public Task<IReadOnlyList<Menu>> GetMenusAsync() => Task.FromResult<IReadOnlyList<Menu>>(Menus.ToList());
    public Task<Menu?> GetMenuAsync(Guid id) => Task.FromResult(Menus.FirstOrDefault(m => m.Id == id));
    public Task AddMenuAsync(Menu m) { Menus.Add(m); return Task.CompletedTask; }
    public Task UpdateMenuAsync(Menu m) => Task.CompletedTask;
    public Task<bool> DeleteMenuAsync(Guid id) => Task.FromResult(Menus.RemoveAll(m => m.Id == id) > 0);
    public Task<bool> IsMenuUsedAsync(Guid id) => Task.FromResult(Orders.Any(o => o.Items.Any(i => i.MenuId == id)));
    public Task<IReadOnlyList<Round>> GetRoundsAsync() => Task.FromResult<IReadOnlyList<Round>>(Rounds.ToList());
    public Task<Round?> GetRoundAsync(Guid id) => Task.FromResult(Rounds.FirstOrDefault(r => r.Id == id));
    public Task AddOrderAsync(Order o) { Orders.Add(o); return Task.CompletedTask; }
    public Task<Order?> GetOrderByCodeAsync(string code) => Task.FromResult(Orders.FirstOrDefault(o => o.Code == code));
    public Task UpdateOrderAsync(Order o) => Task.CompletedTask;   // ออบเจ็กต์เดียวกันในหน่วยความจำ
}

using PreOrder.Core;

namespace PreOrder.Tests;

public class CutoffTests
{
    // รอบ: เปิด 10:00 ปิด 12:00 เวลาไทย (+07:00) = 03:00Z – 05:00Z
    private static readonly DateTimeOffset Open  = new(2026, 11, 18, 10, 0, 0, TimeSpan.FromHours(7));
    private static readonly DateTimeOffset Close = new(2026, 11, 18, 12, 0, 0, TimeSpan.FromHours(7));

    private readonly FakeRepo repo = new();
    private readonly FakeClock clock = new(Open.AddHours(1));       // เริ่มที่ 11:00 (รอบเปิดอยู่)
    private readonly Menu menu = new() { Id = Guid.NewGuid(), Name = "ข้าวมันไก่", Price = 45 };
    private readonly Round round = new() { Id = Guid.NewGuid(), Name = "รอบเที่ยง", OpenAt = Open, CloseAt = Close };
    private readonly OrderService orders;
    private readonly MenuService menus;

    public CutoffTests()
    {
        repo.Menus.Add(menu); repo.Rounds.Add(round);
        orders = new OrderService(repo, clock);
        menus = new MenuService(repo, clock);
    }

    private Task<Order> Place(int qty = 2) =>
        orders.CreateAsync(new CreateOrderRequest("สมชาย", round.Id, [new(menu.Id, qty)]));

    private static async Task AssertCode(ErrorCode code, Func<Task> act) =>
        Assert.Equal(code, (await Assert.ThrowsAsync<DomainException>(act)).Code);

    // ---------- เวลาปิดรับ ----------
    [Fact] public async Task Create_BeforeClose_Works_AndTotalIsQtyTimesPrice()
    {
        var o = await Place(3);
        Assert.Equal(135m, o.Total);
        Assert.Equal(8, o.Code.Length);
    }

    [Fact] public async Task Create_OneSecondBeforeClose_Works()
    {
        clock.Now = Close.AddSeconds(-1);
        await Place();
    }

    [Fact] public async Task Create_ExactlyAtClose_IsRejected()
    {
        clock.Now = Close;
        await AssertCode(ErrorCode.RoundClosed, () => Place());
    }

    [Fact] public async Task Create_BeforeOpen_IsRejected()
    {
        clock.Now = Open.AddSeconds(-1);
        await AssertCode(ErrorCode.RoundNotOpen, () => Place());
    }

    [Fact] public async Task Timezone_Bangkok1200_Equals_0500Utc()
    {
        clock.Now = new DateTimeOffset(2026, 11, 18, 4, 59, 59, TimeSpan.Zero);   // 11:59:59 ไทย
        await Place();
        clock.Now = new DateTimeOffset(2026, 11, 18, 5, 0, 0, TimeSpan.Zero);     // 12:00:00 ไทย
        await AssertCode(ErrorCode.RoundClosed, () => Place());
    }

    [Fact] public async Task Update_AfterClose_IsRejected()
    {
        var o = await Place();
        clock.Now = Close.AddMinutes(1);
        await AssertCode(ErrorCode.RoundClosed,
            () => orders.UpdateAsync(o.Code, new UpdateOrderRequest("สมชาย", [new(menu.Id, 5)])));
    }

    [Fact] public async Task Cancel_AfterClose_IsRejected()
    {
        var o = await Place();
        clock.Now = Close.AddMinutes(1);
        await AssertCode(ErrorCode.RoundClosed, () => orders.CancelAsync(o.Code));
    }

    // ---------- เมนู ----------
    [Fact] public async Task Menu_Add_BeforeClose_Works()
    {
        await menus.AddAsync(new MenuRequest("ชาเย็น", 25));
        Assert.Equal(2, repo.Menus.Count);
    }

    [Fact] public async Task Menu_Add_Update_Delete_AfterClose_AreRejected()
    {
        clock.Now = Close.AddMinutes(1);
        await AssertCode(ErrorCode.RoundClosed, () => menus.AddAsync(new MenuRequest("ชาเย็น", 25)));
        await AssertCode(ErrorCode.RoundClosed, () => menus.UpdateAsync(menu.Id, new MenuRequest("x", 1)));
        await AssertCode(ErrorCode.RoundClosed, () => menus.DeleteAsync(menu.Id));
    }

    // ---------- กติกาอื่น ----------
    [Fact] public async Task Update_BeforeClose_ChangesItems_AndKeepsOriginalUnitPrice()
    {
        var o = await Place(1);
        menu.Price = 60;                                            // ราคาเมนูขึ้นหลังสั่ง
        var u = await orders.UpdateAsync(o.Code.ToLower(), new UpdateOrderRequest("สมหญิง", [new(menu.Id, 2)]));
        Assert.Equal("สมหญิง", u.CustomerName);
        Assert.Equal(90m, u.Total);                                 // 2 x 45 (ราคา ณ เวลาสั่งเดิม)
    }

    [Fact] public async Task Cancel_BeforeClose_Works_ThenEditIsRejected()
    {
        var o = await Place();
        var c = await orders.CancelAsync(o.Code);
        Assert.Equal(OrderStatus.Cancelled, c.Status);
        await AssertCode(ErrorCode.Conflict,
            () => orders.UpdateAsync(o.Code, new UpdateOrderRequest("สมชาย", [new(menu.Id, 1)])));
    }

    [Fact] public async Task WrongCode_IsNotFound() =>
        await AssertCode(ErrorCode.NotFound, () => orders.GetAsync("ZZZZZZZZ"));

    [Theory]
    [InlineData("", 1)]
    [InlineData("สมชาย", 0)]
    public async Task InvalidInput_IsRejected(string name, int qty) =>
        await AssertCode(ErrorCode.Validation,
            () => orders.CreateAsync(new CreateOrderRequest(name, round.Id, [new(menu.Id, qty)])));
}

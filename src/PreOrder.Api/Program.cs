using Npgsql;
using PreOrder.Api;
using PreOrder.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);   // ไฟล์ลับ ไม่ขึ้น Git
builder.Services.AddSingleton(TimeProvider.System);                       // นาฬิกาฝั่ง Server
builder.Services.AddSingleton(NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Supabase") ?? throw new InvalidOperationException("ไม่พบ ConnectionStrings:Supabase")));
builder.Services.AddSingleton<IRepository, NpgsqlRepository>();
builder.Services.AddScoped<MenuService>();
builder.Services.AddScoped<OrderService>();

var app = builder.Build();

// แปลง DomainException -> HTTP status + JSON {code,message}
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (DomainException ex)
    {
        ctx.Response.StatusCode = ex.Code switch
        {
            ErrorCode.Validation => 400,
            ErrorCode.NotFound => 404,
            _ => 409                                   // RoundNotOpen / RoundClosed / Conflict
        };
        await ctx.Response.WriteAsJsonAsync(new { code = ex.Code.ToString(), message = ex.Message });
    }
});

// ---- เมนู ----
app.MapGet("/menus", (MenuService s) => s.ListAsync());
app.MapPost("/menus", async (MenuRequest r, MenuService s) => Results.Created("/menus", await s.AddAsync(r)));
app.MapPut("/menus/{id:guid}", async (Guid id, MenuRequest r, MenuService s) => await s.UpdateAsync(id, r));
app.MapDelete("/menus/{id:guid}", async (Guid id, MenuService s) => { await s.DeleteAsync(id); return Results.NoContent(); });

// ---- รอบ (ส่ง state + เวลา Server กลับไปให้ Client แสดงผล) ----
app.MapGet("/rounds", async (IRepository repo, TimeProvider clock) =>
{
    var now = clock.GetUtcNow();
    var rounds = await repo.GetRoundsAsync();
    return new
    {
        serverTime = Bangkok.ToLocal(now),
        rounds = rounds.Select(r => new
        {
            r.Id, r.Name,
            openAt = Bangkok.ToLocal(r.OpenAt), closeAt = Bangkok.ToLocal(r.CloseAt),
            state = RoundRules.GetState(r, now).ToString()
        })
    };
});

// ---- คำสั่งซื้อ ----
app.MapPost("/orders", async (CreateOrderRequest r, OrderService s) => Results.Created($"/orders", ToDto(await s.CreateAsync(r))));
app.MapGet("/orders/{code}", async (string code, OrderService s) => ToDto(await s.GetAsync(code)));
app.MapPut("/orders/{code}", async (string code, UpdateOrderRequest r, OrderService s) => ToDto(await s.UpdateAsync(code, r)));
app.MapPost("/orders/{code}/cancel", async (string code, OrderService s) => ToDto(await s.CancelAsync(code)));

app.Run();

static object ToDto(Order o) => new
{
    o.Code, o.CustomerName, o.RoundId,
    status = o.Status.ToString().ToLowerInvariant(),
    createdAt = Bangkok.ToLocal(o.CreatedAt), updatedAt = Bangkok.ToLocal(o.UpdatedAt),
    items = o.Items.Select(i => new { i.MenuId, i.Quantity, i.UnitPrice, i.LineTotal }),
    total = o.Total
};

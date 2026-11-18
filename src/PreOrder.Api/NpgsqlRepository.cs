using Npgsql;
using PreOrder.Core;

namespace PreOrder.Api;

/// <summary>เข้าถึง Supabase (Postgres) ตรงด้วย Npgsql</summary>
public sealed class NpgsqlRepository(NpgsqlDataSource db) : IRepository
{
    private static Menu ReadMenu(NpgsqlDataReader r) => new()
    { Id = r.GetGuid(0), Name = r.GetString(1), Price = r.GetDecimal(2), IsAvailable = r.GetBoolean(3) };

    private static Round ReadRound(NpgsqlDataReader r) => new()
    { Id = r.GetGuid(0), Name = r.GetString(1),
      OpenAt = r.GetFieldValue<DateTimeOffset>(2), CloseAt = r.GetFieldValue<DateTimeOffset>(3) };

    public async Task<IReadOnlyList<Menu>> GetMenusAsync()
    {
        await using var cmd = db.CreateCommand("select id,name,price,is_available from menus order by name");
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Menu>();
        while (await r.ReadAsync()) list.Add(ReadMenu(r));
        return list;
    }

    public async Task<Menu?> GetMenuAsync(Guid id)
    {
        await using var cmd = db.CreateCommand("select id,name,price,is_available from menus where id=$1");
        cmd.Parameters.AddWithValue(id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadMenu(r) : null;
    }

    public async Task AddMenuAsync(Menu m)
    {
        await using var cmd = db.CreateCommand("insert into menus(id,name,price,is_available) values($1,$2,$3,$4)");
        cmd.Parameters.AddWithValue(m.Id); cmd.Parameters.AddWithValue(m.Name);
        cmd.Parameters.AddWithValue(m.Price); cmd.Parameters.AddWithValue(m.IsAvailable);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateMenuAsync(Menu m)
    {
        await using var cmd = db.CreateCommand("update menus set name=$2,price=$3,is_available=$4 where id=$1");
        cmd.Parameters.AddWithValue(m.Id); cmd.Parameters.AddWithValue(m.Name);
        cmd.Parameters.AddWithValue(m.Price); cmd.Parameters.AddWithValue(m.IsAvailable);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> DeleteMenuAsync(Guid id)
    {
        await using var cmd = db.CreateCommand("delete from menus where id=$1");
        cmd.Parameters.AddWithValue(id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> IsMenuUsedAsync(Guid id)
    {
        await using var cmd = db.CreateCommand("select exists(select 1 from order_items where menu_id=$1)");
        cmd.Parameters.AddWithValue(id);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<IReadOnlyList<Round>> GetRoundsAsync()
    {
        await using var cmd = db.CreateCommand("select id,name,open_at,close_at from rounds order by open_at");
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Round>();
        while (await r.ReadAsync()) list.Add(ReadRound(r));
        return list;
    }

    public async Task<Round?> GetRoundAsync(Guid id)
    {
        await using var cmd = db.CreateCommand("select id,name,open_at,close_at from rounds where id=$1");
        cmd.Parameters.AddWithValue(id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadRound(r) : null;
    }

    public async Task AddOrderAsync(Order o)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand(
            "insert into orders(id,code,customer_name,round_id,status,created_at,updated_at) values($1,$2,$3,$4,$5,$6,$7)", conn, tx))
        {
            cmd.Parameters.AddWithValue(o.Id); cmd.Parameters.AddWithValue(o.Code);
            cmd.Parameters.AddWithValue(o.CustomerName); cmd.Parameters.AddWithValue(o.RoundId);
            cmd.Parameters.AddWithValue(StatusText(o.Status));
            cmd.Parameters.AddWithValue(o.CreatedAt.ToUniversalTime()); cmd.Parameters.AddWithValue(o.UpdatedAt.ToUniversalTime());
            await cmd.ExecuteNonQueryAsync();
        }
        await InsertItemsAsync(conn, tx, o);
        await tx.CommitAsync();
    }

    public async Task<Order?> GetOrderByCodeAsync(string code)
    {
        await using var conn = await db.OpenConnectionAsync();
        Order? o;
        await using (var cmd = new NpgsqlCommand(
            "select id,code,customer_name,round_id,status,created_at,updated_at from orders where code=$1", conn))
        {
            cmd.Parameters.AddWithValue(code);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            o = new Order
            {
                Id = r.GetGuid(0), Code = r.GetString(1), CustomerName = r.GetString(2), RoundId = r.GetGuid(3),
                Status = r.GetString(4) == "cancelled" ? OrderStatus.Cancelled : OrderStatus.Active,
                CreatedAt = r.GetFieldValue<DateTimeOffset>(5), UpdatedAt = r.GetFieldValue<DateTimeOffset>(6)
            };
        }
        await using (var cmd = new NpgsqlCommand(
            "select id,menu_id,quantity,unit_price from order_items where order_id=$1", conn))
        {
            cmd.Parameters.AddWithValue(o.Id);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                o.Items.Add(new OrderItem { Id = r.GetGuid(0), MenuId = r.GetGuid(1), Quantity = r.GetInt32(2), UnitPrice = r.GetDecimal(3) });
        }
        return o;
    }

    public async Task UpdateOrderAsync(Order o)
    {
        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand(
            "update orders set customer_name=$2,status=$3,updated_at=$4 where id=$1", conn, tx))
        {
            cmd.Parameters.AddWithValue(o.Id); cmd.Parameters.AddWithValue(o.CustomerName);
            cmd.Parameters.AddWithValue(StatusText(o.Status)); cmd.Parameters.AddWithValue(o.UpdatedAt.ToUniversalTime());
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var del = new NpgsqlCommand("delete from order_items where order_id=$1", conn, tx))
        {
            del.Parameters.AddWithValue(o.Id);
            await del.ExecuteNonQueryAsync();
        }
        await InsertItemsAsync(conn, tx, o);
        await tx.CommitAsync();
    }

    private static string StatusText(OrderStatus s) => s == OrderStatus.Cancelled ? "cancelled" : "active";

    private static async Task InsertItemsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Order o)
    {
        foreach (var i in o.Items)
        {
            await using var cmd = new NpgsqlCommand(
                "insert into order_items(id,order_id,menu_id,quantity,unit_price) values($1,$2,$3,$4,$5)", conn, tx);
            cmd.Parameters.AddWithValue(i.Id); cmd.Parameters.AddWithValue(o.Id);
            cmd.Parameters.AddWithValue(i.MenuId); cmd.Parameters.AddWithValue(i.Quantity); cmd.Parameters.AddWithValue(i.UnitPrice);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}

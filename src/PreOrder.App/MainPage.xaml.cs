using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PreOrder.App;

// หนึ่งบรรทัดเมนูในตะกร้า
public class CartLine(MenuDto menu, Action changed) : INotifyPropertyChanged
{
    public MenuDto Menu { get; } = menu;
    public string Name => Menu.Name;
    public string PriceText => $"{Menu.Price:N0} ฿";
    private double qty;
    public double Qty
    {
        get => qty;
        set { qty = value; PropertyChanged?.Invoke(this, new(nameof(Qty))); changed(); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class MainPage : ContentPage
{
    private readonly ApiClient api = new();
    private readonly ObservableCollection<CartLine> cart = new();
    private RoundDto? round;

    public MainPage()
    {
        InitializeComponent();
        MenuList.ItemsSource = cart;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Run(async () =>
        {
            var rounds = await api.GetRoundsAsync();
            // รอบปัจจุบัน = รอบที่เปิดอยู่ ถ้าไม่มีใช้รอบล่าสุด
            round = rounds.Rounds.FirstOrDefault(r => r.State == "Open") ?? rounds.Rounds.LastOrDefault();
            TimeLabel.Text = $"เวลา Server: {rounds.ServerTime:dd/MM/yyyy HH:mm:ss} (Asia/Bangkok)";
            RoundLabel.Text = round is null ? "ยังไม่มีรอบ"
                : $"{round.Name} | ปิดรับ {round.CloseAt:HH:mm} | {StateText(round.State)}";

            cart.Clear();
            foreach (var m in (await api.GetMenusAsync()).Where(m => m.IsAvailable))
                cart.Add(new CartLine(m, UpdateTotal));
            UpdateTotal();
        });
    }

    private static string StateText(string s) => s switch
    { "Open" => "เปิดรับ", "Closed" => "ปิดรับแล้ว", _ => "ยังไม่เปิด" };

    private void UpdateTotal() =>
        TotalLabel.Text = $"ยอดรวม: {cart.Sum(c => (decimal)c.Qty * c.Menu.Price):N2} ฿";

    private List<ItemReq> Items() =>
        cart.Where(c => c.Qty > 0).Select(c => new ItemReq(c.Menu.Id, (int)c.Qty)).ToList();

    // ตัดสินว่าเพิ่ม/แก้ไข/ยกเลิกได้หรือไม่ ทำที่ Server เสมอ — ฝั่งแอปแค่แสดงข้อความที่ Server ตอบกลับ
    private async void OnSubmit(object? s, EventArgs e) => await Run(async () =>
    {
        if (round is null) throw new ApiException("ยังไม่มีรอบให้สั่ง");
        var o = await api.CreateOrderAsync(NameEntry.Text ?? "", round.Id, Items());
        CodeEntry.Text = o.Code;
        await DisplayAlert("สั่งสำเร็จ", $"รหัสคำสั่งซื้อ: {o.Code}\nจดไว้เพื่อแก้ไข/ยกเลิก\nยอดรวม {o.Total:N2} ฿", "ตกลง");
        StatusLabel.Text = $"สั่งแล้ว รหัส {o.Code}";
    });

    private async void OnLoad(object? s, EventArgs e) => await Run(async () =>
    {
        var o = await api.GetOrderAsync(CodeEntry.Text ?? "");
        NameEntry.Text = o.CustomerName;
        foreach (var c in cart) c.Qty = o.Items.FirstOrDefault(i => i.MenuId == c.Menu.Id)?.Quantity ?? 0;
        StatusLabel.Text = $"สถานะ: {(o.Status == "cancelled" ? "ยกเลิกแล้ว" : "ใช้งาน")} | ยอดรวม {o.Total:N2} ฿";
    });

    private async void OnSave(object? s, EventArgs e) => await Run(async () =>
    {
        var o = await api.UpdateOrderAsync(CodeEntry.Text ?? "", NameEntry.Text ?? "", Items());
        StatusLabel.Text = $"แก้ไขแล้ว ยอดรวม {o.Total:N2} ฿";
    });

    private async void OnCancel(object? s, EventArgs e) => await Run(async () =>
    {
        await api.CancelOrderAsync(CodeEntry.Text ?? "");
        StatusLabel.Text = "ยกเลิกคำสั่งซื้อแล้ว";
    });

    private async Task Run(Func<Task> action)
    {
        try { await action(); }
        catch (ApiException ex) { await DisplayAlert("ทำรายการไม่ได้", ex.Message, "ตกลง"); }
        catch (Exception) { await DisplayAlert("ผิดพลาด", "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้", "ตกลง"); }
    }
}

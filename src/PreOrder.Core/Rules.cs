namespace PreOrder.Core;

public enum ErrorCode { Validation, NotFound, RoundNotOpen, RoundClosed, Conflict }

public sealed class DomainException(ErrorCode code, string message) : Exception(message)
{
    public ErrorCode Code { get; } = code;
}

public enum RoundState { NotOpen, Open, Closed }

public static class Bangkok
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);   // Asia/Bangkok ไม่มี DST
    public static DateTimeOffset ToLocal(DateTimeOffset t) => t.ToOffset(Offset);
}

public static class RoundRules
{
    // เทียบเป็น "ช่วงเวลาจริง" (instant) จึงถูกต้องไม่ว่าจะเก็บเวลาในโซนไหน
    // เปิดรับ: OpenAt <= now < CloseAt  (ถึงเวลาปิดพอดี = ปิดแล้ว)
    public static RoundState GetState(Round r, DateTimeOffset now) =>
        now < r.OpenAt ? RoundState.NotOpen :
        now >= r.CloseAt ? RoundState.Closed : RoundState.Open;

    public static void EnsureOpen(Round r, DateTimeOffset now)
    {
        switch (GetState(r, now))
        {
            case RoundState.NotOpen: throw new DomainException(ErrorCode.RoundNotOpen, "รอบนี้ยังไม่เปิดรับคำสั่งซื้อ");
            case RoundState.Closed:  throw new DomainException(ErrorCode.RoundClosed, "รอบนี้ปิดรับคำสั่งซื้อแล้ว");
        }
    }
}

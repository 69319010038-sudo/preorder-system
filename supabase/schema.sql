-- ระบบสั่งอาหารล่วงหน้า: รันไฟล์นี้ใน Supabase > SQL Editor
create table menus (
  id           uuid primary key default gen_random_uuid(),
  name         text not null check (length(trim(name)) > 0),
  price        numeric(10,2) not null check (price >= 0),
  is_available boolean not null default true
);

create table rounds (
  id        uuid primary key default gen_random_uuid(),
  name      text not null,
  open_at   timestamptz not null,
  close_at  timestamptz not null,
  check (close_at > open_at)
);

create table orders (
  id            uuid primary key default gen_random_uuid(),
  code          text not null unique,            -- รหัสคำสั่งซื้อ ใช้แทนการล็อกอิน
  customer_name text not null check (length(trim(customer_name)) > 0),
  round_id      uuid not null references rounds(id),
  status        text not null default 'active' check (status in ('active','cancelled')),
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);

create table order_items (
  id         uuid primary key default gen_random_uuid(),
  order_id   uuid not null references orders(id) on delete cascade,
  menu_id    uuid not null references menus(id),
  quantity   int  not null check (quantity > 0),
  unit_price numeric(10,2) not null check (unit_price >= 0)  -- ราคา ณ เวลาสั่ง
);

create index on orders(round_id);
create index on order_items(order_id);

-- ปิดการเข้าถึงตรงจาก anon key: ให้เข้าผ่าน API เท่านั้น (API ใช้ connection string ของ Postgres)
alter table menus       enable row level security;
alter table rounds      enable row level security;
alter table orders      enable row level security;
alter table order_items enable row level security;

-- ข้อมูลตัวอย่าง (รอบเปิดอยู่ 3 ชั่วโมงนับจากตอนรัน)
insert into menus (name, price) values ('ข้าวกะเพราหมูสับ', 50), ('ข้าวมันไก่', 45), ('ชาเย็น', 25);
insert into rounds (name, open_at, close_at)
values ('รอบเที่ยง', now() - interval '1 hour', now() + interval '2 hours');

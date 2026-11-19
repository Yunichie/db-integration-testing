# BeanCart: Pengujian Integrasi Database dan SQL dengan Testcontainers

Tutorial untuk menguji *data layer* C#/.NET (Dapper + Npgsql) terhadap **database PostgreSQL sungguhan** yang dijalankan oleh Testcontainers di Docker. Respawn mengosongkan database sebelum setiap pengujian.

## Daftar isi

1. [Pengenalan](#1-pengenalan)
2. [Persiapan lingkungan](#2-persiapan-lingkungan)
3. [Instalasi](#3-instalasi)
4. [Implementasi](#4-implementasi)
5. [Cara menjalankan](#5-cara-menjalankan)
6. [Hasil yang diharapkan](#6-hasil-yang-diharapkan)
7. [Pemecahan masalah](#7-pemecahan-masalah)

## 1. Pengenalan

**BeanCart** adalah toko daring kecil yang menjual biji kopi dan alat seduh. Kita hanya menguji **data layer**-nya, yaitu kelas-kelas C# yang menjalankan SQL ke PostgreSQL. Tidak ada halaman web maupun API.

| Alat | Peran |
| --- | --- |
| [Npgsql](https://www.npgsql.org/) | Driver .NET untuk PostgreSQL: koneksi dan transaksi |
| [Dapper](https://github.com/DapperLib/Dapper) | Menjalankan SQL yang Anda tulis dan memetakan baris hasil ke objek C# |
| [Testcontainers](https://dotnet.testcontainers.org/) | Menjalankan PostgreSQL sungguhan di Docker sebelum pengujian dan menghapusnya setelah selesai |
| [Respawn](https://github.com/jbogard/Respawn) | Mengosongkan semua tabel di antara pengujian |
| [xUnit](https://xunit.net/) | Menjalankan pengujian dan membagikan satu kontainer database ke semua pengujian |

Pengujian mencakup enam topik: **JOIN, agregasi, filtering, transaksi, rollback, dan isolasi data.**

### Aturan bisnis

1. Sebuah pesanan dimiliki oleh satu pelanggan dan memiliki minimal satu baris.
2. Setiap baris menyimpan harga satuan saat pembelian, sehingga perubahan harga di kemudian hari tidak mengubah riwayat.
3. Status pesanan adalah `Pending`, `Paid`, `Shipped`, atau `Cancelled`.
4. Membuat pesanan mengurangi stok. Stok tidak pernah di bawah nol.
5. Membuat pesanan bersifat semua-atau-tidak-sama-sekali (*all-or-nothing*).
6. Pendapatan hanya menghitung pesanan `Paid` dan `Shipped`.
7. Email pelanggan bersifat unik.

### Model data

Satu pelanggan memiliki banyak pesanan, satu pesanan memiliki banyak baris, dan satu produk muncul di banyak baris. Lihat [`tests/BeanCart.Tests/Sql/schema.sql`](tests/BeanCart.Tests/Sql/schema.sql).

### Data seed

**Pelanggan**

| Pelanggan | Kota | Email |
| --- | --- | --- |
| Alice | Jakarta | alice@example.com |
| Budi | Bandung | budi@example.com |
| Citra | Jakarta | citra@example.com |
| Dewi | Surabaya | dewi@example.com |

**Produk**

| Label | Produk | Kategori | Harga (IDR) | Stok |
| --- | --- | --- | --- | --- |
| Arabica | Arabica Gayo 250g | Beans | 80.000 | 10 |
| Robusta | Robusta Lampung 250g | Beans | 50.000 | 20 |
| Toraja | Toraja Kalosi 250g | Beans | 95.000 | 5 |
| V60 | V60 Dripper | Equipment | 150.000 | 3 |
| Filters | Paper Filters (100) | Equipment | 30.000 | 2 |

**Pesanan dan barisnya**

| Pesanan | Pelanggan | Status | Tanggal pesan | Baris | Total (IDR) |
| --- | --- | --- | --- | --- | --- |
| O1 | Alice | Paid | 2026-10-01 | Arabica ×2, Robusta ×1 | 210.000 |
| O2 | Alice | Shipped | 2026-10-15 | V60 ×1, Arabica ×1 | 230.000 |
| O3 | Budi | Paid | 2026-10-20 | Toraja ×2 | 190.000 |
| O4 | Budi | Cancelled | 2026-10-22 | Robusta ×4 | 200.000 |
| O5 | Citra | Pending | 2026-11-02 | Arabica ×1, Filters ×1 | 110.000 |

Kolom stok dimasukkan langsung, tidak dihitung dari pesanan. Dewi sengaja tidak memiliki pesanan: ia menunjukkan perbedaan antara `INNER JOIN` dan `LEFT JOIN`. Totalnya ada 8 baris pesanan.



## 2. Persiapan lingkungan

| Alat | Versi | Cara cek |
| --- | --- | --- |
| .NET SDK | 10 (proyek menargetkan `net10.0`) | `dotnet --version` |
| Docker | Docker Desktop (Windows: backend WSL 2, macOS) atau Docker Engine (Linux) | `docker run --rm hello-world` |

- Docker harus sudah berjalan sebelum menjalankan pengujian. Tanpa Docker, semua pengujian gagal saat mulai.
- Di Linux, user Anda harus bisa memakai Docker tanpa `sudo`: jalankan `sudo usermod -aG docker $USER`, lalu logout dan login kembali.
- Jika memakai Podman, arahkan `DOCKER_HOST` ke socket Podman.
- Saat pertama kali dijalankan, dibutuhkan internet agar Docker bisa mengunduh image `postgres:17-alpine` dan NuGet bisa memulihkan paket.
- Opsional: klien SQL (DBeaver, pgAdmin, atau `psql`) untuk melihat isi database pengujian.

Diuji dengan .NET SDK 10.0.114, Docker 29.9.3, Dapper 2.1.89, Npgsql 10.0.3, Testcontainers.PostgreSql 4.17.0, Respawn 7.0.0, dan xunit 2.9.3.

## 3. Instalasi

```bash
git clone <URL repositori ini> BeanCart
cd BeanCart
docker pull postgres:17-alpine   # opsional
dotnet restore
dotnet build
```

## 4. Implementasi

### 4.1 Pola aplikasi

Setiap repository menerima sebuah `NpgsqlConnectionFactory`. Setiap method menulis SQL biasa dan membiarkan Dapper memetakan barisnya:

```cs
public async Task<IReadOnlyList<OrderDetailLine>> GetOrderDetailsAsync(int orderId)
{
    await using var conn = connections.Create();       // koneksi singkat untuk setiap pemanggilan
    var rows = await conn.QueryAsync<OrderDetailLine>("""
        SELECT c.name AS customer_name, p.name AS product_name, ...
        FROM orders o JOIN customers c ON ... JOIN order_items oi ON ... JOIN products p ON ...
        WHERE o.id = @OrderId
        """, new { OrderId = orderId });                // nilai selalu dikirim sebagai parameter
    return rows.AsList();
}
```

- Kolom memakai snake_case (`customer_name`) dan properti memakai PascalCase (`CustomerName`). `DefaultTypeMap.MatchNamesWithUnderscores = true` menghubungkan keduanya.
- Method pencarian hanya menambahkan kondisi `WHERE` untuk filter yang memiliki nilai, sehingga filter kosong berarti "abaikan".

Operasi tulis (`PlaceOrderAsync`, `CancelOrderAsync`) memakai **satu koneksi dan satu transaksi** untuk semua perintah dan baru melakukan commit di akhir:

```cs
await using var conn = connections.Create();
await conn.OpenAsync();
await using var tx = await conn.BeginTransactionAsync();

// insert pesanan, lalu untuk setiap baris: UPDATE stok ... RETURNING price, INSERT order_items
// kirim `tx` ke setiap pemanggilan Dapper

await tx.CommitAsync();   // jika ada error sebelum baris ini, dispose `tx` membatalkan (rollback) semuanya
```

Database juga menegakkan aturan: `CHECK (stock >= 0)`, `CHECK (quantity > 0)`, foreign key, dan email unik. Dengan begitu, pesanan yang salah tetap gagal walaupun kode C# melewatkannya.

### 4.2 Infrastruktur pengujian

**`PostgresFixture`** berjalan sekali per eksekusi pengujian:

```cs
public async Task InitializeAsync()
{
    await _container.StartAsync();                                  // 1. jalankan postgres:17-alpine
    // ...
    await conn.ExecuteAsync(await File.ReadAllTextAsync(schemaPath)); // 2. buat tabel
    _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions  // 3. setelah skema ada
    {
        DbAdapter = DbAdapter.Postgres,
        SchemasToInclude = new[] { "public" }
    });
}
```

**`DatabaseCollection`** membagikan satu fixture tersebut ke setiap kelas pengujian yang diberi `[Collection(DatabaseCollection.Name)]`. Kelas-kelas dalam koleksi yang sama berjalan satu per satu, sehingga tidak pernah berebut database.

**`DatabaseTestBase`** mengosongkan database sebelum **setiap** pengujian, karena xUnit membuat instance kelas baru untuk setiap pengujian:

```cs
public Task InitializeAsync() => Db.ResetDatabaseAsync();
```

**`SeedData.InsertBaselineAsync(conn)`** memasukkan data seed dari [bagian 1](#data-seed) dan mengembalikan id yang dibuat database berdasarkan label, misalnya `ids.Orders["O1"]` atau `ids.Products["Toraja"]`. Pengujian tidak pernah menulis id secara langsung (*hard-code*).

### 4.3 Pola pengujian

Secara umum, pengujian mengikuti pola Arrange-Act-Assert.

Pertama, kita mendefinisikan kelasnya:
```cs
[Collection(DatabaseCollection.Name)]          // 1. Kontainer bersama
public class XxxTests : DatabaseTestBase       // 2. Kelas dasar mengosongkan DB sebelum setiap pengujian
{
    public XxxTests(PostgresFixture db) : base(db) { }

    ...
}
```

Lalu, di dalam kelas, kita mendefinisikan pengujiannya:
```cs
[Fact]
public async Task Describes_the_rule_being_proved_1()
{
    ...
}

[Fact]
public async Task Describes_the_rule_being_proved_2()
{
    ...
}
```

Di dalam setiap pengujian, kita mengikuti pola Arrange-Act-Assert:
```cs
[Fact]
public async Task Describes_the_rule_being_proved()
{
    // Arrange: mulai dari DB kosong, masukkan data dasar, ambil id berdasarkan label
    await using var conn = Db.CreateConnection();
    var ids = await SeedData.InsertBaselineAsync(conn);
    var repo = new OrderRepository(new NpgsqlConnectionFactory(Db.ConnectionString));

    // Act: panggil repository (atau SQL langsung jika skenario membandingkan gaya query)
    var result = await repo.SomethingAsync(ids.Orders["O1"]);

    // Assert: nilai pasti yang dihitung manual dari tabel seed
    Assert.Equal(expected, result.Select(r => (r.Name, r.Value)));
}
```

Pengujian rollback menambahkan satu langkah: ambil *snapshot* (jumlah baris dan stok setiap produk) sebelum pemanggilan yang gagal, lalu pastikan snapshot tersebut **sama persis** sesudahnya.

### 4.4 Kelas pengujian

| File | Topik | Yang diperiksa |
| --- | --- | --- |
| [`SmokeTests.cs`](tests/BeanCart.Tests/SmokeTests.cs) | Setup | Docker, kontainer, dan reset berfungsi |
| [`JoinTests.cs`](tests/BeanCart.Tests/JoinTests.cs) | JOIN | Join empat tabel, `LEFT` vs `INNER JOIN`, filter di `ON` vs `WHERE`, perkalian baris |
| [`AggregationTests.cs`](tests/BeanCart.Tests/AggregationTests.cs) | Agregasi | Pendapatan per pelanggan/kategori, `HAVING`, produk terlaris dengan pemecah seri, `0` bukan `NULL` pada DB kosong |
| [`FilteringTests.cs`](tests/BeanCart.Tests/FilteringTests.cs) | Filtering | Filter opsional, batas rentang tanggal, batas harga/stok, pencarian tanpa membedakan huruf besar-kecil, SQL injection |
| [`TransactionTests.cs`](tests/BeanCart.Tests/TransactionTests.cs) | Transaksi | Buat pesanan menyimpan pesanan, baris, dan stok bersamaan; snapshot harga; batalkan pesanan |
| [`RollbackTests.cs`](tests/BeanCart.Tests/RollbackTests.cs) | Rollback | Baris yang stoknya habis, pelanggaran `CHECK` dan foreign key, transaksi yang tidak di-commit |
| [`IsolationTests.cs`](tests/BeanCart.Tests/IsolationTests.cs) | Isolasi | Setiap pengujian mulai dari kosong, urutan pengujian tidak berpengaruh, baris yang belum di-commit tidak terlihat |

Nilai yang diharapkan untuk setiap pengujian ada di [bagian 6.3](#63-apa-yang-dibuktikan-setiap-pengujian).

## 5. Cara menjalankan

Dari root repositori (folder yang berisi `BeanCart.slnx`):

```bash
dotnet test                                                 # semua pengujian
dotnet test --filter "FullyQualifiedName~RollbackTests"     # satu kelas
dotnet test --logger "console;verbosity=normal"             # tampilkan nama setiap pengujian
dotnet test --logger "console;verbosity=detailed"           # tampilkan nama dan output setiap pengujian
```

Selama pengujian berjalan, `docker ps` menampilkan kontainer `postgres:17-alpine` dan kontainer pembantu `testcontainers/ryuk`. Keduanya dihapus saat eksekusi selesai.

## 6. Hasil yang diharapkan

### 6.1 Ringkasan

Ke-**44 pengujian** lulus dalam sekitar 6 sampai 10 detik setelah image selesai diunduh:

| Smoke | Join | Agregasi | Filtering | Transaksi | Rollback | Isolasi | Total |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 3 | 4 | 8 | 10 | 4 | 6 | 9 | **44** |

### 6.2 Output konsol

`dotnet test --logger "console;verbosity=normal"` menampilkan kira-kira seperti ini (id, waktu, dan urutan akan berbeda):

```text
[testcontainers.org 00:00:00.25] Docker container 6e59f807c579 created
[testcontainers.org 00:00:01.55] Docker container d7723a5c1ecf created
[testcontainers.org 00:00:04.94] Docker container d7723a5c1ecf ready
  Passed BeanCart.Tests.TransactionTests.Place_order_saves_order_lines_and_stock_together [21 ms]
  Passed BeanCart.Tests.RollbackTests.Out_of_stock_line_undoes_the_whole_order(filtersFirst: True) [16 ms]
  Passed BeanCart.Tests.JoinTests.Order_details_join_four_tables [7 ms]
  ...
  Passed BeanCart.Tests.AggregationTests.Total_revenue_on_empty_database_is_zero [1 ms]
[testcontainers.org 00:00:05.83] Delete Docker container d7723a5c1ecf

Test Run Successful.
Total tests: 44
     Passed: 44
 Total time: 6.5222 Seconds
```

Dengan `dotnet test` biasa, Anda hanya melihat ringkasannya:

```text
Test summary: total: 44, failed: 0, succeeded: 44, skipped: 0, duration: 6.7s
```

### 6.3 Apa yang dibuktikan setiap pengujian

Semua nominal dalam IDR dan dimulai dari data seed di [bagian 1](#data-seed), kecuali disebutkan lain. Pendapatan hanya mencakup pesanan `Paid` dan `Shipped`.

**JOIN** (`JoinTests`)

| Id | Skenario | Hasil yang diharapkan |
| --- | --- | --- |
| J1 | Detail O1 | 2 baris: Alice, Arabica ×2 @ 80.000 = 160.000; Alice, Robusta ×1 @ 50.000 = 50.000 |
| J2 | Jumlah pesanan per pelanggan, `LEFT JOIN` vs `INNER JOIN` | LEFT: Alice 2, Budi 2, Citra 1, Dewi 0. INNER: hanya 3 baris, Dewi hilang |
| J3a | Pesanan Paid per pelanggan, filter di `ON` vs di `WHERE` | ON: Alice 1, Budi 1, Citra 0, Dewi 0. WHERE: hanya Alice 1, Budi 1 |
| J3b | Pesanan Alice di-join dengan barisnya | 4 baris, tetapi hanya 2 pesanan berbeda |

**Agregasi** (`AggregationTests`)

| Id | Skenario | Hasil yang diharapkan |
| --- | --- | --- |
| A1 | Pendapatan per pelanggan | Alice 440.000; Budi 190.000; Citra 0; Dewi 0 (nol, bukan null) |
| A1 | Pendapatan di atas 200.000 (`HAVING`) | Hanya Alice 440.000 |
| A2 | Pendapatan per kategori | Beans 480.000; Equipment 150.000; total 630.000 |
| A3 | Top 2 / top 3 berdasarkan unit terjual | Arabica 3, Toraja 2, lalu Robusta 1 (seri dengan V60 dipecah berdasarkan nama) |
| A4 | Pesanan per status | Cancelled 1, Paid 2, Pending 1, Shipped 1 |
| A4 | Rata-rata nilai pesanan yang dihitung sebagai pendapatan | 210.000 (630.000 ÷ 3) |
| A4 | Total pendapatan pada database kosong | 0 |

**Filtering** (`FilteringTests`)

| Id | Skenario | Hasil yang diharapkan |
| --- | --- | --- |
| F1 | Pesanan dengan status Paid | O1, O3 |
| F1 | Tanpa filter, atau filter kosong | Kelima pesanan |
| F2 | 2026-10-10 ≤ tanggal < 2026-11-01 | O2, O3, O4 |
| F2 | Rentang dipisah pada 2026-10-15 (tanggal O2) | Sebelum: O1. Mulai tanggal itu: O2, O3, O4 |
| F2 | Jakarta dan Paid atau Shipped | O1, O2 (O5 milik Citra berstatus Pending) |
| F3 | Beans dengan harga ≥ 60.000 | Arabica, Toraja |
| F3 | Harga 50.000 sampai 95.000 (inklusif) | Arabica, Robusta, Toraja |
| F3 | Stok di bawah 5 | V60 (3), Filters (2); Toraja dengan stok tepat 5 tidak termasuk |
| F4 | Nama mengandung `arabica` | Arabica Gayo 250g |
| F4 | Nama mengandung `'; DROP TABLE products; --` | 0 hasil, tabel products tetap berisi 5 baris |

**Transaksi** (`TransactionTests`)

| Id | Skenario | Hasil yang diharapkan |
| --- | --- | --- |
| T1 | Alice memesan Arabica ×2 dan Toraja ×1 | Pesanan baru berstatus Pending; pesanan 5 → 6; baris 8 → 10; Arabica 10 → 8; Toraja 5 → 4; total 255.000 |
| T1 | Lalu harga Arabica berubah menjadi 90.000 | Pesanan tetap menampilkan 80.000 (snapshot harga) |
| T2 | Batalkan O5 | O5 menjadi Cancelled; Arabica 10 → 11; Filters 2 → 3 |
| T2 | Batalkan O4 (sudah dibatalkan) | Melempar exception; stok Robusta tetap 20 |

**Rollback** (`RollbackTests`)

| Id | Skenario | Hasil yang diharapkan |
| --- | --- | --- |
| R1 | Arabica ×1 + Filters ×3 (stok hanya 2), dengan kedua urutan baris | Melempar "Not enough stock"; tetap 5 pesanan, 8 baris, Arabica 10, Filters 2 |
| R2 | Baris dengan kuantitas 0 | `PostgresException` dengan `order_items_quantity_check`; tidak ada yang tersimpan |
| R2 | Baris dengan id produk yang tidak ada | `PostgresException`, pelanggaran foreign key; tidak ada yang tersimpan |
| R3 | Toraja ×3 dua kali (stok 5) | Yang pertama berhasil (stok 2); yang kedua melempar exception; tetap 6 pesanan, stok 2 |
| R4 | Transaksi di-dispose tanpa `Commit` | Koneksi lain melihat 0 pelanggan |

**Isolasi** (`IsolationTests`)

| Id | Skenario | Hasil yang diharapkan |
| --- | --- | --- |
| D1 | Dua pengujian memasukkan `alice@example.com` | Keduanya lulus; semua tabel kosong di awal masing-masing pengujian |
| D2 | Y (mengharapkan 5 pesanan) berjalan sebelum, di antara, dan sesudah X (menambah 3) | Y selalu melihat 5; X melihat 8 |
| D3 | A melakukan insert di dalam transaksi, B menghitung | B melihat 0 sebelum A selesai; 1 setelah commit, 0 setelah rollback |

### 6.4 Eksperimen opsional: merusak isolasi

Untuk melihat mengapa reset itu penting, ubah `DatabaseTestBase.InitializeAsync` agar melewatinya:

```csharp
public Task InitializeAsync() => Task.CompletedTask; // Db.ResetDatabaseAsync();
```

Jalankan `dotnet test` lagi. Hampir semua pengujian sekarang gagal: hanya `Container_answers_a_query` yang masih lulus, karena tidak menyentuh tabel apa pun. Sebagian besar kegagalan terlihat seperti ini, karena seed kedua memasukkan `alice@example.com` lagi:

```text
Npgsql.PostgresException : 23505: duplicate key value violates unique constraint "customers_email_key"
```

Sisanya gagal karena jumlah baris atau total ikut menghitung data dari pengujian sebelumnya. Kembalikan reset setelah selesai.


## 7. Pemecahan masalah

| Gejala | Kemungkinan penyebab | Solusi |
| --- | --- | --- |
| Semua pengujian gagal saat mulai dengan pesan bahwa Docker tidak berjalan atau tidak ditemukan | Docker belum dijalankan, atau (Linux) user Anda tidak boleh memakai Docker | Jalankan Docker. Di Linux, tambahkan user Anda ke grup `docker`. Jika memakai Podman, atur `DOCKER_HOST` ke socket-nya |
| Eksekusi pertama macet atau timeout | Image masih diunduh | Jalankan `docker pull postgres:17-alpine`, lalu jalankan pengujian lagi |
| `FileNotFoundException` untuk `Sql/schema.sql` | File tidak disalin ke folder output | Periksa item `None Include="Sql\schema.sql"` di `BeanCart.Tests.csproj`; lihat di `bin/Debug/net10.0/Sql` |
| Respawn tidak menghapus apa pun atau tidak menemukan tabel | Respawner dibuat sebelum skema | Buat Respawner setelah menjalankan `schema.sql`, seperti di `PostgresFixture` |
| Properti objek hasil pemetaan kosong atau nol | Kolom `customer_name` vs properti `CustomerName` | Tetap aktifkan `MatchNamesWithUnderscores = true`, atau beri alias kolom di SQL |
| `InvalidCastException` pada count atau sum | `count` mengembalikan `bigint`, `sum` dari `numeric` mengembalikan `numeric` | Gunakan `long` dan `decimal` di record Anda |
| `Cannot write DateTime with Kind=Unspecified ...` | Npgsql hanya menerima nilai UTC untuk `timestamptz` | Gunakan `new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)` |
| `cannot insert a non-DEFAULT value into column "id"` | Id bertipe `GENERATED ALWAYS` | Biarkan database membuat id dan baca kembali dengan `RETURNING id` |
| `ITestCaseOrderer` atau `Xunit.Abstractions` tidak ditemukan | Proyek memakai xUnit v3 | Ganti ke `xunit` 2.9.3 di `BeanCart.Tests.csproj` |
| Pengujian lulus jika dijalankan sendiri, tetapi gagal secara acak saat dijalankan bersama | Ada kelas pengujian tanpa `[Collection(DatabaseCollection.Name)]`, sehingga berjalan paralel di database yang sama | Tambahkan atribut tersebut ke setiap kelas pengujian |
| Data tetap tersimpan walaupun salah satu langkah gagal | Ada perintah yang berjalan di luar transaksi | Kirim `tx` ke setiap pemanggilan Dapper dalam operasi tersebut dan lakukan commit paling akhir |

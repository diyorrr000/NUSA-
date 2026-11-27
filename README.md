# Nusa — Windows uchun jonli notch

Ekraningiz tepasida kichkina qora "orolcha" (iPhone'dagi Dynamic Island kabi). Ustiga sichqonchani olib borsangiz ochiladi, olib ketsangiz yopiladi. O'zimiz uchun noldan yozilgan: **C# / .NET 8 / WPF**, Windows 10 (1809+) va Windows 11, 64-bit.

## Imkoniyatlar

| | |
|---|---|
| 🎵 **Musiqa** | Istalgan pleyer: Spotify, brauzerdagi YouTube, Apple Music, Yandex va h.k. Muqova, ijro/pauza, oldingi/keyingi, vaqt chizig'ini surish. Yopiq notchda muqova va "jonli" ekvalayzer. |
| 🎤 **Sinxron qo'shiq matni** | Ochiq [LRCLIB](https://lrclib.net) bazasidan. Har bir qator aytilayotganda yonadi, qatorni bossangiz o'sha joyga o'tadi. Joriy qator yopiq notchda ham ko'rinadi. |
| ⏱ **Diqqat taymeri (Pomodoro)** | 25/5, 50/10, 15/3 yoki o'zingizniki. Raundlar, uzun tanaffus, notchda qolgan vaqt halqasi, bugungi diqqat statistikasi. |
| ✅ **Vazifalar** | Vazifa yozing va vaqt bering (`14:30`, `1430`, `+15`) — notch o'z vaqtida eslatadi. |
| 💧 **Suv eslatmasi** | Kunlik maqsad, stakan hajmi, faol soatlar, soatlar bo'yicha grafik. |
| 📁 **Javon** | Fayllarni notchga sudrab tashlang, keyin chatga/pochtaga/papkaga sudrab chiqaring. Fayllar nusxalanmaydi. |
| 📋 **Bufer tarixi** | Nusxa olgan matnlaringiz (oxirgi 30 ta). Bosing — qayta nusxalanadi, yoki sudrab tashlang. Faqat xotirada saqlanadi. |
| 🕌 **Namoz vaqtlari** | Internet kerak emas — kompyuterda hisoblanadi. O'zbekiston shaharlari, Hanafiy asr, quyosh yoyi, eslatma va musiqani avtomatik pauza qilish. |
| 📅 **Bugun** | Hafta, batareya, keyingi namoz, suv, diqqat — bir qarashda. |
| 🖥 **Tizim monitori** | Protsessor yuklanishi, xotira (RAM), internet tezligi (↓/↑) — so'nggi 60 soniya grafigi bilan; disklardagi bo'sh joy, kompyuter yoqilganiga qancha bo'lgani. Faqat sahifa ochiq turganda o'lchaydi. |
| ⬇️ **Yuklab olingan fayllar** | Brauzer "Yuklanmalar" papkasiga faylni yuklab bo'lishi bilan notchda nomi va hajmi chiqadi. Bossangiz — javonga qo'shiladi, u yerdan ochasiz yoki chatga sudraysiz. |
| 🔔 **Tizim ko'rsatkichlari** | Ovoz balandligi, Caps Lock, klaviatura tili, zaryadlovchi ulandi/uzildi, batareya kam, internet uzildi. |
| 👀 **Maskot** | Kompyuter jim tursa, soat o'rniga ko'zchalar paydo bo'ladi, ko'z qisadi, uxlab qoladi va qaytganingizda qo'l silkitadi. |

Qo'shimcha: to'liq ekranli o'yin/video paytida yashirinadi, minimal (ingichka chiziq) rejim, sahifalarni yashirish va tartiblash, Windows bilan avtomatik ishga tushish. Tray (soat yonidagi) ikonkasini o'ng tugma bilan bosing — menyu chiqadi.

## Yuklab olish va ishga tushirish

1. GitHub'da **Actions → Build Nusa** bo'limiga kiring, oxirgi muvaffaqiyatli ishga tushirishni oching.
2. Pastdagi **Artifacts** dan `Nusa-win-x64` ni yuklab oling (ichida `Nusa.exe`).
   - `Nusa-win-x64-small` — kichikroq, lekin kompyuterda [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) bo'lishi kerak.
3. `Nusa.exe` ni ishga tushiring. Windows "Windows protected your PC" desa: **More info → Run anyway** (ilova imzolanmagan).

Versiya chiqarish uchun teg qo'ying (`git tag v1.0.0 && git push --tags`) — Actions avtomatik **Release** yaratib, exe'ni biriktiradi.

## O'zingiz yig'ish

Windows'da [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) o'rnating, so'ng:

```powershell
# Ishga tushirish
dotnet run --project src/Nusa

# Bitta .exe fayl yasash
dotnet publish src/Nusa -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Visual Studio 2022 bilan ham ochsa bo'ladi: `src/Nusa/Nusa.csproj`.

## Ma'lumotlar

Hammasi `%AppData%\Nusa\data.json` faylida (sozlamalar, vazifalar, suv jurnali, javon). Akkaunt yo'q, kuzatuv yo'q. Internetga faqat qo'shiq matni uchun (lrclib.net) murojaat qilinadi. Klaviatura kuzatilmaydi — faqat Caps Lock holati va joriy til tekshiriladi.

## Loyiha tuzilishi

```
src/Nusa/
  App.xaml(.cs)            ilova, tray menyusi, uslublar (ranglar, tugmalar)
  NotchWindow.xaml(.cs)    notchning o'zi: holatlar, animatsiya, ogohlantirishlar, maskot
  SettingsWindow.xaml(.cs) sozlamalar oynasi
  Pages/                   notch sahifalari (Musiqa, Taymer, Vazifalar, Suv, Javon, Bufer, Namoz, Bugun, Tizim)
  Services/
    Core.cs                markaziy holat: pomodoro, suv, vazifalar, eslatmalar
    MediaService.cs        Windows media boshqaruvi (GlobalSystemMediaTransportControls)
    LyricsService.cs       LRCLIB'dan sinxron matn
    PrayerTimes.cs         namoz vaqtlarini astronomik hisoblash
    VolumeWatcher.cs       tizim ovozi (CoreAudio)
    SystemMonitor.cs       CPU, RAM, internet tezligi, disklar
    DownloadWatcher.cs     "Yuklanmalar" papkasini kuzatish
    Storage.cs             JSON saqlash
```

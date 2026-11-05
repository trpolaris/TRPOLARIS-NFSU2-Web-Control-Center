<div align="center">

# 🏁 TRPOLARIS NFSU2 Web Control Center

### Windows üzerinde NFSU2 Web sunucusunu kurmak, yönetmek ve güncel tutmak için masaüstü kontrol merkezi

[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![UI](https://img.shields.io/badge/UI-WPF-5C2D91?style=for-the-badge&logo=windows&logoColor=white)](https://learn.microsoft.com/dotnet/desktop/wpf/)
[![Version](https://img.shields.io/badge/Version-1.6.3-2EA44F?style=for-the-badge)](./VERSION.txt)
[![Architecture](https://img.shields.io/badge/Architecture-x64-informational?style=for-the-badge)](#-sistem-gereksinimleri)

**TRPOLARIS NFSU2 Web Control Center**, tarayıcı üzerinden oynanabilen Need for Speed Underground 2 Web sunucusunun Windows tarafındaki kurulum ve yönetim işlemlerini tek bir yerel uygulamada toplar. WSL 2, Ubuntu ve Docker Desktop altyapısını kullanarak kurulum, sunucu işlemleri, dosya yönetimi, erişim profilleri, tanılama ve canlı günlükler için merkezi bir arayüz sunar.

[✨ Özellikler](#-özellikler) · [📦 Kurulum](#-kurulum) · [🛠️ Derleme](#️-kaynak-koddan-derleme) · [🧯 Sorun Giderme](#-sorun-giderme)

</div>

---

## 📚 İçindekiler

- [🎯 Proje Hakkında](#-proje-hakkında)
- [✨ Özellikler](#-özellikler)
- [🧩 Mimari ve Teknolojiler](#-mimari-ve-teknolojiler)
- [💻 Sistem Gereksinimleri](#-sistem-gereksinimleri)
- [📦 Kurulum](#-kurulum)
- [🛠️ Kaynak Koddan Derleme](#️-kaynak-koddan-derleme)
- [🎮 Oyun Arşivi Doğrulama](#-oyun-arşivi-doğrulama)
- [🌐 Erişim Modları](#-erişim-modları)
- [🛡️ Güvenlik ve Veri Koruma](#️-güvenlik-ve-veri-koruma)
- [📁 Proje Yapısı](#-proje-yapısı)
- [🆕 v1.6.3 Sürüm Notları](#-v163-sürüm-notları)
- [🧯 Sorun Giderme](#-sorun-giderme)
- [⚠️ Bilinen Sınırlamalar](#️-bilinen-sınırlamalar)
- [🤝 Katkı ve Hata Bildirimi](#-katkı-ve-hata-bildirimi)
- [📄 Lisans ve Marka Bilgisi](#-lisans-ve-marka-bilgisi)

---

## 🎯 Proje Hakkında

Bu proje, **NFSU2 Web sunucusunun Windows masaüstü yönetim aracıdır**. Oyunun kendisi veya web tabanlı oyun istemcisi değildir. Sunucu kaynak kodu ve web oyun bileşenleri ayrı bir projede bulunur: [trpolaris/NFSU2-Web](https://github.com/trpolaris/NFSU2-Web).

Kontrol Merkezi; Windows arayüzünden PowerShell ve WSL komutlarını çalıştırır, Linux tarafındaki araç zincirini yönetir ve Docker Compose üzerinden NFSU2 sunucu servislerini kontrol eder.

| Bilgi | Değer |
|---|---|
| Uygulama | TRPOLARIS NFSU2 Web Control Center |
| Güncel kaynak sürümü | `1.6.3` |
| Platform | Windows x64 |
| Masaüstü arayüzü | C# / WPF / XAML |
| .NET hedefi | `.NET 10` (`net10.0-windows`) |
| Varsayılan çalışma dizini | `C:\nfsu2` |
| Varsayılan HTTP portu | `9093` |
| Sunucu projesi | [trpolaris/NFSU2-Web](https://github.com/trpolaris/NFSU2-Web) |

---

## ✨ Özellikler

### 📊 Merkezi Kontrol Paneli

- WSL, Ubuntu, Docker Engine ve NFSU2 sunucusunun durumunu tek ekranda görüntüleme.
- Yerel sunucu adresini görme, tarayıcıda açma ve panoya kopyalama.
- LAN adresi ve etkin erişim profilini görüntüleme.
- Kurulum, sunucu yönetimi, dosyalar, tanılama ve günlük ekranlarına hızlı erişim.

### ⚡ Tek Tık Kurulum

Kurulum sihirbazı, gerekli bileşenleri denetleyip kurulum adımlarını sırayla yürütür:

1. Windows, WSL ve oyun klasörü ön kontrolleri.
2. WSL 2 ve Ubuntu dağıtımının denetlenmesi.
3. Docker Desktop ve Ubuntu WSL Integration kontrolü.
4. Linux tarafında Git, Python, Node.js, Rust ve WASI SDK araçlarının hazırlanması.
5. NFSU2 Web kaynak kodunun alınması ve bağımlılıkların kurulması.
6. Oyun varlıklarından gerekli modülün üretilmesi ve WebAssembly derlemesi.
7. Web katmanı ve Rust sunucusu için Docker imajının oluşturulması.
8. Sunucunun başlatılması ve sağlık kontrolünün yapılması.

> ℹ️ İlk Ubuntu açılışında Linux kullanıcı adı ve parola oluşturman gerekebilir. Windows özelliklerinin açılması veya Docker Desktop kurulumu yeniden başlatma isteyebilir.

### 🎮 Oyun Arşivi İçe Aktarma ve Doğrulama

- Yapılandırılmış arşiv bağlantısından ZIP indirme.
- Bilgisayardaki mevcut ZIP arşivini içe aktarma.
- Var olan oyun klasörünü seçme.
- Oyun çalıştırılabilir dosyasının boyut ve SHA-256 kontrolü.
- Gerekli oyun klasörlerinin varlık ve içerik kontrolü.
- Birden fazla olası `SPEED2.EXE` bulunduğunda aday yolları ve reddedilme nedenlerini raporlama.
- Doğrulama başarısızsa hedef oyun klasörünü değiştirmeme.

### 🖥️ Sunucu Yönetimi

- NFSU2 sunucusunu başlatma, durdurma ve yeniden başlatma.
- Docker Compose servis ve konteyner durumunu yenileme.
- Sunucu sağlık kontrolünü görüntüleme.
- Son sunucu günlüklerini inceleme.
- Etkin sunucu adresini tarayıcıda açma.

### 🔄 GitHub Güncelleme ve Geri Alma

- NFSU2 Web projesinin beklenen Git deposunu ve `main` dalını doğrulama.
- Uygun olduğunda fast-forward güncelleme yapma.
- İzlenen yerel değişiklikleri güncelleme sırasında korumaya çalışma.
- WASM ve Docker imajlarını yeniden oluşturma.
- Yeni sunucunun sağlık kontrolünü yapma.
- Güncelleme başarısız olursa önceki imaj ve WASM çıktısına geri dönmeye çalışma.
- Güncelleme raporunu diske yazma.

Güncelleme raporu:

```text
C:\nfsu2\NFSU2-UPDATE-REPORT.txt
```

### 🗂️ Proje Dosya Yöneticisi

- WSL içindeki proje klasörlerini gezme.
- Dosya ve klasörleri yenileme.
- Dosya yükleme ve indirme.
- Yeni klasör oluşturma, yeniden adlandırma ve silme.
- Dosyaları VS Code veya Not Defteri ile açma.
- Projeyi VS Code veya Windows Explorer'da açma.
- İzlenen web kaynak dosyalarında değişiklikleri uygulama/derleme akışını başlatma.

> ⚠️ Kaynak dosyalarını otomatik uygulamadan önce değişikliklerini kaydet ve mümkünse Git commit'i oluştur. Otomatik izleme; `node_modules`, `deps`, `gen`, `out`, `target` ve önbellek gibi derleme klasörlerini kapsam dışında tutar.

### 🌐 Erişim Profilleri

| Profil | Açıklama | Örnek adres |
|---|---|---|
| 🏠 **Local** | Yalnızca aynı bilgisayardan erişim | `http://localhost:9093/` |
| 🖧 **LAN / HTTPS** | Yerel ağdaki telefon ve bilgisayarlardan erişim | `https://LAN-IP:PORT/` |
| 🌍 **Public Tunnel** | Geçici internet paylaşım bağlantısı | `https://....trycloudflare.com` |

- HTTP portunu ve LAN HTTPS portunu yapılandırma.
- LAN IPv4 adresini görüntüleme.
- Erişim profili değişikliğinde önceki yapılandırmayı geri yüklemeye çalışma.
- Uygulamanın yönettiği geçici tüneli başlatma ve durdurma.

### 🩺 Tanılama ve Bakım

- WSL 2, Ubuntu, Docker Engine, port ve sunucu kontrolleri.
- Oyun arşivinin ve oyun klasörünün doğrulanması.
- Sistem durumunu yeniden tarama.
- WSL bağlantısını yeniden başlatma.
- Docker tanılama çıktısını toplama.
- `apt` / `dpkg` etkinliğini kontrol ederek paket yöneticisi onarımı yapma.
- Paket yöneticisi etkin çalışırken onarımı başlatmama.

### 📟 Canlı İşlem Günlüğü

- PowerShell ve WSL çıktısını anlık izleme.
- Kayıtları işlem kategorisine göre filtreleme.
- İşlem aşaması ve ilerleme yüzdesini görüntüleme.
- Günlük dosyasını açma, klasörünü görüntüleme ve dışa aktarma.
- Güvenli durdurma veya hemen iptal etme.

> ⚠️ **Hemen iptal et**, devam eden PowerShell/WSL işlem ağacını sonlandırır ve kısmi değişiklik bırakabilir. Mümkün olduğunda **Güvenli durdur** seçeneğini kullan.

### 🧹 Kaldırma Merkezi

Kaldırma işlemleri bileşen bazında seçilir. Veri kaybı yaratabilecek seçenekler varsayılan olarak işaretli değildir.

| Bileşen | Varsayılan | Etki |
|---|---|---|
| NFSU2 konteynerleri | Seçili | NFSU2 konteynerlerini durdurup kaldırır. |
| NFSU2 sunucu imajları | Seçili | Tanımlı NFSU2 imajlarını kaldırır. |
| NFSU2 tünel süreçleri | Seçili | Uygulamanın yönettiği tüneli kapatır. |
| NFSU2 kayıt hacmi | Seçili değil | Silinirse oyun kayıtları kaybolabilir. |
| `C:\nfsu2` proje kaynakları | Seçili değil | Proje ve yapılandırma dosyalarını kaldırır. |
| `C:\nfsu2\game` oyun dosyaları | Seçili değil | Yerel oyun dosyalarını kaldırır. |
| Ubuntu WSL dağıtımı | Seçili değil | Ubuntu içindeki Linux dosyalarını siler. |
| Docker Desktop | Seçili değil | Diğer Docker projelerini de etkileyebilir. |

Ubuntu dağıtımını veya Docker Desktop'ı kaldırmak ayrıca onay gerektirir. İşaretli bileşenleri silmeden önce mutlaka kontrol et.

---

## 🧩 Mimari ve Teknolojiler

```text
┌─────────────────────────────────────────────────┐
│       TRPOLARIS NFSU2 Web Control Center         │
│                  Windows / WPF                  │
└────────────────────────┬────────────────────────┘
                         │
                  PowerShell / WSL
                         │
             ┌───────────┴───────────┐
             │                       │
       ┌─────▼──────┐         ┌──────▼──────┐
       │ WSL 2      │◄───────►│ Docker      │
       │ Ubuntu     │         │ Desktop     │
       └─────┬──────┘         └──────┬──────┘
             │                       │
       Git / Python              NFSU2 Compose
       Node.js / Rust            Web Container
       WASI SDK / WASM           Game + Module
             │                       │
             └───────────┬───────────┘
                         │
                Local / LAN / Tunnel
                         │
                    Web Browser
```

| Katman | Teknoloji |
|---|---|
| Masaüstü | C#, .NET 10, WPF, XAML |
| Windows otomasyonu | Windows PowerShell 5.1 |
| Linux ortamı | WSL 2, Ubuntu, Bash |
| Konteyner | Docker Desktop, Docker Compose |
| Web arayüzü | React, Node.js, npm |
| Sunucu | Rust, Cargo |
| Oyun modülü | C / WASI SDK, WebAssembly |
| Yardımcı araçlar | Python, Git |

---

## 💻 Sistem Gereksinimleri

### Kontrol Merkezi için

- 64-bit Windows bilgisayar.
- `.NET 10` SDK.
- WPF destekli Windows geliştirme ortamı.
- Windows PowerShell 5.1.
- Kurulum ve sistem yapılandırmaları için yönetici izni.
- İnternet bağlantısı.

### NFSU2 Web sunucusunu kurmak için

- WSL 2 desteği ve etkin sanallaştırma.
- Ubuntu WSL dağıtımı.
- Docker Desktop.
- Docker Desktop üzerinde Ubuntu için etkin WSL Integration.
- Oyun arşivi ve derleme çıktıları için yeterli disk alanı.
- Araç zinciri, kaynak kodu ve bağımlılık indirmeleri için internet erişimi.

> 💡 Kurulum betiği gerekli Ubuntu dağıtımını ve Docker Desktop'ı hazırlamayı dener. Docker Desktop içinde **Settings → Resources → WSL Integration** bölümünden kullandığın Ubuntu dağıtımını etkinleştir.

---

## 📦 Kurulum

### 1. Depoyu klonla

Windows PowerShell aç:

```powershell
git clone https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center.git
cd TRPOLARIS-NFSU2-Web-Control-Center
```

### 2. Uygulamayı yayımla

.NET 10 SDK kurulu olmalıdır. Ayrıntılı komutlar için [Kaynak Koddan Derleme](#️-kaynak-koddan-derleme) bölümünü izle.

### 3. Uygulamayı yönetici olarak başlat

Derleme tamamlandığında:

```text
publish\TRPOLARIS.NFSU2.WebControlCenter.exe
```

Uygulama sistem bileşenlerini yapılandırdığı için Windows UAC yönetici izni isteyebilir.

### 4. Sunucu kurulumunu başlat

1. **Tanılama ve Bakım** ekranında sistem kontrollerini çalıştır.
2. **Tek Tık Kurulum** ekranını aç.
3. Varsayılan HTTP portunu veya kullanacağın boş portu belirle.
4. Oyun klasörünü seç ya da ZIP arşivini içe aktar.
5. **Klasörü doğrula** işlemini çalıştır.
6. Local, LAN/HTTPS veya Public Tunnel erişim profilini seç.
7. **Kurulumu başlat** düğmesine bas.
8. Canlı işlem günlüğünden kurulum aşamalarını takip et.
9. İşlem tamamlandığında kontrol panelindeki sunucu adresini aç.

---

## 🛠️ Kaynak Koddan Derleme

### Gerekli araçlar

- Windows x64.
- .NET 10 SDK.
- WPF masaüstü geliştirme desteği.
- Windows PowerShell 5.1.

> ℹ️ Proje `net10.0-windows` ve WPF hedeflidir. Release derlemesini Windows üzerinde yap. Kaynak ZIP'inde hazır EXE bulunmaz; uygulama bu adımlarla üretilir.

### Önerilen derleme

Proje kökünde PowerShell aç:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Repair-NuGet-RuntimePacks.ps1
.\Publish-SingleFile.ps1
```

`Repair-NuGet-RuntimePacks.ps1`, bozuk veya eksik .NET 10.0.12 Windows runtime pack önbelleğini yeniler, NuGet önbelleğini temizler, restore ve Release build işlemlerini çalıştırır.

`Publish-SingleFile.ps1`, uygulamayı `win-x64`, `Release`, `self-contained` ve `single-file` ayarlarıyla yayımlar.

Başarılı çıktı:

```text
publish\TRPOLARIS.NFSU2.WebControlCenter.exe
```

### Manuel derleme

```powershell
dotnet restore .\src\TRPOLARIS.NFSU2.WebControlCenter\TRPOLARIS.NFSU2.WebControlCenter.csproj -r win-x64

dotnet build .\src\TRPOLARIS.NFSU2.WebControlCenter\TRPOLARIS.NFSU2.WebControlCenter.csproj -c Release -r win-x64

dotnet publish .\src\TRPOLARIS.NFSU2.WebControlCenter\TRPOLARIS.NFSU2.WebControlCenter.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o .\publish
```

---

## 🎮 Oyun Arşivi Doğrulama

v1.6.3, oyun arşivinin neden reddedildiğini daha açık raporlar. Desteklenen sürüm **North America v1.2**'dir.

| Kontrol | Beklenen değer |
|---|---|
| Ana çalıştırılabilir dosya | `SPEED2.EXE` |
| Dosya boyutu | `4,800,512` bayt |
| SHA-256 | `f9dd86c054878ce6276beb07c1fd61874f7a1e4bf1f241b084c65b73e24168a7` |
| Zorunlu klasörler | `CARS`, `FRONTEND`, `GLOBAL`, `TRACKS` |
| Klasör içeriği | Her zorunlu klasör boş olmamalı |

### Arşiv güvenlik sınırları

- En fazla sıkıştırılmış arşiv boyutu: **20 GiB**.
- En fazla açılmış toplam arşiv boyutu: **20 GiB**.
- En fazla ZIP girdisi: **150.000**.
- İndirme zaman aşımı: **3 saat**.
- HTTPS olmayan yönlendirmeler reddedilir.
- Mutlak yollar ve `../` ile arşiv dışına çıkan yollar kabul edilmez.
- Sembolik bağlantılar reddedilir.
- Eksik indirmeler ve geçici `.partial` dosyaları temizlenir.
- Arşiv önce geçici alana açılır; doğrulama başarılı olmadan mevcut oyun klasörü değiştirilmez.
- Yerleştirme öncesi mevcut hedef klasör zaman damgalı yedeğe alınır; işlem başarısız olursa geri yükleme denenir.

Varsayılan oyun dizini:

```text
C:\nfsu2\game
```

> ⚠️ Oyun dosyaları bu GitHub deposuna dahil değildir. Yalnızca kullanma hakkına sahip olduğun oyun kopyasını kullan. Oyun sürümü desteği v1.6.3 ile genişletilmemiştir.

---

## 🌐 Erişim Modları

### 🏠 Local

Aynı Windows bilgisayarında tarayıcıdan erişim:

```text
http://localhost:9093/
```

### 🖧 LAN / HTTPS

Aynı yerel ağdaki telefon, tablet veya başka bilgisayarlardan erişim. Windows Güvenlik Duvarı ve kullanılan HTTPS portu için ağ erişiminin açık olduğundan emin ol.

### 🌍 Public Tunnel

Cloudflare Quick Tunnel aracılığıyla geçici HTTPS adresi oluşturur. Bu adres test ve kısa süreli paylaşım içindir; kalıcı yayın altyapısı veya kimlik doğrulama sistemi değildir.

**Güvenlik uyarısı:** Public Tunnel bağlantısını bilen herkes sunucuya erişebilir. Uygulama bu profile otomatik kullanıcı doğrulaması veya erişim parolası eklemez. Kalıcı yayın için ayrıca erişim kontrolü, güvenlik duvarı ve alan adına bağlı ters proxy yapılandır.

---

## 🛡️ Güvenlik ve Veri Koruma

- Güncelleme ve kaldırma işlemleri NFSU2'ye ait servisleri ve proje kaynaklarını hedefler.
- Sunucu güncellemesinde oyun verisi ve kayıt hacminin korunması amaçlanır.
- Güncelleme akışında `docker compose down -v`, genel Docker volume silme veya `docker volume prune` kullanılmaz.
- Yeni sürüm sağlık kontrolünden geçmeden önceki imaj ve WASM çıktısı için geri dönüş kopyası hazırlanır.
- Git güncellemesi mümkün olduğunda fast-forward yöntemiyle yapılır; zorla reset/rebase kullanılmaz.
- Dosya yöneticisi proje dışına çıkan yolları reddeder.
- Kaldırma merkezinde oyun dosyaları, kayıt hacmi, Ubuntu ve Docker Desktop seçenekleri varsayılan olarak kapalıdır.

### Kullanıcı için güvenlik kontrol listesi

- [ ] Public Tunnel adresini herkese açık veya güvenilmeyen ortamlarda paylaşma.
- [ ] `.env`, sertifika, özel anahtar, erişim bilgisi ve kişisel günlükleri depoya yükleme.
- [ ] Otomatik dosya uygulamasından önce değişikliklerini commit et.
- [ ] Oyun dosyalarını veya kayıt volume'ünü silmeden önce yedek al.
- [ ] Docker Desktop'ı kaldırmanın diğer projeleri de etkileyebileceğini unutma.

---

## 📁 Proje Yapısı

```text
TRPOLARIS-NFSU2-Web-Control-Center/
│
├── src/
│   └── TRPOLARIS.NFSU2.WebControlCenter/
│       ├── Assets/
│       │   └── TRPOLARIS-NFSU2.ico
│       ├── Properties/
│       │   └── PublishProfiles/
│       │       └── WinX64SingleFile.pubxml
│       ├── Resources/
│       │   ├── NFSU2-Windows-OneClick-Installer.ps1
│       │   ├── nfsu2-access.sh
│       │   ├── nfsu2-project-lookup.sh
│       │   └── nfsu2-git-update.sh
│       ├── App.xaml
│       ├── App.xaml.cs
│       ├── MainWindow.xaml
│       ├── MainWindow.xaml.cs
│       ├── app.manifest
│       └── TRPOLARIS.NFSU2.WebControlCenter.csproj
│
├── Repair-NuGet-RuntimePacks.ps1
├── Publish-SingleFile.ps1
├── TRPOLARIS.NFSU2.WebControlCenter.sln
├── VERSION.txt
├── PATCH-NOTES-1.6.3.md
├── .gitignore
└── README.md
```

---

## 🆕 v1.6.3 Sürüm Notları

### 🔎 Daha Açıklayıcı Oyun Arşivi Tanılaması

- ZIP içinde `SPEED2.EXE` yoksa neden doğrudan bildirilir.
- EXE boyutu beklenen `4.800.512` bayt değerinden farklıysa bulunan ve beklenen boyut gösterilir.
- Boyut doğru olsa bile SHA-256 uyuşmazsa gerçek ve beklenen hash gösterilir.
- EXE doğrulansa bile `CARS`, `FRONTEND`, `GLOBAL` veya `TRACKS` klasörlerinden biri eksik/boşsa hangi klasörün sorunlu olduğu belirtilir.
- Birden fazla `SPEED2.EXE` adayı varsa ilk beş adayın göreli yolu ve reddedilme nedeni gösterilir.
- Arşivde hiç `SPEED2.EXE` bulunamazsa dosyanın kurulu oyun klasörü değil, oyun kurulum paketi olabileceği açıklanır.
- Doğrulama başarısız olduğunda hedef oyun klasörü değiştirilmez.

### 🧩 Sürüm Uyumluluğu

v1.6.3 oyun sürümü desteğini genişletmez. WSL tarafındaki WASM üretimi ve kurulum betikleri de aynı North America v1.2 SHA-256 kontrolünü kullandığından yalnızca masaüstü doğrulamasını gevşetmek uyumsuz modül üretilmesine yol açabilir.

Ayrıntılar: [`PATCH-NOTES-1.6.3.md`](./PATCH-NOTES-1.6.3.md)

---

## 🧯 Sorun Giderme

| Sorun | Kontrol / çözüm |
|---|---|
| `dotnet` bulunamıyor | Windows üzerine .NET 10 SDK kur ve yeni PowerShell oturumu aç. |
| WSL komutu çalışmıyor | WSL 2 ve Windows özelliklerini kontrol et; gerekirse yeniden başlat. |
| Ubuntu ilk açılışta bekliyor | Ubuntu terminalinde Linux kullanıcı adı/parola oluşturma adımını tamamla. |
| Ubuntu içinde `docker` bulunamıyor | Docker Desktop'ı aç ve **Settings → Resources → WSL Integration** bölümünde Ubuntu'yu etkinleştir. |
| Docker Engine hazır değil | Docker Desktop'ın tamamen başlamasını bekle. |
| Oyun sürümü doğrulanmıyor | `SPEED2.EXE` boyutunu, SHA-256 değerini ve zorunlu klasörleri kontrol et. |
| Port kullanımda | Kontrol Merkezi'nde boş bir port seç; Windows Firewall ve port kullanımını denetle. |
| Güncelleme sağlık kontrolü başarısız | Canlı işlem günlüklerini ve güncelleme raporunu incele. |
| Public bağlantı açılmıyor | Tünel sürecinin çalıştığını ve erişim profilinin Public olduğunu doğrula. Quick Tunnel adresi geçicidir. |

### Günlük dosyaları

Uygulama günlükleri ve kurulum kayıtları temel olarak şurada tutulur:

```text
C:\nfsu2\logs
```

Ek raporlar:

```text
C:\nfsu2\NFSU2-SETUP-REPORT.txt
C:\nfsu2\NFSU2-UPDATE-REPORT.txt
```

---

## ⚠️ Bilinen Sınırlamalar

- Uygulama Windows x64 ve WPF için geliştirilmiştir; Linux/macOS masaüstü sürümü değildir.
- Sunucu kurulum ve derleme akışı WSL 2, Ubuntu ve Docker Desktop'a bağlıdır.
- Oyun doğrulaması yalnızca tanımlanan North America v1.2 dosya imzasını kabul eder.
- Kaynak arşivinde derlenmiş EXE bulunmaz; Windows üzerinde üretilmelidir.
- Public Tunnel geçicidir ve yerleşik kimlik doğrulaması sağlamaz.
- İlk kurulumda Ubuntu kullanıcı oluşturma, Windows özellik etkinleştirme ve UAC onayı gibi kullanıcı etkileşimleri gerekebilir.

---

## 🤝 Katkı ve Hata Bildirimi

Hata bildirirken şu bilgileri ekle:

1. Kontrol Merkezi sürümü (`1.6.3`).
2. Windows sürümü ve `wsl --version` çıktısı.
3. Docker Desktop sürümü ve Ubuntu dağıtım adı.
4. Hatanın oluştuğu ekran ve işlem aşaması.
5. Hassas bilgiler temizlenmiş ilgili günlük bölümü.

Pull request göndermeden önce kurulum, güncelleme, veri koruma ve geri alma akışlarını kontrol et. `.env`, sertifikaları, erişim bilgilerini, oyun dosyalarını veya kişisel günlükleri commit etme.

---

## 📄 Lisans ve Marka Bilgisi

Bu kaynak paketinde bir `LICENSE` dosyası bulunmamaktadır. Proje sahibi tarafından lisans eklenene kadar kodun kullanım, dağıtım ve değiştirme koşulları açıkça tanımlanmamıştır. Yeniden dağıtım veya türev çalışma yayımlamadan önce proje sahibinden izin al.

**Need for Speed** ve ilgili oyun markaları hak sahiplerine aittir. TRPOLARIS NFSU2 Web Control Center bağımsız bir topluluk aracıdır; oyun hak sahipleriyle resmî bağlantısı veya onayı olduğu iddiasında değildir.

---

<div align="center">

**TRPOLARIS** · NFSU2 Web Control Center · `v1.6.3`

🏁 *Kurulumdan güncellemeye, sunucu yönetimi tek merkezde.*

</div>

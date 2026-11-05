<div align="center">

# 🏁 TRPOLARIS NFSU2 Web Control Center

### Windows üzerinde NFSU2 Web sunucusunu kurmak, yönetmek ve güncel tutmak için masaüstü kontrol merkezi

[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![Framework](https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![UI](https://img.shields.io/badge/UI-WPF-5C2D91?style=for-the-badge&logo=windows&logoColor=white)](https://learn.microsoft.com/dotnet/desktop/wpf/)
[![Version](https://img.shields.io/badge/Version-1.6.2-9ACD32?style=for-the-badge)](./VERSION.txt)

**TRPOLARIS NFSU2 Web Control Center**, tarayıcı üzerinden oynanabilen Need for Speed Underground 2 Web sunucusunun Windows kurulumunu ve günlük yönetimini tek bir yerel uygulamada toplar. WSL 2, Ubuntu ve Docker Desktop altyapısını kullanır; kurulum, sunucu işlemleri, proje dosyaları, erişim profilleri, tanılama ve canlı günlükler için merkezi bir arayüz sağlar.

</div>

---

## 📚 İçindekiler

- [✨ Genel Bakış](#-genel-bakış)
- [🚀 Özellikler](#-özellikler)
- [🧩 Nasıl Çalışır?](#-nasıl-çalışır)
- [💻 Sistem Gereksinimleri](#-sistem-gereksinimleri)
- [📦 Kurulum](#-kurulum)
- [🛠️ Kaynak Koddan Derleme](#️-kaynak-koddan-derleme)
- [🎮 Oyun Dosyası Doğrulaması](#-oyun-dosyası-doğrulaması)
- [🌐 Erişim Modları](#-erişim-modları)
- [🛡️ Güvenlik ve Veri Koruma](#️-güvenlik-ve-veri-koruma)
- [🗂️ Proje Yapısı](#️-proje-yapısı)
- [📋 Sürüm 1.6.2](#-sürüm-162)
- [🧯 Sorun Giderme](#-sorun-giderme)
- [⚠️ Sınırlamalar ve Önemli Notlar](#️-sınırlamalar-ve-önemli-notlar)
- [🤝 Katkı ve Geri Bildirim](#-katkı-ve-geri-bildirim)
- [📄 Lisans](#-lisans)

---

## ✨ Genel Bakış

Bu proje, **NFSU2 Web sunucusunun Windows tarafındaki yönetim aracıdır**. Oyunun kendisi veya web tabanlı oyun istemcisi değildir. Web sunucusu kaynak kodunu, WASM modülünü ve servis bileşenlerini hazırlamak için [trpolaris/NFSU2-Web](https://github.com/trpolaris/NFSU2-Web) deposuyla birlikte çalışır.

Uygulama yerel bir Windows masaüstü arayüzüdür. Arka plandaki Linux tabanlı derleme ve sunucu işlemlerini WSL 2/Ubuntu üzerinden; konteyner yaşam döngüsünü Docker Desktop üzerinden yürütür.

Temel çalışma dizini:

```text
C:\nfsu2
```

Varsayılan HTTP portu:

```text
9093
```

Arayüz, koyu renkli **Night Circuit** tasarım sistemiyle hazırlanmıştır. Durum kartları, işlem aşamaları, ilerleme göstergeleri ve filtrelenebilir terminal görünümü uzun süren işlemlerin izlenmesini kolaylaştırır.

## 🚀 Özellikler

### 📊 Kontrol Paneli

- WSL 2, Ubuntu, Docker Engine ve NFSU2 sunucusu durumunu tek ekranda görüntüleme.
- Aktif yerel sunucu adresini görme, tarayıcıda açma ve panoya kopyalama.
- LAN adresi ve varsa geçici HTTPS tüneli bilgisini görüntüleme.
- Sunucu yönetimi, tek tık kurulum, tanılama, dosya yöneticisi ve günlük ekranlarına hızlı erişim.

### ⚡ Tek Tık Kurulum

Kurulum sihirbazı, NFSU2 Web sunucusunun ihtiyaç duyduğu bileşenleri sırayla hazırlar:

1. Windows ve oyun klasörü ön kontrollerini yapar.
2. Desteklenen Ubuntu dağıtımını WSL üzerinden hazırlar veya eksik Ubuntu dağıtımının kurulumunu başlatır.
3. Docker Desktop'ı denetler; bulunmuyorsa `winget` üzerinden kurmayı dener.
4. Docker Desktop ile Ubuntu arasındaki WSL Integration bağlantısını kontrol eder.
5. Ubuntu tarafında Git, Python, Node.js 24, Rust stable ve WASI SDK 34 gibi proje araçlarını hazırlar.
6. `trpolaris/NFSU2-Web` kaynak kodunu alır ve Python bağımlılıklarını kurar.
7. Oyun verilerinden gerekli kodu üretir ve WebAssembly modülünü derler.
8. React/web katmanını ve Rust sunucusunu Docker imajı içinde oluşturur.
9. NFSU2 sunucusunu başlatır ve sağlık kontrolünü bekler.

> ℹ️ İlk Ubuntu kurulumunda Linux kullanıcı adı ve parola oluşturman istenebilir. Windows özelliklerinin etkinleştirilmesi veya Docker Desktop kurulumu sistem yeniden başlatması gerektirebilir.

### 🎮 Oyun Arşivi İçe Aktarma

Oyun dosyaları için iki yöntem bulunur:

- Yapılandırılmış dosya barındırma bağlantısından ZIP arşivini indirme.
- Bilgisayardaki mevcut ZIP arşivini seçerek içe aktarma.

Ayrıca mevcut bir oyun klasörü seçilebilir. Kurulumdan önce oyun sürümü ve gerekli klasörler doğrulanır. Geçersiz veya desteklenmeyen bir dosya setinde WSL/Docker kurulum aşamasına geçilmez.

### 🖥️ Sunucu Yönetimi

- Yalnızca doğrulanmış NFSU2 Compose projesini başlatma ve durdurma.
- Sunucuyu yeniden başlatma.
- Container, servis, sağlık durumu ve port bilgilerini yenileme.
- Son sunucu günlüklerini görüntüleme.
- Aktif sunucu adresini tarayıcıda açma.
- GitHub `main` dalından güncelleme ve yeniden derleme başlatma.

### 🔄 GitHub Güncelleme ve Yeniden Derleme

Güncelleme süreci yalnızca NFSU2 Web projesine yöneliktir. Genel akış şöyledir:

- Beklenen Git deposu ve `main` dalı doğrulanır.
- Güncelleme mümkün olduğunda fast-forward yöntemiyle uygulanır; zorla reset veya rebase kullanılmaz.
- İzlenen yerel değişiklikler güncelleme sırasında stash/restore akışıyla korunmaya çalışılır.
- Python bağımlılıkları, WASI SDK, WASM modülü ve Docker imajı yenilenir.
- Docker imajı içinde React bağımlılıkları `npm ci` ile; Rust sunucusu kilitli Cargo bağımlılıklarıyla derlenir.
- Yeni sürüm başlatılır ve sağlık kontrolünden geçirilir.
- Yeni sürüm sağlıklı başlamazsa önceki Docker imajı ve WASM modülü geri yüklenmeye çalışılır.

Güncelleme raporu şu konuma yazılır:

```text
C:\nfsu2\NFSU2-UPDATE-REPORT.txt
```

### 🗂️ Proje Dosya Yöneticisi

Kurulu WSL proje klasörü Windows arayüzünden gezilebilir. Dosya yöneticisi şu işlemleri destekler:

- Klasörler arasında gezinme ve içerikleri yenileme.
- Projeyi VS Code veya Windows Explorer ile açma.
- Seçili dosyayı VS Code veya Not Defteri ile açma.
- Dosya indirme/yükleme.
- Yeni klasör oluşturma, yeniden adlandırma ve silme.

`web/` altındaki izlenen frontend/backend kaynak dosyaları kaydedildiğinde otomatik derleme ve sunucuya uygulama seçeneği kullanılabilir. Bekleyen değişiklikler elle de uygulanabilir.

> ⚠️ Otomatik uygulama seçeneğini açmadan önce değişikliklerini kaydet ve mümkünse Git commit'i oluştur. Derleme izleyicisi `node_modules`, `deps`, `gen`, `out`, `target` ve önbellek klasörlerini izlemez.

### 🌐 Erişim ve Paylaşım

Üç erişim profili sunulur:

| Profil | Kullanım | Adres biçimi |
|---|---|---|
| 🏠 **Local** | Yalnızca aynı Windows bilgisayarı | `http://localhost:9093/` |
| 🖧 **LAN / HTTPS** | Aynı yerel ağdaki cihazlar | `https://LAN-IP:PORT/` |
| 🌍 **Public Tunnel** | Geçici internet erişimi | `https://…trycloudflare.com` |

HTTP portu, LAN IPv4 adresi ve LAN HTTPS portu arayüzden ayarlanabilir. Profil değişikliği başarısız olursa önceki Compose ve ortam dosyalarının geri yüklenmesi için işlem akışı bulunur.

**Önemli:** Public Tunnel geçici bir erişim bağlantısıdır. Bağlantıyı bilen herkes sunucuya ulaşabilir; uygulama otomatik kullanıcı doğrulaması veya erişim parolası eklemez. Kalıcı ve herkese açık yayın için ayrıca erişim kontrolü, güvenlik duvarı ve kendi alan adınla yapılandırılmış bir ters proxy kullan.

### 🩺 Tanılama ve Bakım

- WSL 2, Ubuntu, Docker Engine, port, oyun dosyası ve sunucu konteyneri kontrolleri.
- Sistem durumunu yenileme ve WSL bağlantısını yeniden başlatma.
- Docker tanılama çıktısını günlüklerde toplama.
- `apt` / `dpkg` etkinliğini kontrol ederek güvenli paket onarımı yapma.
- Canlı paket yöneticisi işlemi varsa onarımı başlatmama.

Uygulama paket yöneticisi kilit dosyalarını zorla silmez ve WSL/Docker ortamını kendiliğinden sıfırlamaz.

### 📟 Canlı İşlem Günlüğü

- PowerShell ve WSL işlem çıktısını tek ekranda izleme.
- Tüm kayıtlar, WSL/kurulum, uygulama ve hata kategorileri arasında filtreleme.
- Günlük dosyasını açma, günlük klasörünü görüntüleme, yenileme ve dışa aktarma.
- İşlem aşaması, ilerleme yüzdesi ve durum bilgisini takip etme.
- **Güvenli durdurma** ile mevcut kritik adımın tamamlanmasını bekleme.
- **Hemen iptal et** ile çalışan PowerShell/WSL işlem ağacını sonlandırma.

> ⚠️ Hemen iptal et seçeneği, devam eden işlem sırasında kısmi değişikliklere neden olabilir. Mümkün olduğunda Güvenli durdur seçeneğini kullan.

### 🧹 Kaldırma Merkezi

Kaldırma işlemleri seçilebilir bileşenler üzerinden yapılır. Uygulama varsayılan olarak diğer Docker projelerini hedeflemez.

| Bileşen | İlk açılıştaki durum | Etki |
|---|---|---|
| NFSU2 konteynerleri | Seçili | NFSU2'ye ait konteynerleri durdurur ve kaldırır. |
| NFSU2 sunucu imajları | Seçili | Tanımlı NFSU2 imajlarını kaldırır. |
| NFSU2 tünel süreçleri | Seçili | Uygulamanın yönettiği tüneli kapatır ve erişim kaydını temizler. |
| NFSU2 kayıt hacmi | Seçili değil | Silinirse oyun kayıtları kalıcı olarak kaybolur. |
| `C:\nfsu2` proje kaynakları | Seçili değil | Proje kodu, modül ve yapılandırma dosyalarını kaldırır. |
| `C:\nfsu2\game` oyun dosyaları | Seçili değil | Yerel oyun dosyalarını kaldırır. |
| Ubuntu WSL dağıtımı | Seçili değil | Ubuntu içindeki Linux dosyalarını kalıcı olarak siler. |
| Docker Desktop | Seçili değil | Diğer Docker projelerini de etkileyebilir. |

Ubuntu veya Docker Desktop kaldırma seçenekleri ayrıca onay ister. Kaldırmadan önce işaretli seçenekleri ve korunması gereken verileri mutlaka kontrol et.

## 🧩 Nasıl Çalışır?

```text
┌───────────────────────────────────────────────┐
│       TRPOLARIS NFSU2 Web Control Center       │
│                 Windows / WPF                 │
└───────────────────────┬───────────────────────┘
                        │
              PowerShell 5.1 / WSL CLI
                        │
           ┌────────────┴────────────┐
           │                         │
     ┌─────▼──────┐           ┌──────▼──────┐
     │ WSL 2      │           │ Docker      │
     │ Ubuntu     │◄─────────►│ Desktop     │
     └─────┬──────┘           └──────┬──────┘
           │                         │
     Git / Python                NFSU2 Compose
     Node.js / Rust              Web Container
     WASI SDK / WASM              Game + Module
           │                         │
           └────────────┬────────────┘
                        │
                Local / LAN / Tunnel
                        │
                 Web Browser
```

**Teknoloji yığını**

| Katman | Teknoloji |
|---|---|
| Masaüstü uygulaması | C#, .NET 10, WPF, XAML |
| Windows otomasyonu | Windows PowerShell 5.1 |
| Linux çalışma ortamı | WSL 2, Ubuntu, Bash |
| Konteyner yönetimi | Docker Desktop, Docker Compose |
| Web arayüzü derlemesi | React, Node.js 24, npm |
| Sunucu derlemesi | Rust, Cargo |
| Oyun modülü | C / WASI SDK 34, WebAssembly |
| Python araç zinciri | Python 3, virtual environment, pip |

## 💻 Sistem Gereksinimleri

### Uygulamayı çalıştırmak için

- 64-bit Windows bilgisayar.
- WSL 2'yi destekleyen ve sanallaştırması etkin bir sistem.
- Windows PowerShell 5.1.
- Kurulum ve sistem değişiklikleri için yönetici izni.
- İnternet bağlantısı.
- Oyun arşivi ve derleme çıktıları için yeterli boş disk alanı.

### Sunucuyu kurmak ve çalıştırmak için

- WSL 2 ve Ubuntu dağıtımı.
- Docker Desktop ve Ubuntu için etkin WSL Integration.
- Docker Engine'in çalışır durumda olması.
- Kurulum sırasında indirilecek Git, Python, Node.js, Rust, WASI SDK ve proje bağımlılıkları için internet erişimi.
- Desteklenen NFSU2 oyun dosyaları.

> 💡 Kurulum betiği eksik Ubuntu dağıtımını WSL üzerinden kurmayı ve Docker Desktop bulunmadığında `winget` ile yüklemeyi dener. İlk Ubuntu kullanıcı kurulumu kullanıcı etkileşimi gerektirebilir. Docker Desktop içindeki **Settings → Resources → WSL Integration** bölümünde kullanılan Ubuntu dağıtımını etkinleştir.

## 📦 Kurulum

### 1. Kaynak kodunu edin

Yeni kontrol merkezi deposunu klonladıktan sonra proje klasörüne geç:

```powershell
git clone https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center.git
cd TRPOLARIS-NFSU2-Web-Control-Center
```

> Repo adı veya GitHub adresi farklıysa `git clone` satırını kendi depo adresine göre güncelle.

### 2. Uygulamayı derle

Kaynak koddan EXE üretmek için [Kaynak Koddan Derleme](#️-kaynak-koddan-derleme) bölümünü takip et.

### 3. Uygulamayı başlat

Derleme başarılı olduğunda şu dosyayı yönetici olarak çalıştır:

```text
publish\TRPOLARIS.NFSU2.WebControlCenter.exe
```

Uygulama yönetici izni ister. Windows UAC penceresini onayla.

### 4. Tek tık kurulumu başlat

1. **Tanılama ve Bakım** ekranından ilk sistem denetimini çalıştır.
2. **Tek Tık Kurulum** ekranında sunucu HTTP portunu belirle. Varsayılan port `9093` değeridir.
3. Oyun klasörünü seç veya ZIP arşivini indir/içe aktar.
4. **Klasörü doğrula** işlemiyle oyun dosyalarının kontrolünü tamamla.
5. Kurulum sonrası erişim profilini seç: Local, LAN/HTTPS veya Public Tunnel.
6. **Kurulumu başlat** düğmesine bas ve işlem günlüğündeki aşamaları takip et.
7. Kurulum tamamlandığında kontrol panelinden sunucu adresini aç.

## 🛠️ Kaynak Koddan Derleme

### Gerekli araçlar

- Windows x64.
- .NET 10 SDK.
- WPF masaüstü geliştirme bileşenleri bulunan Visual Studio veya eşdeğer .NET SDK kurulumu.
- Windows PowerShell 5.1.

> ℹ️ `net10.0-windows` ve WPF hedefi nedeniyle bu uygulamanın Release derlemesi Windows üzerinde yapılmalıdır. Bu kaynak arşivinde önceden derlenmiş EXE bulunmaz; EXE aşağıdaki adımlarla üretilir.

### Derleme adımları

Proje kökünde Windows PowerShell 5.1 aç:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Repair-NuGet-RuntimePacks.ps1
.\Publish-SingleFile.ps1
```

İlk betik, bozuk veya eksik .NET Windows runtime pack önbelleğini yenileyip projeyi restore/build eder. İkinci betik uygulamayı `Release`, `win-x64`, `self-contained` ve `single-file` ayarlarıyla yayımlar.

Başarılı çıktı:

```text
publish\TRPOLARIS.NFSU2.WebControlCenter.exe
```

### Manuel derleme alternatifi

```powershell
dotnet restore .\src\TRPOLARIS.NFSU2.WebControlCenter\TRPOLARIS.NFSU2.WebControlCenter.csproj -r win-x64
dotnet build .\src\TRPOLARIS.NFSU2.WebControlCenter\TRPOLARIS.NFSU2.WebControlCenter.csproj -c Release -r win-x64
dotnet publish .\src\TRPOLARIS.NFSU2.WebControlCenter\TRPOLARIS.NFSU2.WebControlCenter.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o .\publish
```

## 🎮 Oyun Dosyası Doğrulaması

Uygulama yalnızca tanımlı **North America v1.2** oyun sürümünü kabul eder. Doğrulama sırasında aşağıdaki kontroller uygulanır:

| Kontrol | Beklenen değer |
|---|---|
| Ana çalıştırılabilir dosya | `SPEED2.EXE` |
| Dosya boyutu | `4,800,512` bayt |
| SHA-256 | `f9dd86c054878ce6276beb07c1fd61874f7a1e4bf1f241b084c65b73e24168a7` |
| Gerekli klasörler | `CARS`, `FRONTEND`, `GLOBAL`, `TRACKS` |
| Klasör içeriği | Her gerekli klasör boş olmamalı |

Arşiv içe aktarma güvenlik sınırları:

- İndirilen sıkıştırılmış arşiv için azami boyut: **20 GiB**.
- Açılmış arşiv için azami toplam boyut: **20 GiB**.
- Azami ZIP girdisi: **150.000**.
- İndirme zaman aşımı: **3 saat**.
- HTTPS olmayan yönlendirmeler kabul edilmez.
- Tamamlanmamış indirmeler ve geçici `.partial` dosyaları temizlenir.
- Mutlak yollar, dizin dışına çıkan `../` yolları ve sembolik bağlantılar reddedilir.
- Dosyalar önce geçici alana açılır; doğrulama başarılı olmadan hedef oyun klasörünün üzerine yazılmaz.
- Mevcut hedef klasör değiştirilmeden önce zaman damgalı yedeğe taşınır; yerleştirme doğrulaması başarısız olursa önceki klasör geri yüklenir.

Varsayılan oyun hedefi:

```text
C:\nfsu2\game
```

**Not:** Oyun dosyaları bu GitHub kaynak deposunun parçası değildir. Yalnızca kullanma hakkına sahip olduğun oyun kopyasını kullan. Harici dosya barındırma bağlantısı değişebilir veya kullanılamaz hale gelebilir; bu durumda yerel ZIP içe aktarma seçeneğini kullan.

## 🌐 Erişim Modları

### 🏠 Local

Sunucuya aynı bilgisayardan erişmek için kullanılır:

```text
http://localhost:9093/
```

### 🖧 LAN / HTTPS

Aynı yerel ağdaki telefon, tablet veya diğer bilgisayarlardan HTTPS erişimi sağlar. Windows güvenlik duvarı kuralı istenebilir. Kullanılan LAN IP adresi ile HTTPS portunun istemci cihazlardan erişilebilir olduğundan emin ol.

### 🌍 Public Tunnel

Geçici bir Cloudflare Quick Tunnel adresi oluşturur. Bu yöntem test ve kısa süreli paylaşım içindir; kalıcı yayın altyapısı veya kimlik doğrulama sistemi yerine geçmez. Bağlantı uygulamanın yönettiği tünel süreci çalıştığı sürece geçerlidir.

## 🛡️ Güvenlik ve Veri Koruma

- Kurulum, güncelleme ve kaldırma betikleri NFSU2'ye ait proje/Compose kaynaklarını hedeflemek üzere tasarlanmıştır.
- Sunucu güncellemesinde oyun verisi ve `/state` kayıt hacmi korunur.
- Güncelleme akışında `docker compose down -v`, genel volume silme veya Docker volume prune kullanılmaz.
- Yeni sürüm sağlıklı başlamadan önce mevcut Docker imajı ve WASM modülü için geri dönüş kopyası hazırlanır.
- Git güncellemesi mümkün olduğunda fast-forward ile sınırlandırılır; zorla branch değiştirme veya hard reset yapılmaz.
- Dosya yöneticisinde `.git` gizlenir ve proje dışına çıkan dosya yolları reddedilir.
- Kaldırma ekranında Ubuntu, Docker Desktop, oyun klasörü ve kayıt volume'ü gibi veri kaybına yol açabilecek seçenekler varsayılan olarak kapalıdır.

### Kullanıcının dikkat etmesi gerekenler

- Public Tunnel bağlantısını güvenilir olmayan kişilerle paylaşma; bu profilde otomatik kimlik doğrulaması yoktur.
- `.env`, `.env.local`, sertifika/özel anahtarlar, kişisel erişim bilgileri, oyun dosyaları ve yerel günlükleri GitHub'a yükleme.
- Otomatik dosya uygulamasından önce düzenlemelerini Git ile commit et.
- Kayıt volume'ünü veya oyun klasörünü silmeden önce yedek al.
- Docker Desktop'ı kaldırmanın NFSU2 dışındaki projeleri de etkileyebileceğini unutma.

## 🗂️ Proje Yapısı

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
├── DESIGN-SYSTEM.md
├── PATCH-NOTES-1.6.2.md
├── GAME-ARCHIVE-DOWNLOAD-1.6.1.md
├── UPDATE-PIPELINE-1.6.0.md
├── UPDATE-PIPELINE-1.5.7.md
└── README.md
```

## 📋 Sürüm 1.6.2

### 🐛 Hata Düzeltmeleri

- `FormatBytes(long)` yardımcı metodu eklendi. Böylece oyun arşivi indirme, boş disk alanı kontrolü, ZIP açma özeti ve arşiv boyutu kontrollerinde kullanılan eksik metot hatası giderildi.
- Bu değişiklik, önceki sürüm derlemesinde görülen `CS0103: The name 'FormatBytes' does not exist in the current context` hatalarını giderir.
- Birincil/cyan-yeşil butonların metin kontrastı düzeltildi. Buton metni artık butonun tanımlı foreground rengini devralır.

### 🔒 Korunan İşlevler

- MediaFire arşiv indirme ve yerel ZIP içe aktarma akışı.
- Oyun klasörü/sürümü doğrulaması.
- PowerShell ve WSL ön kontrolleri.
- Docker/WSL kurulum ve sunucu yaşam döngüsü.
- GitHub güncelleme, yedekleme ve geri alma davranışı.

Daha ayrıntılı değişiklikler için [PATCH-NOTES-1.6.2.md](PATCH-NOTES-1.6.2.md) dosyasına bak.

## 🧯 Sorun Giderme

| Belirti | Kontrol / çözüm |
|---|---|
| `dotnet` bulunamadı | Windows üzerinde .NET 10 SDK kur ve yeni PowerShell penceresi aç. |
| WSL komutu bulunamıyor | Windows özelliklerini ve WSL 2 kurulumunu kontrol et; gerekirse yeniden başlat. |
| Ubuntu ilk açılışta bekliyor | Ubuntu terminalinde Linux kullanıcı adı ve parola oluşturmayı tamamla. |
| WSL içinde `docker` bulunamıyor | Docker Desktop'ı aç; **Settings → Resources → WSL Integration** altında kullanılan Ubuntu dağıtımını etkinleştir. |
| Docker Engine hazır değil | Docker Desktop'ın tamamen başlamasını bekle ve Engine durumunu kontrol et. |
| Oyun sürümü doğrulanmıyor | `SPEED2.EXE` boyutunu, SHA-256 değerini ve gerekli oyun klasörlerini kontrol et. Yalnızca belirtilen sürüm desteklenir. |
| Sunucu portu kullanılıyor | Kontrol Merkezi'nde boş bir port seç; Windows güvenlik duvarı ve yerel port kullanımını denetle. |
| Güncelleme sağlık kontrolünü geçemedi | Canlı WSL/uygulama günlüklerini aç; geri yükleme işleminin sonucunu kontrol et. |
| Public bağlantı açılmıyor | Tünel işleminin çalıştığını ve uygulamadaki erişim profilinin Public olduğunu doğrula. Quick Tunnel adresi geçicidir. |

### Günlük dosyaları

Uygulama günlükleri ve kurulum dosyaları temel olarak şu dizinde tutulur:

```text
C:\nfsu2\logs
```

Canlı İşlem Günlüğü ekranından kayıtları kategoriye göre filtreleyebilir, dosyayı açabilir veya dışa aktarabilirsin. Güncelleme raporu `C:\nfsu2\NFSU2-UPDATE-REPORT.txt`, kurulum raporu ise `C:\nfsu2\NFSU2-SETUP-REPORT.txt` konumunda tutulur.

## ⚠️ Sınırlamalar ve Önemli Notlar

- Bu uygulama Windows x64 ve WPF için geliştirilmiştir; Linux veya macOS masaüstü uygulaması değildir.
- Sunucu kurulumu ve derleme işlemleri WSL 2, Ubuntu ve Docker Desktop altyapısına bağlıdır.
- Oyun doğrulaması yalnızca tanımlanan North America v1.2 dosya imzasını kabul eder.
- İnternet bağlantısı; kaynak kod, araç zinciri, bağımlılıklar ve gerektiğinde oyun arşivini indirmek için gereklidir.
- Public Tunnel geçicidir ve yerleşik kimlik doğrulaması sunmaz.
- Kaynak paketinde önceden derlenmiş EXE bulunmaz; Windows üzerinde derlenmelidir.
- Windows Release derlemesi ve gerçek kurulum testi, hedef Windows/WSL/Docker ortamında ayrıca doğrulanmalıdır.

## 🤝 Katkı ve Geri Bildirim

Hata bildirirken mümkünse aşağıdaki bilgileri ekle:

1. Kontrol Merkezi sürümü (`1.6.2`).
2. Windows sürümü ve `wsl --version` çıktısı.
3. Docker Desktop sürümü ve Ubuntu WSL dağıtım adı.
4. Sorunun oluştuğu ekran ve işlem aşaması.
5. İlgili günlükten hassas bilgiler temizlenmiş hata bölümü.

Pull request göndermeden önce değişikliğin kurulum, sunucu güncelleme, veri koruma ve geri alma akışlarını etkileyip etkilemediğini kontrol et. Gizli yapılandırma dosyalarını, sertifikaları, oyun dosyalarını veya kişisel günlükleri commit etme.

## 📄 Lisans

Bu kaynak arşivinde bir `LICENSE` dosyası bulunmuyor. Proje sahibi tarafından bir lisans eklenene kadar kodun kullanım, dağıtım ve değiştirme izinleri açıkça tanımlanmış değildir. Yeniden dağıtım veya türev çalışma yayımlamadan önce proje sahibinden izin al.

**Need for Speed** ve ilgili oyun markaları hak sahiplerine aittir. Bu proje bağımsız bir topluluk aracıdır; oyun hak sahipleriyle resmî bağlantısı veya onayı olduğu iddiasında değildir.

---

<div align="center">

**TRPOLARIS** · NFSU2 Web Control Center · `v1.6.2`

🏁 *Kurulumdan güncellemeye, sunucu yönetimi tek merkezde.*

</div>

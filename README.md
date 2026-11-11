<div align="center">

# 🏁 NFSU2 Server control

### 🌌 Night Circuit · Windows Infrastructure Control

**NFSU2 Web sunucusunu Windows üzerinden kurun, güncelleyin, çalıştırın ve güvenle kaldırın.**

[![Latest Release](https://img.shields.io/github/v/release/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center?display_name=tag&label=Latest%20Release)](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/releases/latest)
[![Release Workflow](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/actions/workflows/release-control-center.yml/badge.svg)](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/actions/workflows/release-control-center.yml)
[![Windows](https://img.shields.io/badge/Windows-x64-1677D2?logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/UI-WPF-6E4AA5?logo=windows&logoColor=white)](https://learn.microsoft.com/dotnet/desktop/wpf/)

[⬇️ Son sürümü indir](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/releases/latest) · [🌐 NFSU2-Web kaynak projesi](https://github.com/trpolaris/NFSU2-Web) · [🛠️ GitHub Actions](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/actions) · [🐞 Hata bildir](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/issues)

</div>

---

## 📖 Proje hakkında

**NFSU2 Server control**, NFSU2 Web sunucusunu Windows bilgisayarda kurmak ve çalıştığı altyapıyı güncel tutmak için geliştirilmiş bir **Windows masaüstü uygulamasıdır**.

Uygulama; Windows tarafındaki işlemleri tek bir arayüzde toplar, Linux tabanlı hazırlık ve derleme görevlerini **WSL 2 / Ubuntu** üzerinde yürütür, sunucuyu ise **Docker Desktop** ile çalıştırır. Böylece WSL, proje araçları, web kaynakları, Rust sunucusu ve WASM modülünü ayrı ayrı elle hazırlamak yerine kurulum ve güncelleme akışını NFSU2 Server control üzerinden yönetebilirsiniz.

> **Kapsamı:** NFSU2 Server control web sitesini veya oyunu yönetmez. Oyuncu, sohbet, yarış ya da oyun içi yönetim konsolu değildir. Görevi NFSU2 Web sunucusunun Windows üzerindeki kurulumunu, altyapı bakımını, servis yaşam döngüsünü ve kaldırılmasını kolaylaştırmaktır. Web uygulaması ve sunucu kaynak kodu ayrı [trpolaris/NFSU2-Web](https://github.com/trpolaris/NFSU2-Web) deposunda geliştirilir.

## ✨ Öne çıkan özellikler

| | Özellik | Ne işe yarar? |
|---|---|---|
| 🚀 | **Tek tık kurulum** | WSL 2, Ubuntu, Docker entegrasyonu ve gerekli proje araçlarını hazırlar. |
| 🎮 | **Oyun arşivi hazırlığı** | ZIP arşivini indirir veya bilgisayardan içe aktarır; kurulum öncesinde doğrular. |
| 📊 | **İndirme takibi** | Yüzde, anlık MB/s ve tahmini kalan süreyi gösterir. |
| ⏯️ | **Duraklat / devam et** | İndirmeyi duraklatır, sürdürür veya iptal eder; kaynak destekliyorsa kaldığı yerden devam eder. |
| 🔄 | **Altyapı güncelleme** | GitHub main kaynaklarını ve WSL araç zincirini yeniler; React, Rust ve WASM bileşenlerini yeniden derler. |
| 🐳 | **Docker servis kontrolü** | NFSU2 servisini başlatır, durdurur ve yeniden başlatır; sağlık durumunu görüntüler. |
| 🛡️ | **İmzalı otomatik güncelleme** | GitHub Releases API, RSA imzalı manifest, SHA-256, SemVer ve devam ettirilebilir indirme kullanır. |
| 🗂️ | **Dosya yöneticisi** | WSL içindeki proje dosyalarını görüntüler, düzenler ve aktarır. |
| 🌐 | **Erişim ve paylaşım** | Local, LAN/HTTPS ve geçici Public Tunnel profillerini yapılandırır. |
| 🖥️ | **Uzak Linux bağlantısı (ilk aşama)** | SSH parola/özel anahtar bağlantısı, host-key doğrulaması, kayıtlı bağlantı profilleri ve uzak sistem/Docker durum sorgusu. |
| 📋 | **Canlı işlem günlükleri** | Aşamaları, ilerlemeyi, komut çıktılarını ve hata kayıtlarını tek ekranda gösterir. |
| 🧹 | **Kontrollü kaldırma** | NFSU2 bileşenlerini seçerek kaldırır; kalıcı verileri ve diğer Docker projelerini varsayılan olarak korur. |

## 🖼️ Ekran görüntüleri

Aşağıdaki ekran turu, uygulamanın açılışından günlük ekranına kadar temel bölümlerini sırayla gösterir. Tekil görselleri büyütmek için ilgili ekran görüntüsüne tıklayın.

<p align="center">
  <img src="docs/screenshots/control-center-tour.gif" alt="NFSU2 Server control arayüz ekran turu" width="900">
</p>


## 🖥️ Uzak Linux sunucu modu

Uzak sunucu modu, Windows uygulamasından SSH ile Ubuntu/Debian sunucusuna doğrudan bağlantı kurmak için eklenmiştir. İlk bağlantı aşamasında kayıtlı sunucu profilleri, parola veya SSH özel anahtarı ile kimlik doğrulama, kullanıcı onaylı SHA-256 host-key sabitleme ve uzak makinenin temel sistem/Docker bilgilerini sorgulama desteklenir.

Parolalar ve özel anahtar parolaları bağlantı profillerine yazılmaz. İlk bağlantıda gösterilen host-key parmak izi, güvenilir başka bir kanaldan doğrulanmadan kabul edilmemelidir. Uzak kurulum, servis yönetimi ve SFTP dosya yöneticisi bu bağlantı katmanının sonraki geliştirme aşamalarıdır.

## ⚙️ Nasıl çalışır?

NFSU2 Server control, Windows arayüzünden aşağıdaki zinciri yönetir:

**Windows / WPF → WSL 2 / Ubuntu → GitHub kaynakları ve proje araçları → React + Rust + WASM derlemesi → Docker Compose → NFSU2 Web sunucusu**

### 🔄 Altyapı güncelleme akışı

1. GitHub main dalındaki güncel kaynak kodu alınır.
2. Ubuntu paketleri ve proje araç zinciri kontrol edilir; gerekli bağımlılıklar güncellenir.
3. WASM modülü hazırlanır; React ve Rust bileşenleri kilitli bağımlılık dosyalarıyla derlenir.
4. Yeni Docker imajı oluşturulur ve NFSU2 servisi yeniden başlatılır.
5. Sağlık kontrolü yapılır. Yeni sürüm sağlıklı başlamazsa önceki imaj ve WASM çıktısına geri dönüş denenir.

### 🛡️ Veri güvenliği

- Güncelleme sırasında oyun klasörü ve kalıcı Docker volume verileri normal akışta silinmez.
- Docker volume'lerini silen bir Compose işlemi normal güncelleme adımının parçası değildir.
- Yalnızca NFSU2 Server control tarafından kurulan/doğrulanan NFSU2 Compose servisi hedeflenir; diğer Docker projeleri hedeflenmez.
- Kaldırma ekranındaki Ubuntu WSL, Docker Desktop, oyun dosyaları ve kalıcı kayıtları silme seçenekleri varsayılan olarak kapalıdır.
- Public Tunnel geçici bir erişim adresidir; bağlantıyı bilen herkese açık olabilir ve kendi başına kullanıcı doğrulaması sağlamaz.

## 🧱 Teknoloji altyapısı

| Katman | Teknolojiler |
|---|---|
| Windows uygulaması | C#, .NET 10, WPF, XAML |
| Windows otomasyonu | PowerShell 5.1 |
| Linux ortamı | WSL 2, Ubuntu, Bash |
| Konteyner | Docker Desktop, Docker Compose |
| Web arayüzü | React, Node.js 24, npm |
| Sunucu | Rust, Cargo |
| Oyun modülü | WASI SDK, WebAssembly |
| Yardımcı araçlar | Git, Python |

## 💻 Sistem gereksinimleri

**NFSU2 Server control uygulaması**
- 64-bit Windows 10/11.
- Uygulama kurulumu ve sistem bileşenleri için gerektiğinde yönetici izni.
- İndirmeler ve kaynak güncellemeleri için internet bağlantısı.

**NFSU2 Web sunucu altyapısı**
- WSL 2 ve donanım sanallaştırması desteği.
- Ubuntu WSL dağıtımı.
- Docker Desktop ve Ubuntu için WSL Integration.
- Oyun arşivi, kaynak kodu, WASM çıktıları ve Docker imajları için yeterli disk alanı.

> Yayımlanan uygulama self-contained tek EXE olarak hazırlanır; NFSU2 Server control'ü açmak için ayrıca .NET Desktop Runtime yüklemek gerekmez. WSL, Ubuntu ve Docker ise sunucu altyapısının gereksinimleridir.

## 📥 Kurulum

1. [GitHub Releases](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/releases/latest) sayfasından **NFSU2 Server control.exe** dosyasını indirin.
2. EXE'yi çalıştırın. Windows yönetici izni isterse onaylayın.
3. **Sunucu Kurulumu** ekranında oyun arşivini indirin veya mevcut ZIP dosyanızı içe aktarın.
4. Oyun klasörünü doğrulayın ve **Kurulumu başlat** seçeneğini kullanın.
5. İşlemleri **İşlem Günlükleri** ekranından takip edin.

## 🔔 Uygulama güncellemesi

NFSU2 Server control, GitHub Releases API üzerinden yayınları denetler. Sürüm bilgisi bir `version.txt` dosyasından değil Release etiketinden (`tag_name`) alınır.

- **SemVer:** Kararlı ve önizleme sürümleri doğru sıralanır.
- **Stable / Beta:** Güncelleme tercihleri penceresinden kanal seçilebilir.
- **ETag:** GitHub aynı sürümü döndürdüğünde HTTP 304 ile gereksiz veri aktarımı azaltılır.
- **İmzalı manifest:** `update-manifest.json`, gömülü RSA açık anahtarıyla RSA-SHA256 üzerinden doğrulanır.
- **Dosya bütünlüğü:** EXE indirme sonrası SHA-256 ve dosya boyutuyla doğrulanır.
- **Devam ettirilebilir indirme:** Bağlantı kesilirse kısmi dosya bir sonraki denemede Range isteğiyle sürdürülür.
- **Yerel C# güncelleme yardımcısı:** EXE değiştirme işlemi harici PowerShell updater olmadan yapılır.
- **Yedeksiz değiştirme:** Güncelleme sırasında `.previous` veya başka bir uygulama yedeği oluşturulmaz. İndirme ve doğrulama tamamlanmadan mevcut EXE'ye dokunulmaz.
- **Rollback politikası:** Yedek oluşturulmaması tercih edildiğinden, yeni EXE dosyası yerleştirildikten sonra açılış doğrulaması başarısız olursa otomatik eski sürüme dönüş yapılamaz; sonuç güncelleme geçmişine ve günlüğe yazılır.
- **Başlangıç doğrulaması ve geçmiş:** Yeni uygulama açılış sinyali verir; güncelleme sonuçları yerel geçmişte tutulur.
- **Minimum sürüm politikası:** Release manifesti, desteklenen en düşük istemci sürümünü belirtebilir.

**Eski sürümlerden v1.8.1'e geçiş:** Eski updater'lar uygulama yedeği oluşturabildiğinden, v1.8.1'e ilk geçişi elle yapın. Release sayfasından `NFSU2 Server control.exe` dosyasını indirin; uygulama klasöründe yeni dosyayı mevcut EXE'nin eski adıyla (`TRPOLARIS.ControlCenter.exe` veya `TRPOLARIS.NFSU2.WebControlCenter.exe`) yerleştirip eski dosyanın üzerine yazın ve çalıştırın. v1.8.1 bu eski dosya adlarını tanır ve ilk açılışta kendi adını `NFSU2 Server control.exe` olarak değiştirir. Hiçbir `.previous` veya `.bak` uygulama yedeği oluşturulmaz. Sonraki uygulama güncellemeleri yerel C# motoruyla yapılır.

Uygulamanın kendi güncellemesi ile NFSU2 Web sunucusunun **Altyapı güncelleme** işlemi birbirinden ayrıdır:
- **Uygulama güncellemesi:** Windows'taki NFSU2 Server control EXE dosyasını yeniler.
- **Altyapı güncellemesi:** WSL içindeki web kaynaklarını, derleme araçlarını ve Docker sunucu imajını yeniler.

## 🆕 Sürüm notları

### v1.8.1 · NFSU2 Server control adı ve temalı arayüz
- Uygulama adı, Windows ürün bilgisi ve yayımlanan dosya adı `NFSU2 Server control` / `NFSU2 Server control.exe` olarak değiştirildi.
- v1.8.0 ve daha eski EXE dosya adları ilk açılışta yedeksiz olarak yeni ada taşınır.
- Güncelleme tercihleri ekranındaki ComboBox açılır listeleri ve geçmiş ListBox alanı koyu tema ile yeniden tasarlandı.
- Uyarı, onay ve hata pencereleri Windows MessageBox yerine uygulamanın kendi karanlık tema diyaloglarını kullanır.

### v1.8.0 · İmzalı güncelleme motoru ve yeniden adlandırma
- GitHub Release varlıkları RSA-SHA256 imzalı manifest ile doğrulanır; EXE SHA-256 ve boyut kontrolünden geçer.
- GitHub Releases API, SemVer ve ETag tabanlı kararlı/beta sürüm denetimi eklendi.
- Kesilen EXE indirmelerini Range istekleriyle devam ettirme ve güncelleme geçmişi eklendi.
- Güncelleme kanalını ve otomatik kontrol sıklığını uygulama içinden seçme eklendi.
- PowerShell güncelleme yardımcısı kaldırıldı; dosya değiştirme C# yerel yardımcı süreçle yapılır.
- Uygulama güncellemesi sırasında `.previous` veya başka bir EXE yedeği oluşturulmaz.
- v1.8.0 ara sürümünün ürün/EXE adı `TRPOLARIS Control Center` ve `TRPOLARIS.ControlCenter.exe` idi; v1.8.1 ile ürün/EXE adı `NFSU2 Server control` olarak değiştirildi.
- v1.7.3'ten v1.8.0'a tek seferlik manuel dosya adı geçişi sağlandı; eski PowerShell updater'ın yedek oluşturan akışı kullanılmaz.

### v1.7.3 · İlk kurulum ve WSL/Docker kararlılığı
- 🐳 İlk Ubuntu kurulumunda Docker Desktop WSL Integration hazır olana kadar güvenilir yeniden deneme.
- 🛠️ Windows PowerShell 5.1'in yönlendirilmiş Docker hata çıktısını bekleme döngüsünü durduran hata olarak değerlendirmesi düzeltildi.
- 🐧 Farklı Windows sürücüleri ve Linux kullanıcı adları için dinamik WSL proje yolu/kullanıcı keşfi iyileştirildi.
- 📁 Dosya yöneticisi, dosya sahipliği ve altyapı güncelleme yolları sağlamlaştırıldı.
- 🔄 Ana pencere başlığı, üst çubuk ve splash screen sürüm bilgileri ortak AssemblyVersion sağlayıcısından otomatik alınır; eski sürüm numarasının ekranda kalması önlenir.

### v1.7.2 · Arayüz, indirme ve kullanım iyileştirmeleri
- 🎨 Güncelleme denetimi penceresi Night Circuit tasarımına taşındı; güncel sürüm bilgisi sadeleştirildi.
- 🟢 Buton yazıları için kontrast ve metin gölgesi iyileştirildi; bilgi ipuçları koyu ve okunaklı hale getirildi.
- 🖱️ Açılış Splash Screen penceresi fareyle taşınabilir.
- 📥 Oyun arşivi için **Oyunu indir** tarayıcı düğmesi eklendi ve MediaFire indirme adresi yenilendi.
- 🧹 Kullanılmayan alanlar ve derleme uyarısı oluşturan kod temizlendi.

### v1.7.1 · Arayüz ve açılış iyileştirmeleri
- 🎯 Üst çubuk ikonları ve daha kompakt durum eylemleri.
- 💡 Kurulum ekranında soru işaretli bilgi ipuçları ve sadeleştirilmiş yerleşim.
- 🧭 Dosya Yöneticisi ile Erişim ve Paylaşım ekranları korundu.
- ⏳ Taşmaları giderilmiş splash ekranı ve güncelleme kontrolü sırasında yüzde ilerlemesi.

### v1.7.0 · Night Circuit
- 🌌 Koyu grafit yüzeyler, neon vurgular ve yenilenmiş WPF bileşenleri.
- ✨ Yeni splash screen ve açılış deneyimi.
- 🔔 GitHub Release kontrolü ve SHA-256 doğrulamalı uygulama güncelleme akışı.

### v1.6.6 · İndirme ve WSL/Docker
- 📥 Oyun arşivi indirme, içe aktarma ve otomatik yerleştirme.
- 📊 İndirme yüzdesi, anlık hız ve tahmini kalan süre.
- ⏯️ Duraklatma, devam ettirme ve iptal; destekleyen kaynaklarda kaldığı yerden indirme.
- 🐧 Docker Desktop – Ubuntu WSL 2 entegrasyon kontrolü ve hazırlığı.

## 🛠️ Kaynak koddan derleme

    git clone https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center.git
    Set-Location TRPOLARIS-NFSU2-Web-Control-Center

    dotnet restore .\src\TRPOLARIS.NFSU2.WebControlCenter\TRPOLARIS.NFSU2.WebControlCenter.csproj -r win-x64

    dotnet publish .\src\TRPOLARIS.NFSU2.WebControlCenter\TRPOLARIS.NFSU2.WebControlCenter.csproj -c Release -r win-x64 --self-contained true -o .\publish

Tek dosyalı yayın ayarları doğrudan `TRPOLARIS.NFSU2.WebControlCenter.csproj` içinde tanımlıdır. Ayrı bir yayın PowerShell betiği gerekmez. WPF arayüzü, uygulama kodu ve kurulum/güncelleme otomasyon kaynakları tek Windows x64 EXE dosyasına gömülür.

Kaynak koddan derlemek için Windows üzerinde .NET 10 SDK ve WPF geliştirme araçları gereklidir. Çıktı `publish\NFSU2 Server control.exe` konumuna yazılır.

## 🔗 Bağlantılar

- [⬇️ Son sürümü indir](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/releases/latest)
- [🛠️ Release derleme akışı](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/actions/workflows/release-control-center.yml)
- [🌐 NFSU2-Web kaynak projesi](https://github.com/trpolaris/NFSU2-Web)
- [🐞 Hata bildir / öneri gönder](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/issues)

---

<div align="center">

**NFSU2 Server control** · **v1.8.1**

*Install · Update · Maintain · Remove*

</div>

<div align="center">

# 🏁 TRPOLARIS NFSU2 Web Control Center

### 🌌 Night Circuit · Windows Infrastructure Control

**NFSU2 Web sunucusunu Windows üzerinden kurun, altyapısını güncel tutun ve gerektiğinde güvenle kaldırın.**

[![Latest Release](https://img.shields.io/github/v/release/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center?display_name=tag&label=Latest%20Release)](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/releases/latest)
[![Release Workflow](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/actions/workflows/release-control-center.yml/badge.svg)](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/actions/workflows/release-control-center.yml)
[![Platform](https://img.shields.io/badge/Windows-x64-1677D2?logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![UI](https://img.shields.io/badge/UI-WPF-6E4AA5?logo=windows&logoColor=white)](https://learn.microsoft.com/dotnet/desktop/wpf/)

[⬇️ Son sürüm](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/releases/latest) · [🌐 NFSU2-Web](https://github.com/trpolaris/NFSU2-Web) · [🛠️ Actions](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/actions)

</div>

---

## ✨ Genel bakış

**TRPOLARIS Control Center**, Windows üzerinde çalışan WPF masaüstü aracıdır. WSL 2, Ubuntu ve Docker Desktop altyapısını hazırlar; NFSU2 Web sunucusunun kurulumu, güncellenmesi, servis yaşam döngüsü ve kaldırılmasını tek merkezden yürütür.

> Control Center, web sitesini veya oyunu yönetmez. Oyuncu, sohbet, yarış ya da oyun içi yönetim ekranları içermez. Web uygulaması ve Rust sunucusu ayrı [NFSU2-Web](https://github.com/trpolaris/NFSU2-Web) deposunda geliştirilir.

## 🖼️ Ekran görüntüleri

*Aşağıdaki slayt gösterisi uygulamanın ana ekranlarını sırayla gösterir. Tekil ekran görüntülerünü açmak için galeri bağlantılarını kullanın.*

<p align="center">
  <img src="docs/screenshots/control-center-tour.gif" alt="TRPOLARIS Control Center v1.7.1 ekran turu" width="900">
</p>

| Ekran | Açıklama |
|---|---|
| [🚀 Başlangıç ekranı](docs/screenshots/00-startup-splash.png) | Açılış, GitHub sürüm kontrolü ve yüzde ilerlemesi. |
| [📊 Genel bakış](docs/screenshots/01-dashboard.png) | WSL, Ubuntu, Docker ve NFSU2 servis durumu. |
| [📥 Sunucu kurulumu](docs/screenshots/02-server-setup.png) | Oyun arşivi indirme/içe aktarma, doğrulama ve kurulum. |
| [🔄 Altyapı güncelleme](docs/screenshots/03-infrastructure-update.png) | GitHub kaynakları, araç zinciri, WASM ve Docker güncellemesi. |
| [🗂️ Dosya yöneticisi](docs/screenshots/04-file-manager.png) | WSL proje dosyalarını görüntüleme ve düzenleme. |
| [🌐 Erişim ve paylaşım](docs/screenshots/05-access-sharing.png) | Local, LAN/HTTPS ve Public Tunnel profilleri. |
| [🧹 Kaldırma merkezi](docs/screenshots/06-uninstall-center.png) | NFSU2 bileşenlerini kontrollü kaldırma. |
| [📋 İşlem günlükleri](docs/screenshots/07-operation-logs.png) | Canlı çıktı, aşamalar, ilerleme ve hata kayıtları. |

## ⚡ Öne çıkan özellikler

| | Özellik | Kısa açıklama |
|---|---|---|
| 🚀 | **Tek tık kurulum** | WSL 2, Ubuntu, Docker ve proje araç zincirini hazırlar. |
| 📦 | **Oyun arşivi indirme** | İlerleme, MB/s, kalan süre, duraklat/devam ve iptal. |
| ↪️ | **Devam ettirilebilir indirme** | Kaynak HTTP Range destekliyorsa kaldığı yerden sürdürür. |
| 🔄 | **Altyapı güncelleme** | GitHub `main`, Python, Node.js, Rust, WASI, React, WASM ve Docker imajını yeniler. |
| 🐳 | **Docker servis kontrolü** | NFSU2 servisini başlatır, durdurur ve yeniden başlatır. |
| 🛡️ | **Güvenli uygulama güncellemesi** | Açılışta Release kontrolü; EXE indirme, SHA-256 doğrulama ve yedekli değiştirme. |
| 🗂️ | **Dosya yöneticisi** | WSL proje dosyalarını gezme, düzenleme ve aktarma. |
| 🌐 | **Erişim profilleri** | Local, LAN/HTTPS ve geçici Public Tunnel. |
| 📋 | **Canlı günlükler** | İşlem aşamaları, yüzde ilerlemesi, filtreleme ve hata çıktıları. |
| 🧹 | **Kontrollü kaldırma** | Proje bileşenlerini kaldırır; kalıcı verileri varsayılan olarak korur. |

## 🔄 Güncel sürüm · v1.7.1

- **v1.7.1** — Üst menü ikonları, kompakt eylemler, bilgi ipuçları, iyileştirilmiş splash ve korunan Dosya Yöneticisi / Erişim ekranları.
- **v1.7.0** — Night Circuit arayüz yenilemesi, yeni splash ve GitHub tabanlı otomatik uygulama güncellemesi.
- **v1.6.6** — İndirme hızı ve kalan süre, duraklat/devam, kaldığı yerden indirme ve Docker–WSL entegrasyon iyileştirmeleri.

## 🧱 Teknoloji altyapısı

`C#` · `.NET 10` · `WPF / XAML` · `Windows PowerShell 5.1` · `WSL 2 / Ubuntu` · `Docker Desktop` · `Node.js 24` · `React` · `Rust / Cargo` · `WASI SDK` · `WebAssembly` · `Git` · `Python`

## 📥 Kurulum

1. [Son kararlı Release](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/releases/latest) sayfasından `TRPOLARIS.NFSU2.WebControlCenter.exe` dosyasını indirin.
2. EXE'yi çalıştırın ve istenen Windows yönetici iznini onaylayın.
3. **Sunucu Kurulumu** ekranından oyun arşivini seçip doğrulayın.
4. Kurulumu başlatın; ilerlemeyi **İşlem Günlükleri** ekranından izleyin.

**Gereksinimler:** 64-bit Windows, WSL 2, Ubuntu WSL dağıtımı, Docker Desktop ve internet bağlantısı. Uygulama EXE'si self-contained yayımlanır; ayrıca .NET Desktop Runtime kurulumu gerekmez.

## 🛡️ Güvenlik ve veri koruma

- Oyun arşivi doğrulanmadan mevcut oyun klasörü değiştirilmez.
- Sunucu güncellemesi öncesi mevcut Docker imajı ve WASM modülü geri dönüş için saklanır.
- Normal güncelleme akışı Docker volume'lerini veya oyun kayıtlarını silmez.
- Kaldırma ekranında Ubuntu, Docker Desktop, oyun dosyaları ve kalıcı kayıtların silinmesi varsayılan olarak seçili değildir.
- Public Tunnel geçicidir ve tek başına kullanıcı doğrulaması sağlamaz.

## 🔗 Bağlantılar

- [📦 Releases](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/releases)
- [🛠️ Release workflow](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/actions/workflows/release-control-center.yml)
- [🐞 Hata bildirimi](https://github.com/trpolaris/TRPOLARIS-NFSU2-Web-Control-Center/issues)
- [🌐 NFSU2 Web kaynak projesi](https://github.com/trpolaris/NFSU2-Web)

---

<div align="center">

**TRPOLARIS** · NFSU2 Web Control Center · **v1.7.1**

*Install · Update · Maintain · Remove*

</div>

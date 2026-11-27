# UnityRTS: kurulum, sahne ve test rehberi

Unity **6000.1.10f1** (Unity 6, 3D, built-in render pipeline, C#). Proje: `UnityRTS/`.

## Kod yapısı

| Klasör | Assembly | İçerik |
|---|---|---|
| `Assets/Scripts/Core/` | `Rts.Core` (UnityEngine **yok**, `noEngineReferences`) | Oyun kuralları ve otoriter simülasyon: `GameData`, `PlayerState`, `MatchSimulation`, `CombatRules`, `VisibilityService`/`VisibleWorld`, `ZoneMap`/`Formation`, `AiSafeSnapshot`, `ActionPlanParser`/`MiniJson`, `GameActionValidator`, `ApprovedPlanExecutor`, `PlanReview`, `MapSetup` |
| `Assets/Scripts/Game/` | `Rts.Game` | Unity katmanı: `GameBootstrap`, `SimulationDriver`, `EntityViewManager`, `SelectionController`, `RtsCameraController`, `RtsHud`, `AiPlanPanel`, `DebugPanel`, `RtsInput`, `GameContext` |
| `Assets/Scripts/Editor/` | `Rts.Editor` | `Rts` menüsü: demo sahnesi oluşturma, örnek snapshot dışa aktarma |
| `Assets/Tests/EditMode/` | `Rts.Tests.EditMode` | 77 test: kurallar, savaş, fog of war, snapshot, parser, validator, onay akışı |
| `Assets/Tests/PlayMode/` | `Rts.Tests.PlayMode` | 4 test: bootstrap, fog görünümü, simülasyon ilerlemesi, AI paneli |

Kural: oyun mantığı `Rts.Core` içinde kalır. Unity katmanı yalnızca görüntüler ve girdiyi komuta çevirir.
Hem oyuncu arayüzü hem onaylanmış AI planı aynı `MatchSimulation.Try*/Order*` yöntemlerini çağırır.

## Sahne kurulumu

**Hazır sahne:** `Assets/Scenes/RtsDemo.unity` (Build Settings'te). Açıp Play'e basmak yeterli.

Elle kurmak istersen:

1. Menü: **Rts > Create Demo Scene** (sahneyi yeniden oluşturur), ya da
2. Boş bir sahne aç ve içine boş bir GameObject koy, adı `GameBootstrap`, `GameBootstrap` bileşenini ekle.

`GameBootstrap` geri kalan her şeyi çalışma anında kurar (zemin, ışık, kamera, simülasyon, görünümler, HUD, kontrolcüler,
AI paneli, debug paneli). Elle bağlanması gereken başka obje veya referans yoktur.

| Sahnede | Not |
|---|---|
| `Main Camera` | İsteğe bağlı. `GameBootstrap.MainCamera` boşsa `Camera.main` kullanılır, yoksa oluşturulur |
| `GameBootstrap` (+ `GameBootstrap` bileşeni) | Tek zorunlu obje. `LocalPlayer` alanı 1 veya 2 |
| Directional Light | İsteğe bağlı, yoksa bootstrap ekler |

Çalışma anında `GameBootstrap` altında oluşan bileşenler: `RtsInput`, `SimulationDriver`, `RtsCameraController`,
`SelectionController`, `RtsHud`, `AiPlanPanel`, `DebugPanel` ve `Entities` (`EntityViewManager`). Girdi IMGUI olay sistemi üzerinden
okunur, bu yüzden eski Input Manager ve yeni Input System ayarlarının ikisinde de çalışır.

## Oynanış / kontroller

| Girdi | Etki |
|---|---|
| Sol tık / sürükle | Seçim (Shift: ekle). Yalnızca kendi birim ve binaların |
| Sağ tık | Hareket (grup formasyon), **görünür** düşmana saldırı, Worker seçiliyken kaynağa toplama |
| Citadel / Barracks / ArcheryYard / Stable seçili | Alttaki panelde Train (Shift: x5) ve Research düğmeleri |
| Worker seçili | Build House/Barracks/ArcheryYard/Stable ve Gather Wood/Food/Gold düğmeleri |
| WASD / oklar, tekerlek | Kamera, zoom (S durdurma komutudur; geri kaydırmak için Aşağı ok veya Ctrl+S) |
| Esc | Seçimi temizle |
| Tab | Kontrol edilen oyuncuyu değiştir (aynı pencerede iki oyunculu hotseat test görünümü) |
| F1 | Debug paneli (düşman spawn, kaynak, çağ atlama, hız) |
| F2 | AI Commander paneli |
| F9 | AI snapshot'ını `logs/ai_snapshot.json` dosyasına yaz |
| H | Yardımı aç/kapat |

Kazanma: rakibin Citadel'ini yok et (Citadel yalnızca Ram'lerle verimli yıkılır, Age II gerekir).

## Doğrulama (otomatik)

Unity yolu: `C:\Program Files\Unity\Hub\Editor\6000.1.10f1\Editor\Unity.exe`. Proje Unity ile açıkken aynı projeyi batch modda çalıştırma.

```powershell
$u = "C:\Program Files\Unity\Hub\Editor\6000.1.10f1\Editor\Unity.exe"
$p = "C:\...\Game-Project\UnityRTS"
& $u -batchmode -nographics -projectPath $p -runTests -testPlatform EditMode -testResults editmode.xml -logFile editmode.log
& $u -batchmode -nographics -projectPath $p -runTests -testPlatform PlayMode -testResults playmode.xml -logFile playmode.log
```

Editor içinde: **Window > General > Test Runner**.

`GameDataTests`, `rag/knowledge/*.json` dosyalarını okuyup `GameData` ile karşılaştırır (maliyet, HP, hız, nüfus, süre, üretim yeri).
Bilgi tabanı değişirse bu testler hangi değerin ayrıştığını gösterir.

## AI akışı ile uçtan uca deneme

1. Play, **F2 > Export snapshot** (veya F9) ile `logs/ai_snapshot.json` oluşur. (Oynamadan örnek: menü **Rts > Export Sample AI Snapshot**, `logs/ai_snapshot_sample.json`.)
2. Terminal: `python -m agent.cli "I am being attacked by cavalry from the north. Can I defend?" --snapshot logs/ai_snapshot.json --plan-out logs/ai_plan.json`
3. Unity: **F2 > Load plan file**. Panel eylemleri tek tek doğrular (OK/X ve sebep), toplam maliyeti gösterir.
4. **REJECT**: hiçbir oyun durumu değişmez. **APPROVE**: `GameActionValidator` güncel duruma göre yeniden doğrular, yalnızca geçen eylemler `MatchSimulation` komutlarına dönüşür.
5. Debug için F2'de "Paste valid sample" ve "Paste invalid sample" düğmeleri var. Geçersiz plan şema/parser aşamasında reddedilir ve oyuna hiç ulaşmaz.

Fog of War denemesi: F1 > "Enemy: 4 Raiders at my NorthGate". Düşman, birimlerin veya binaların görüş alanına girene kadar çizilmez, saldırı komutuna hedef olamaz
ve snapshot'ta görünmez. Birliğini düşmanın yanına götürünce `visible_enemies` alanında `Raider / 4 / NorthGate` olarak belirir.

## Photon Fusion 2 (Host Mode) ile bağlama: henüz yapılmadı

`MatchSimulation` ağdan bağımsız, tek süreçli otoriter durum makinesidir. Fusion eklendiğinde:

- Fusion SDK'sını Package Manager / Photon sitesinden içe aktar, App ID'yi Photon ayarlarına gir (repoya yazma).
- Host, `MatchSimulation.Tick` ve tüm `Try*/Order*` çağrılarını çalıştırır. İstemciler bu komutları RPC/input olarak host'a yollar, host oyuncu kimliğini bağlantıdan alır.
- Host durumu istemcilere replike eder (Fusion `NetworkObject`/`[Networked]`), `EntityViewManager` replike durumu okur.
- `VisibilityService.FilterFor(playerId)` host tarafında her oyuncu için çalışır. Prototipte host oyuncusunun tüm durumu teknik olarak görebildiği raporda belirtilmelidir.
- AI akışı değişmez: onaylanan plan host'a komut olarak gider ve `GameActionValidator` host'ta çalışır.

## Bilinen sınırlar

- Ağ (Photon) yok. İki oyuncu Tab ile aynı pencerede oynanır.
- Görseller ilkel şekillerdir. Fog of war yalnızca düşman görünürlüğünü ve komutları kontrol eder, haritada gölge (shroud) çizilmez.
- Birimler çarpışmaz (üst üste binebilir), yol bulma yoktur (düz çizgi hareket).
- Ayrı bir rakip yapay zekâsı yoktur (Debug paneli veya Tab ile test edilir).
- Bina yerleşimi otomatiktir (Citadel çevresinde boş nokta). Serbest yerleştirme yoktur.
- Oyun içi elle test yapılmadı: doğrulama 77 EditMode + 4 PlayMode testi ve batch modda sahne oluşturmadır.

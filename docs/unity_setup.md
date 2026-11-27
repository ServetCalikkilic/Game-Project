# UnityRTS: kurulum, sahne ve test rehberi

Unity **6000.1.10f1** (Unity 6, 3D, built-in render pipeline, C#). Proje: `UnityRTS/`.

## Kod yapısı

| Klasör | Assembly | İçerik |
|---|---|---|
| `Assets/Scripts/Core/` | `Rts.Core` (UnityEngine **yok**, `noEngineReferences`) | Oyun kuralları ve otoriter simülasyon: `GameData`, `PlayerState`, `MatchSimulation`, `CombatRules`, `VisibilityService`/`VisibleWorld`, `ZoneMap`/`Formation`, `AiSafeSnapshot`, `ActionPlanParser`/`MiniJson`, `GameActionValidator`, `ApprovedPlanExecutor`, `PlanReview`, `MapSetup` |
| `Assets/Scripts/Game/` | `Rts.Game` | Unity katmanı: `GameBootstrap`, `SimulationDriver`, `EntityViewManager`, `SelectionController`, `RtsCameraController`, `RtsHud`, `AiPlanPanel`, `DebugPanel`, `RtsInput`, `GameContext` |
| `Assets/Scripts/Editor/` | `Rts.Editor` | `Rts` menüsü: demo sahnesi oluşturma, örnek snapshot dışa aktarma |
| `Assets/Scripts/Core/Net/` | `Rts.Core` | Ağ protokolü (Fusion'dan bağımsız): komut/durum kodlayıcıları, `HostAuthority`, `HostSession`, `ClientSession`, `LoopbackNetwork` |
| `Assets/Scripts/Net/` | `Rts.Net.Fusion` (`RTS_FUSION` ile) | Fusion 2 adaptörü: `RtsNetObject`, `FusionLauncher`, editör araçları |
| `Assets/Tests/EditMode/` | `Rts.Tests.EditMode` | 109 test: kurallar, savaş, fog of war, snapshot, parser, validator, onay akışı, ağ protokolü (host yetkisi, replikasyon, komut kapıları) |
| `Assets/Tests/PlayMode/` | `Rts.Tests.PlayMode` | 5 test: bootstrap, fog görünümü, simülasyon ilerlemesi, AI paneli, Host rolünde istemciye replikasyon |

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

## Çok oyunculu: Photon Fusion 2.x, Host Mode

### Mimari

```
 Client (P2)                                   Host (P1, State Authority)
 UI --ICommandSink--> ClientSession            UI --ICommandSink--> LocalCommandSink --+
        |  NetCommand bytes (kimlik alanı yok)                                         v
        +-------- RPC_Command(bytes) ---------> HostSession.OnCommand(senderFromConnection)
                                                  -> HostAuthority (decode + hız sınırı)
                                                  -> MatchSimulation.Try*/Order*   <== tek gerçek simülasyon
                                                  -> PlanReview/GameActionValidator (AI planları)
 ClientSession.Replica  <-- RPC_ToPlayer(target, chunk) -- HostSession.Replicate (5 Hz, oyuncuya özel)
 (Tick etmez, komut kabul etmez)                          StateCodec.Capture = fog-filtreli durum
```

| Parça | Dosya | Not |
|---|---|---|
| Protokol (Fusion'dan bağımsız) | `Core/Net/*.cs` | `NetCommand`/`CommandCodec`, `StateCodec`, `Chunker`, `HostAuthority`, `HostSession`, `ClientSession`, `LoopbackNetwork` |
| Replica | `MatchSimulation.IsReplica` | `ApplyReplicatedState`; replica `Tick` etmez, `Try*/Order*` reddeder |
| Komut yolu (UI) | `Game/CommandSinks.cs` | `LocalCommandSink` (Local/Host), `ClientCommandSink` (Client). UI simülasyona doğrudan dokunmaz |
| Roller | `GameBootstrap.StartLocal / StartHost / StartClient` | `AutoStart=false` ile ağ başlatıcı çağırır |
| Fusion adaptörü | `Net/RtsNetObject.cs`, `Net/FusionLauncher.cs` | Yalnızca `RTS_FUSION` tanımlıyken derlenir |
| Editör araçları | `Editor/FusionDefineSymbol.cs`, `Net/Editor/FusionSceneTools.cs` | Tanımı otomatik ekler, prefab ve sahne oluşturur |

Yetki ve güvenlik kuralları:

- Gerçek `MatchSimulation` yalnızca host'ta çalışır. İstemci replica'sı değişirse host etkilenmez.
- Komut mesajında oyuncu kimliği yoktur. Host kimliği bağlantıdan (`RpcInfo.Source` → oyuncu 2) alır; bilinmeyen gönderen sessizce yok sayılır.
- Her komut bozuk veriye karşı savunmacı çözülür (uzunluk, enum, koordinat, id sayısı), oyuncu başına saniyede 60 komutla sınırlıdır ve reddedilen komutlar sebebiyle `Authority.Log`'a yazılır.
- İstemci komutları host'ta aynı yetkili metotlardan geçer: sahiplik, maliyet, nüfus, ön koşul ve **hedef görünürlüğü** (gizli düşmana saldırı reddedilir). Citadel'e 60 birimden uzak inşaat da reddedilir.
- Onaylanan AI planı: istemci APPROVE'a basınca plan JSON'u host'a gider. Host `PlanReview` + `GameActionValidator` ile **kendi durumuna göre yeniden doğrular** ve yalnızca geçen eylemleri uygular. Onay düğmesi bir arayüz kapısıdır, otorite host'taki doğrulayıcıdır.
- Fog of war ağda da korunur: host her oyuncuya yalnızca kendi ekonomisini, kendi varlıklarını, görünen düşman varlıklarını (tür, konum, can) ve bildiği kaynak düğümlerini yollar. Rakibin ekonomisi, görünmeyen birimleri ve emirleri ağa hiç çıkmaz. AI snapshot'ı da host'ta üretilir (`RequestSnapshot`).
- Durum 5 Hz tam anlık görüntü olarak gider (yeni oyuncuya hemen), 400 baytlık parçalara bölünür. İstemci görünümü hareketi yumuşatır.

### Photon Fusion 2 SDK kurulumu

SDK repoya konmaz (lisans; `.gitignore`'da `Assets/Photon/`). Her geliştirici kendi kopyasına alır.

1. Photon hesabı aç, [Photon Dashboard](https://dashboard.photonengine.com)'da bir **Fusion** uygulaması oluştur ve **App Id**'yi kopyala.
2. [SDK indirme sayfasından](https://doc.photonengine.com/fusion/current/getting-started/sdk-download) Fusion 2 SDK'sını (`.unitypackage`) indir. Sayfa Unity 6.0 ve 6.3'ü destek listesinde gösterir. Fusion 2.1 Photon Realtime 5 ister ve 2.0 ile uyumlu değildir, ikisini karıştırma.
3. Unity'de **Assets > Import Package > Custom Package...** ile içe aktar. Project Settings > Editor > Asset Serialization **Force Text** olmalı (bu projede öyle).
4. Fusion'ın **Realtime Settings** ayarına **App Id**'yi yaz. Bu ayar dosyası `.gitignore`'dadır, repoya yazma.
5. Derleme bitince konsolda `[Rts] Updated scripting define RTS_FUSION...` yazar. Menü **Rts > Network > Fusion SDK Status** durumu gösterir. `Rts.Net.Fusion` ve `Rts.Net.Fusion.Editor` asmdef'leri o andan itibaren derlenir.
6. **Derleme hatası alırsan** (örneğin `NetworkRunner` bulunamadı): `Assets/Scripts/Net/Rts.Net.Fusion.asmdef` ve `Assets/Scripts/Net/Editor/Rts.Net.Fusion.Editor.asmdef` içindeki `references` listesine SDK sürümünün asmdef adlarını ekle (genellikle `Fusion.Unity`; `Fusion.Runtime` bir asmdef ise onu da). Fusion DLL'leri normalde otomatik referanstır.
7. **Rts > Network > Create Net Prefab and Scene** menüsünü çalıştır. Bu `Assets/Prefabs/RtsNetObject.prefab` (kökte `NetworkObject` + `RtsNetObject`) ve `Assets/Scenes/RtsNetwork.unity` üretir, prefab tablosunu yeniden kurmayı dener. Tablo kurulmadıysa **Tools > Fusion > Rebuild Prefab Table**.

### Sahne ve prefab ayarları

| Obje | Bileşenler / alanlar |
|---|---|
| Sahnede `GameBootstrap` | `GameBootstrap` (`AutoStart` **kapalı**) ve `FusionLauncher` (`Bootstrap` = aynı obje, `NetObjectPrefab` = `RtsNetObject` prefabı, `SessionName`) |
| Prefab `RtsNetObject` | Kökte `NetworkObject` ve `RtsNetObject` (başka bileşen gerekmez). Host çalışma anında `Runner.Spawn` ile üretir |
| `Main Camera`, ışık | Sahnede olabilir; olmazsa bootstrap kurar |
| Build Settings | `RtsNetwork` ve `RtsDemo` sahneleri |

`NetworkRunner` çalışma anında `FusionLauncher` tarafından eklenir. Fusion Input kullanılmaz (`ProvideInput=false`), komutlar RPC ile gider.

### İki istemciyle test (Unity Editor)

Seçenek A: **Editör + Windows build**
1. File > Build Profiles: Windows, sahne `RtsNetwork`; Player Settings > Resolution: **Run In Background** açık. Build al ve çalıştır.
2. Editör'de `RtsNetwork` sahnesini aç, Play. **Host a match** (oturum adı örn. `rts-room`). HUD'da `Host P1 - remote players: ...` yazar.
3. Build'de aynı oturum adıyla **Join a match**. Client HUD'ı `Client P2 - host state #N` gösterir.

Seçenek B: **Multiplayer Play Mode** (`com.unity.multiplayer.playmode` paketi): Window > Multiplayer > Multiplayer Play Mode, bir sanal oyuncu etkinleştir. Ana editör Host, sanal oyuncu Join.

Kontrol listesi:
1. Host: Worker'ları seç ve Gather. Client'ta **rakip tabanı sis içinde görünmez**, yalnızca kendi tarafı vardır.
2. Client: Citadel seç, Train Worker. Host'ta kuyruk ve nüfus değişir; client HUD'ı ~0.2 sn içinde güncellenir.
3. Client: Worker seçip Build Barracks, tamamlanınca Train Spearman. Yetersiz kaynakta host'tan "insufficient resources" bildirimi gelir.
4. Host: F1 > "Enemy: 4 Raiders at my NorthGate". Client'ta (kendisi P2) F1 paneli kapalıdır. Düşman birim, tarafın görüş alanına girene kadar çizilmez; görünce saldırı komutu verilebilir.
5. Gizli bir hedefe ya da rakibin birimine komut verme denemesi: host reddeder, client'ta bildirim görünür.
6. Kazanma: bir tarafın Ram'i rakip Citadel'ini yıkınca iki ekranda da VICTORY/DEFEAT görünür ve komutlar kabul edilmez.
7. AI akışı: client'ta F2 > Export snapshot (host üretir) > `python -m agent.cli ... --snapshot logs/ai_snapshot.json --plan-out logs/ai_plan.json` > Load plan file > APPROVE: sonuç "AI plan: N executed, M rejected" bildirimi olarak gelir. REJECT host'a hiçbir şey göndermez.

### SDK olmadan çalışan testler

Protokolün tamamı Fusion'sız doğrulanır (aynı kodlama, parçalama ve host yetki yolu): `LoopbackNetwork` host ve istemciyi bellekte bağlar. Örnekler: istemci komutunun host'ta uygulanıp geri replike edilmesi, kimlik taklidi, gizli hedefe saldırı, çok uzak inşaat, bozuk ve rastgele paketler, hız sınırı, fog filtreli replikasyon, kazanma durumunun istemciye gitmesi.
Testler `Assets/Tests/EditMode/NetworkTests.cs` dosyasındadır ve Unity Test Runner'da çalışır.

### Doğrulanmamış kısım (önemli)

`RtsNetObject.cs` ve `FusionLauncher.cs` bu geliştirme ortamında **gerçek Fusion SDK'sı ile derlenmedi ve çalıştırılmadı** (Photon indirme sayfası otomatik isteği güvenlik servisi ile engelledi). Kod, Fusion 2'nin bilinen API'lerine göre yazıldı; yalnızca elle yazılmış bir stub ile derleme biçimi denendi. İlk kurulumda şunları kontrol et:

- `[Rpc(RpcSources.All, RpcTargets.StateAuthority)]` ve `byte[]` parametre. Weaver dizi parametresini kabul etmezse `RPC_Command` ve `RPC_ToPlayer` imzalarını (`NetworkArray`, `[RpcSize]` veya reliable data) uyarla. Yalnızca bu iki metot ve `Chunker.DefaultMaxChunk` değişir.
- `[RpcTarget] PlayerRef` ile tek oyuncuya gönderim ve `RpcInfo.Source`.
- `Runner.ActivePlayers`, `Runner.Disconnect`, `StartGameArgs.PlayerCount`, `Runner.Spawn(prefab)`. `NetworkSceneManagerDefault` reflection ile aranır, bulunamazsa atlanır.
- RPC boyutu sınırı: parça boyutu 400 bayttır (`Chunker.DefaultMaxChunk`). Fusion'ın sınırı farklıysa değiştir.
- Asmdef referansları (yukarıda madde 6).

## Bilinen sınırlar

- Ağ: en fazla 2 oyuncu (host P1, istemci P2); yeniden bağlanma ve host migration yok. İstemcide tahmin (prediction) yok, komut sonucu ~0.2 sn gecikmeyle görünür. Offline modda iki oyuncu Tab ile aynı pencerede oynanır.
- Görseller ilkel şekillerdir. Fog of war yalnızca düşman görünürlüğünü ve komutları kontrol eder, haritada gölge (shroud) çizilmez.
- Birimler çarpışmaz (üst üste binebilir), yol bulma yoktur (düz çizgi hareket).
- Ayrı bir rakip yapay zekâsı yoktur (Debug paneli veya Tab ile test edilir).
- Bina yerleşimi otomatiktir (Citadel çevresinde boş nokta). Serbest yerleştirme yoktur.
- Oyun içi elle test yapılmadı; ağ adaptörü gerçek Fusion ile denenmedi (yukarı bak). Doğrulama EditMode/PlayMode testleri ve batch modda sahne oluşturmadır.

# Sistem Mimarisi

Bu belge oyun tasarımını ve AI mimarisini özetler. Sayısal değerlerin kaynağı `rag/knowledge/` altındaki dosyalardır; burada çelişki varsa o dosyalar geçerlidir.

## 1. Genel Akış

```
Oyuncu
  → Unity 6 RTS istemcisi/host'u
  → VisibilityService.FilterFor(session) → AiSafeSnapshot
  → AI Gateway (oturum + snapshot deposu)
  → LLM Ajanı  ⇄  MCP Client ⇄  Özel RTS MCP Sunucusu (Python)
                                  ├ get_player_resources
                                  ├ get_army_status
                                  ├ get_visible_enemies
                                  ├ get_technology_status
                                  ├ get_recent_events
                                  ├ estimate_plan_cost
                                  └ search_game_knowledge (RAG)
  → ActionPlan JSON → şema doğrulaması
  → Unity Onay Paneli (Approve / Reject / Ask Why)
  → GameActionValidator (güncel yetkili durumla yeniden kontrol)
  → Photon Fusion komutu (yetkili yol)
```

1. **Unity 6 + Photon Fusion 2.x:** RTS mekanikleri, Fog of War ve durum senkronizasyonu. Oyuncu stratejik yardım istediğinde yalnızca güvenli, görünürlük filtresinden geçmiş `AiSafeSnapshot` gönderir.
2. **Custom MCP Server (Python):** Canlı güvenli durumu ve RAG bilgisini okuyan araçlar sunar.
3. **LLM Agent & RAG:** Soruya göre gerekli araçları dinamik seçer, bilgi tabanından kaynak ID'leriyle bilgi çeker ve `ActionPlan` üretir.
4. **Onay & Doğrulama:** Plan oyuncuya gösterilir; onaylanırsa `GameActionValidator` yeniden kontrol eder, geçerliyse yetkili komut çalışır.

LLM bir danışmandır, oyun otoritesi değildir.

## 2. Ağ Tercihi: Photon Fusion 2.x, Host Mode

- **Neden Fusion:** Ders için önerilen seçenek; tick tabanlı durum senkronizasyonu ve state authority modeli RTS'e uygun. PUN 2 maintenance/LTS durumunda olduğu için seçilmedi.
- **Neden Host Mode:** İki oyunculu prototipte en basit ve yetkili model. Host, state authority olarak birim, kaynak, savaş sonuçları ve maç durumunu tutar.
- **Komut modeli:** İstemci yalnızca komut/RPC gönderir (ör. `RequestTrainUnit`). Host kaynak, bina sahipliği, ön koşul ve nüfus limitini doğrular, sonra durumu değiştirir. Oyuncu komutları ve onaylanmış AI planları aynı yolu kullanır.
- **Senkronize edilen durum:** oyuncu kimliği, birim sahipliği, spawn/despawn, hareket/komut durumu, kaynak etkili eylemler, savaş sonuçları, bina durumu, çağ/teknoloji, maç durumu.
- **Sınırlama:** Host oyuncusu teknik olarak tüm durumu görebilir. Bu bir prototip varsayımıdır ve raporda belirtilir. AI snapshot'ı yine de her oyuncu için görünürlük filtresinden geçer, host için bile.

## 3. Oyun Tasarımı

### Kaynaklar
Wood, Food, Gold. Workers kaynağı toplayıp Citadel'e bırakır. Nüfus: Citadel 10, House +5; sert limit Age I = 40, Age II = 60.

### Binalar (`bld_*`)
| Bina | Maliyet | HP | Rol |
|---|---|---|---|
| Citadel | başlangıç binası | 2000 | Ana bina, kaynak bırakma, Worker üretir, çağ ve ekonomi araştırması. Yıkılırsa kaybedilir |
| House | 30 wood | 300 | +5 nüfus |
| Barracks | 150 wood | 800 | Spearman, Guardian, Ram üretir; Age II melee teknolojileri |
| ArcheryYard | 175 wood | 700 | Archer üretir; Longbow Training |
| Stable | 175 wood, 50 gold | 700 | Raider üretir |

### Birimler (`unit_*`)
| Birim | Maliyet | HP | Hasar | Hız | Üretim | Çağ |
|---|---|---|---|---|---|---|
| Worker | 50 food | 40 | 3 | 3.0 | Citadel | I |
| Spearman | 50 food, 25 wood | 70 | 8 (+14 Raider) | 3.2 | Barracks | I |
| Archer | 45 wood, 25 gold | 45 | 7 pierce, menzil 7 | 3.4 | ArcheryYard | I |
| Raider | 70 food, 40 gold | 100 | 10 (+6 Archer, +4 Worker) | 5.2 | Stable | I |
| Guardian | 60 food, 45 gold | 140 | 12 | 2.6 | Barracks | II |
| Ram | 140 wood, 60 gold | 200 | 3 (+42 bina) | 2.2 | Barracks | II |

Counter döngüsü: **Spearman > Raider > Archer > Spearman**. Guardian, Spearman ve Archer'a karşı güçlüdür; Raider ve Ram'e karşı zayıftır. Ram oklara neredeyse bağışıktır ama her yakın dövüş birimine karşı zayıftır.

### Teknolojiler (`tech_*`)
| Teknoloji | Yer | Maliyet | Etki |
|---|---|---|---|
| Iron Age | Citadel | 300 food, 150 gold | Age II; Guardian, Ram ve Age II teknolojilerini açar; nüfus limiti 60 |
| Sharpened Tools | Citadel | 100 wood, 75 food | Wood/food toplama +%20 |
| Longbow Training | ArcheryYard | 175 wood, 75 gold | Archer +1 menzil, +2 hasar |
| Forged Blades | Barracks | 150 food, 100 gold | Yakın dövüş hasarı +%20 (Age II) |
| Reinforced Plating | Barracks | 100 food, 120 gold | +1 pierce zırh (Age II) |

### Kazanma / Kaybetme
Rakibin Citadel'i yok edilen oyuncu kaybeder. Yalnızca Ram binalara karşı verimlidir, bu yüzden Citadel'e saldırı için Age II gerekir.

## 4. Anlamsal Durum (`AiSafeSnapshot`)

Oyuncunun görebileceği bilgilerle sınırlı kompakt DTO:

| Alan | İçerik |
|---|---|
| `phase` | EarlyEconomy, BuildUp, UnderAttack, Skirmish, LateGame (oyun kuralıyla Unity'de hesaplanır) |
| `resources` | wood, food, gold |
| `population` | kullanılan / limit |
| `ownArmy` | tür bazında sayı ve kaba bölge |
| `ownBuildings` | tür, sayı, durum |
| `technology` | mevcut çağ, araştırılmış ve araştırılmakta olanlar |
| `recentEvents` | son ~30 sn'nin güvenli olayları (ör. "NorthGate saldırı altında", "2 Worker kaybedildi") |
| `visibleEnemies` | yalnızca görünür düşmanlar: tür, sayı, kaba bölge |
| `capturedAt` | zaman damgası (bayatlık kontrolü için) |

Bölgeler: BaseCenter, NorthGate, SouthGate, EastGate, WestGate, FarmArea, Forest, GoldMine. Snapshot'ta ham koordinat, rakip ekonomisi veya keşfedilmemiş alan bilgisi yoktur.

## 5. Fog of War ve Güvenlik

1. **Modelden önce filtrele:** `VisibilityService.FilterFor(session)` gizli düşman birimlerini, keşfedilmemiş alanı ve rakip ekonomisini snapshot'tan kod seviyesinde çıkarır. Prompt'ta "gizli bilgiyi yok say" demek yeterli kabul edilmez.
2. **Kimlik oturumdan gelir:** Oyuncu kimliği ve `match_id` Unity/AI Gateway oturumundan bağlanır. MCP araçları `player_id` veya `match_id` argümanı almaz; model kimlik değiştiremez. `get_enemy_state(player_id=...)` ve `get_everything()` gibi araçlar yoktur.
3. **Araç yetkileri:** Salt-okunur araçlar ile eylem öneren çıktı ayrılır. Ajanın dosya, shell, serbest HTTP, veritabanı yönetimi veya kod çalıştırma aracı yoktur.
4. **Prompt injection:** Retrieval ve araç çıktısı veri olarak ele alınır. Politika atlatma denemeleri güvenlik loguna yazılır. Beklenen davranış: ajan yalnızca görünür bilgiyle akıl yürütür.
5. **Sınırlar:** maksimum ajan adımı, istek zaman aşımı, giriş doğrulama; araç logu (ad, argüman, süre, durum, hata); gizli anahtarlar repoya veya loglara yazılmaz.
6. **Eksik veri:** Araç başarısız olursa veya snapshot bayatsa ajan veri uydurmaz, `limitations` alanında güveni düşürdüğünü belirtir.

## 6. ActionPlan Şeması

Dosya: `schemas/action_plan.schema.json` (JSON Schema 2020-12).

- Üst alanlar: `summary` (≤400), `confidence` (0-1), `actions` (≤8), isteğe bağlı `evidence` (RAG kaynak ID'leri) ve `limitations`.
- Eylem tipleri: `TRAIN_UNIT`, `BUILD`, `MOVE_GROUP`, `RESEARCH`.
- Alanlar enum ile kısıtlıdır: `unit_type`, `building_type`, `tech_id`, `target_zone`. `count` 1-20 aralığındadır.
- Her eylem tipi için zorunlu ve yasak alanlar şemada tanımlıdır (ör. `TRAIN_UNIT` için `unit_type` ve `count` zorunlu, `tech_id` yasak). Ek alan kabul edilmez.
- Serbest metin hiçbir zaman doğrudan oyun komutu olarak yorumlanmaz.

Not: `BUILD` için `building_type` şimdilik House, Barracks, ArcheryYard ve Stable ile sınırlıdır (Citadel inşa edilemez). `RESEARCH` yalnızca `tech_id` alır.

## 7. Human-in-the-Loop ve GameActionValidator

1. Unity şema-geçerli planı alır. Şema ihlali yürütmeye hiç ulaşmaz.
2. Oyuncuya eylemler, gerekçeler, toplam maliyet, güven ve kanıt gösterilir.
3. Oyuncu **Approve** veya **Reject** seçer. Yalnızca salt-okunur analiz onay gerektirmez; durumu değiştiren her eylem onay ister.
4. **Reject:** karar loglanır, oyun durumu değişmez.
5. **Approve:** `GameActionValidator` güncel yetkili durumu yeniden kontrol eder, çünkü plan üretildikten sonra kaynaklar veya savaş durumu değişmiş olabilir.
6. Yalnızca geçen eylemler Fusion komutuna dönüştürülür. Reddedilenler ve sebepleri loglanır.

Doğrulayıcı kontrolleri: izinli eylem tipi, izinli ID'ler, sayı aralığı, gerekli bina, çağ ve teknoloji ön koşulları, maliyet, nüfus limiti, sahiplik, hedef bölge/görünürlük ve host otoritesi.

## 8. Repo Yapısı

| Klasör | İçerik |
|---|---|
| `UnityRTS/` | Unity 6 projesi |
| `agent/` | AI Gateway ve LLM ajanı |
| `mcp-server/` | Özel MCP sunucusu |
| `rag/knowledge/` | `units.json`, `buildings.json`, `technologies.json`, `rules.md` |
| `schemas/` | `action_plan.schema.json` |
| `evaluation/` | Test seti, loglar, metrikler (gecikme, araç doğruluğu, geçersiz eylem oranı, injection testi) |
| `docs/` | Mimari ve rapor belgeleri |

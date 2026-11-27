# Sistem Mimarisi Özeti

1. **Unity 6 + Photon:** RTS mekanikleri, Fog of War ve çok oyunculu durum senkronizasyonu. Oyuncu stratejik yardım istediğinde yalnızca güvenli, görünürlük filtresinden geçmiş `AiSafeSnapshot` gönderir.
2. **Custom MCP Server (Python):** Canlı güvenli durumu ve RAG bilgisini okuyan araçlar sunar (`get_player_resources`, `get_army_status`, `get_visible_enemies`, `get_technology_status`, `estimate_plan_cost`, `search_game_knowledge`).
3. **LLM Agent & RAG:** Soruya göre gerekli araçları dinamik seçer, kendi oyun kuralları/birim/teknoloji bilgi tabanından kaynak ID'leriyle bilgi çeker ve `ActionPlan` JSON şemasına uygun plan üretir.
4. **Onay & Doğrulama:** Plan Unity'de oyuncuya gösterilir. Onaylanırsa `GameActionValidator` güncel duruma göre tekrar kontrol eder ve geçerliyse yetkili oyun komutu çalıştırılır.
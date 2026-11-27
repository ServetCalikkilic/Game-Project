# CSE-377 Agentic Multiplayer RTS - Proje Kuralları

- **Oyun Motoru:** Unity 6 (3D, C#).
- **Multiplayer:** Photon Fusion 2.x (veya seçilen Photon PUN 2) ile en az 2 oyunculu senkronizasyon.
- **Kaynaklar (en az 3):** Wood (Odun), Food (Yiyecek), Gold (Altın).
- **Birimler & Binalar:** En az 3 birim türü (farklı maliyet, can, hasar, hız ve counter ilişkisi), en az 3 bina türü ve en az 1 teknoloji/çağ mekaniği.
- **Otorite Kuralı:** AI planları oyunu doğrudan değiştirmez; önce oyuncu onaylar (Approve/Reject), ardından `GameActionValidator` ve yetkili Photon/Unity akışı doğrular.
- **Güvenlik & Fog of War:** Gizli düşman birimleri, keşfedilmemiş alan ve rakibin gizli ekonomisi LLM'e/prompt'a gönderilmez. Filtreleme kod tarafında yapılır. API anahtarları ve token'lar asla repoya veya loglara yazılmaz.
- **Kod Yaklaşımı:** Değişiklikleri küçük, modüler ve test edilebilir tut; bilinmeyen veriyi uydurma.
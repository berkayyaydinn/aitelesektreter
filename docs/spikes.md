# Spike Rehberi (kod yazmadan önce kanıtla)

Her spike bağımsız, kısa, **git/karar öncesi** çalıştırılır. Amaç: belirsizliği erken öldür.

## Spike 1 — SIP trunk → LiveKit SIP (1 hafta)
**Hedef:** TR sağlayıcıdan tek DID'e gelen çağrı LiveKit odasına düşsün.
1. TR sağlayıcıda (Verimor/NetGSM) 1 test DID + SIP trunk al (IP auth).
2. LiveKit Cloud veya self-host SIP'i kur; `infra/livekit/inbound-trunk.json` uygula.
3. Codec G.711'e sabitle. DID'i telefondan ara.
4. **Başarı:** INVITE LiveKit'e ulaşıyor, oda açılıyor, ses iki yönlü.
5. **Olmazsa:** araya FreeSWITCH/Asterisk SBC koy.

## Spike 2 — Çağrı yönlendirme doğrulama (1 gün)
**Hedef:** Gerçek GSM → DID yönlendirme 3 operatörde çalışıyor mu?
1. Test SIM'de `**21*DID#` çevir.
2. Numarayı başka telefondan ara → Spike 1 hattına düşüyor mu?
3. Operatör bazlı davranışı (gecikme, sesli mesaj) not et.

## Spike 3 — Türkçe STT/TTS kalite/gecikme (2 gün)
**Hedef:** Telefon kalitesinde (8kHz) kabul edilebilir Türkçe.
1. 20-50 gerçek domain cümlesi kaydet (randevu/sipariş ifadeleri).
2. Deepgram nova-3 / Azure STT / Whisper large-v3 → WER karşılaştır.
3. Azure Neural TR / ElevenLabs multilingual → doğallık + gecikme.
4. **Başarı:** WER kabul edilebilir + uçtan uca yanıt <~1.5 sn.

## Spike 4 — Meta WhatsApp template gönderimi (paralel, 1-3 gün + onay süresi)
**Hedef:** Onaylı template ile mesaj gitsin.
1. Meta Business + Business Verification başlat (uzun sürer, hemen başla).
2. Tek WABA + WhatsApp Cloud API test numarası.
3. `randevu_onayi` template'i oluştur + onaya gönder.
4. **Başarı:** Cloud API'den gerçek numaraya template ulaşıyor.

## Spike 5 — LLM Türkçe diyalog + tool-calling (1 gün)
**Hedef:** Hızlı bulut model Türkçe randevu diyaloğunu tool-calling ile yürütüyor mu?
1. Claude Haiku / GPT-4o-mini / Gemini Flash karşılaştır.
2. `check_availability` + `create_appointment` araçlarını mock backend'le test et.
3. **Başarı:** Doğru slot teklifi + doğru araç çağrısı + düşük gecikme.

---

**Karar kapısı:** 5 spike yeşil olmadan tam implementasyona geçme. Her biri swappable olduğu için
bir provider düşse alternatife geçilir, mimari değişmez.

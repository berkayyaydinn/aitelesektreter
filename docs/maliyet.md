# Maliyet Modeli — VoiceReception

> **Amaç:** Sistemin birim (dakika / çağrı) ve aylık sabit maliyetlerini şeffaf çıkarmak;
> tenant başına marj ve fiyatlandırma kararına temel oluşturmak.
>
> **Uyarı:** Sağlayıcı liste fiyatları zamanla değişir ve hacme göre indirim alır. Aşağıdaki
> rakamlar **public list-price tahminleridir**, kesin teklif değildir. Canlıya geçmeden her
> sağlayıcıdan güncel fiyat teyidi alın.

## Varsayımlar

| Parametre | Değer | Not |
|-----------|-------|-----|
| Kur | **1 USD = 46.42 ₺** | 2026-06-18 güncel (exchangerate-api) |
| Ortalama çağrı süresi | **3 dk** | randevu/sipariş diyaloğu |
| Ajan konuşma oranı | ~%45 süre | TTS karakter tahmini buradan |
| Çağrı başına WhatsApp | 1 utility şablon | görüşme sonrası bilgilendirme |
| Varsayılan stack | Deepgram nova-3 + gpt-4o-mini + Azure TR TTS | `.env` ile swappable |

---

## 1. Değişken Maliyet — Dakika Başına (varsayılan stack)

| Kalem | Sağlayıcı | Liste fiyatı | $/dk | ₺/dk |
|-------|-----------|--------------|------|------|
| STT (konuşma→metin) | Deepgram `nova-3` streaming | ~$0.0077/dk | 0.0077 | 0.36 |
| TTS (metin→konuşma) | Azure Neural `tr-TR-EmelNeural` | ~$16 / 1M karakter | 0.0064 | 0.30 |
| LLM (diyalog) | OpenAI `gpt-4o-mini` | $0.15/$0.60 per 1M tok (in/out) | 0.0007 | 0.03 |
| Medya/SIP köprü | LiveKit Cloud (telephony) | ~$0.005/katılımcı-dk | 0.0050 | 0.23 |
| Gelen hat | TR SIP trunk (terminasyon) | ~₺0.15/dk | 0.0032 | 0.15 |
| **Toplam değişken** | | | **~$0.023** | **~₺1.07** |

**3 dk ortalama çağrı → ~$0.069 ≈ ₺3.21** (mesajlaşma hariç).

> LLM kalemi düşük çünkü `gpt-4o-mini` ucuz. Model `gpt-4o`'ya yükseltilirse LLM ~16× artar
> (~₺0.5/dk). TTS sağlayıcısı ElevenLabs seçilirse TTS ~10× artar (aşağı bkz).

---

## 2. Çağrı Başına Mesajlaşma

| Kalem | Sağlayıcı | Birim fiyat | $/çağrı | ₺/çağrı |
|-------|-----------|-------------|---------|---------|
| WhatsApp utility şablon (TR) | Meta WhatsApp Cloud API | ~$0.04 / conversation | 0.040 | 1.86 |

> Meta TR "utility" konuşma fiyatı bölge/şablon türüne göre değişir. İlk N konuşma/ay ücretsiz
> kademe olabilir. Instagram (ertelenmiş) eklenirse ayrı satır.

---

## 3. Toplam Çağrı Maliyeti (varsayılan, 3 dk + 1 WhatsApp)

| Bileşen | ₺ |
|---------|---|
| Değişken (3 dk × ₺1.07) | 3.21 |
| WhatsApp bilgilendirme | 1.86 |
| **Çağrı başı toplam** | **~₺5.07 (~$0.11)** |

---

## 4. Aylık Sabit Maliyet (altyapı)

| Kalem | Sağlayıcı | Aylık $ | Aylık ₺ | Not |
|-------|-----------|---------|---------|-----|
| Voice worker compute | VM (2 vCPU / 4 GB) | ~$24 | ~1.114 | Python LiveKit worker |
| Backend + API | VM (2 vCPU / 4 GB) | ~$24 | ~1.114 | .NET 8 Minimal API |
| PostgreSQL | Managed (küçük) | ~$15 | ~696 | tek instance, MVP |
| LiveKit | Cloud free/starter veya self-host | $0–50 | 0–2.321 | self-host VM'e taşınabilir |
| DID kira (numara) | TR SIP sağlayıcı | ~$2 / numara | ~93 | **tenant başına** |
| **Sabit toplam (1 tenant)** | | **~$65–115** | **~₺3k–5.3k** | DID hariç ölçeklenir |

> Voice worker + backend tek VM'de birleştirilirse sabit maliyet ~$24 düşer. LiveKit self-host
> edilirse Cloud kalemi sıfırlanır, VM kalemi artar.

---

## 5. Senaryo: Tenant Başına Aylık Maliyet

Varsayım: tenant ayda **300 çağrı** alıyor.

| Kalem | Hesap | ₺/ay |
|-------|-------|------|
| Çağrı değişken | 300 × ₺5.07 | 1.521 |
| DID kira | 1 numara | 93 |
| Paylaşılan altyapı payı | (5 tenant'a bölünmüş ~₺4k) | 800 |
| **Tenant başı maliyet** | | **~₺2.414** |

> Marj örneği: tenant'a ₺2.500–4.000/ay paket satılırsa brüt marj %4–40 aralığında. Çağrı
> hacmi arttıkça paylaşılan altyapı payı düşer, marj artar.

---

## 5b. Dakika Hacmine Göre Maliyet Tablosu

Varsayım: ort. çağrı **3 dk** → çağrı sayısı = dk ÷ 3. Her çağrıda 1 WhatsApp.
Sabit platform altyapısı **₺4.000/ay** (DID hariç) — hacimden bağımsız.

- Değişken: **₺1.07/dk**
- WhatsApp (dk'ya yayılmış): ₺1.86 ÷ 3 = **₺0.62/dk**
- Değişken + mesaj efektif: **₺1.69/dk**

| Dakika/ay | Çağrı (~) | Değişken ₺ | WhatsApp ₺ | Değişken+Mesaj ₺ | + Sabit ₺ | **Toplam ₺/ay** | Efektif ₺/dk |
|-----------|-----------|------------|------------|------------------|-----------|-----------------|--------------|
| 1.000 | 333 | 1.070 | 620 | 1.690 | 4.000 | **5.690** | 5.69 |
| 1.500 | 500 | 1.605 | 930 | 2.535 | 4.000 | **6.535** | 4.36 |
| 2.000 | 667 | 2.140 | 1.240 | 3.380 | 4.000 | **7.380** | 3.69 |
| 3.000 | 1.000 | 3.210 | 1.860 | 5.070 | 4.000 | **9.070** | 3.02 |
| 5.000 | 1.667 | 5.350 | 3.100 | 8.450 | 4.000 | **12.450** | 2.49 |
| 10.000 | 3.333 | 10.700 | 6.200 | 16.900 | 4.000 | **20.900** | 2.09 |

> Hacim arttıkça sabit ₺4.000 dağılır → efektif dk maliyeti düşer (1.000 dk'da ₺5.69 → 10.000 dk'da ₺2.09).
> Committed-use indirimleri (Deepgram/OpenAI/Azure) yüksek hacimde değişken kalemi de düşürür — tabloda hesaba katılmadı.

---

## 6. Sağlayıcı Swap Etkisi (hızlı kıyas)

| Değişiklik | Etki |
|------------|------|
| LLM `gpt-4o-mini` → `gpt-4o` | LLM ~₺0.03 → ~₺0.5/dk (~16×) |
| TTS Azure → ElevenLabs | TTS ~₺0.30 → ~₺3.0/dk (~10×) |
| STT Deepgram → Azure/OpenAI | benzer aralık (~₺0.30–0.45/dk) |
| LiveKit Cloud → self-host | dk maliyeti ↓, sabit VM ↑ |

---

## 7. Türk Sağlayıcı Alternatifleri (₺ bazlı + KVKK)

**Neden:** Varsayılan stack (Deepgram US, OpenAI US, Azure EU) iki risk taşır:
1. **Kur riski** — fatura USD, gelir ₺. Kur artışı marjı yer.
2. **KVKK yurt dışı aktarım (m.9)** — ses verisi yurt dışı işleyiciye gider → arayan müşteriden
   **açık rıza** şart (metinler `docs/legal/kvkk-acik-riza.md`'de mevcut). TR sağlayıcı kullanılırsa
   veri **Türkiye'de** kalır → yurt dışı aktarım yükü kalkar, açık rıza basitleşir, ihlal riski düşer.

Provider'lar `.env` ile swappable; katman katman TR muadili konabilir.

| Katman | Global varsayılan | Türk alternatif(ler) | ₺ tahmini | KVKK / not |
|--------|-------------------|----------------------|-----------|------------|
| STT (TR) | Deepgram nova-3 | **Sestek**, **Cbot**, DataBoss | teklif bazlı, ~₺0.40–1.0/dk | Veri TR; on-prem/TR cloud seçeneği |
| TTS (TR) | Azure tr-TR | **Sestek**, **Cbot** | teklif bazlı, ~₺0.40–1.5/dk | Türkçe doğal ses; veri TR |
| LLM | OpenAI gpt-4o-mini | Self-host **Trendyol-LLM** / **VNGRS Kumru** / **T3 AI** / Cosmos Turkish-Llama (TR GPU) | GPU VM ~₺15k–40k/ay (hacme bölünür) | Veri TR; düşük hacimde global daha ucuz |
| SIP / DID | TR SIP trunk | **Netgsm**, **Verimor**, **Bulutfon**, **AloTech** | ~₺0.10–0.20/dk + numara kira | Zaten TR; ₺ faturalı |
| WhatsApp BSP | Meta Cloud (doğrudan) | **Netgsm**, **İletimerkezi**, **Turkcell**, Infobip-TR | ~₺0.80–1.8/konuşma | ₺ faturalı yerel BSP; veri akışı aynı (Meta) |
| Hosting / Bulut | yurt dışı IaaS | **Turkcell Bulut**, **Türk Telekom Bulut**, **Vargonen**, **Radore**, **Natro** | VM ~₺900–1.5k/ay | **Veri yerleşimi TR** — KVKK için kritik |

### Trade-off

| Boyut | Global stack | Türk stack |
|-------|--------------|------------|
| Kur riski | Yüksek (USD) | **Yok (₺)** |
| KVKK yurt dışı aktarım | Var → açık rıza şart | **Yok / minimal** |
| Birim ₺/dk | Düşük (~₺1.07) | Orta-yüksek (~₺1.5–3, teklife bağlı) |
| Türkçe ses kalitesi | İyi | İyi–çok iyi (yerel optimize) |
| Olgunluk / entegrasyon | Yüksek (LiveKit plugin hazır) | Değişken — özel entegrasyon gerekebilir |
| LLM (düşük hacim) | Çok ucuz | GPU sabit maliyeti ağır |

> **Pratik öneri (hibrit):** En riskli katmanları TR'ye taşı, ucuz/olgun olanı bırak.
> - **Hosting + DID + WhatsApp BSP → TR** (KVKK veri yerleşimi + ₺ fatura, maliyet farkı küçük).
> - **STT/TTS → TR** (ses = en hassas kişisel veri; yurt dışı aktarımdan kurtulmanın asıl kazancı burada).
> - **LLM → gpt-4o-mini kalabilir** düşük hacimde (₺0.03/dk); transkript metni anonimleştirilebilir.
>   Hacim büyüyünce self-host TR LLM'e geç.

> **Not:** Türkçe STT/TTS sağlayıcıları (Sestek/Cbot) genelde **kurumsal teklif** ile fiyatlandırır;
> public liste fiyatı yoktur. Yukarıdaki ₺ aralıkları tahmindir — teklif al, sözleşmeye **DPA + veri
> yerleşimi (TR)** şartını koy.

---

## 8. Hibrit Stack — Maliyet Tablosu (önerilen)

**Stack:** STT/TTS → **TR (Sestek/Cbot)**, Hosting + DID + WhatsApp BSP → **TR**,
LLM → **gpt-4o-mini** (düşük hacimde kalır), medya köprü → **LiveKit**.
KVKK: ses verisi TR'de kalır. Tek USD kalemi LLM + LiveKit (cüzi).

> TR STT/TTS kurumsal teklif bazlıdır; aşağıda **orta değer** alındı. Gerçek teklife göre ±%40 oynar.

### 8.1 Dakika Başına Değişken

| Kalem | Sağlayıcı | ₺/dk (orta) | Band |
|-------|-----------|-------------|------|
| STT (TR) | Sestek / Cbot | 0.70 | 0.40–1.0 |
| TTS (TR, ~%45 konuşma) | Sestek / Cbot | 0.40 | 0.18–0.68 |
| LLM | gpt-4o-mini (USD) | 0.03 | — |
| Medya/SIP köprü | LiveKit | 0.23 | — |
| Gelen hat | TR SIP (Netgsm/Verimor) | 0.15 | 0.10–0.20 |
| **Toplam değişken** | | **~₺1.51/dk** | ~1.0–2.2 |

### 8.2 Çağrı Başına (3 dk + 1 WhatsApp)

| Bileşen | ₺ |
|---------|---|
| Değişken (3 dk × ₺1.51) | 4.53 |
| WhatsApp (TR BSP, ~₺1.30/konuşma) | 1.30 |
| **Çağrı başı toplam** | **~₺5.83** |

### 8.3 Aylık Sabit (TR hosting)

| Kalem | Sağlayıcı | ₺/ay |
|-------|-----------|------|
| Voice worker VM | Turkcell/TT Bulut, Vargonen | ~1.300 |
| Backend + API VM | TR cloud | ~1.300 |
| PostgreSQL | TR managed/self | ~800 |
| LiveKit | Cloud veya TR VM self-host | ~1.000 |
| DID kira | Netgsm (tenant başına) | ~100 |
| **Sabit platform (DID hariç)** | | **~₺4.400** |

### 8.4 Dakika Hacmine Göre (hibrit)

Değişken ₺1.51/dk · WhatsApp ₺1.30/çağrı (3 dk → ₺0.43/dk) · sabit platform ₺4.500/ay.

| Dakika/ay | Çağrı (~) | Değişken ₺ | WhatsApp ₺ | + Sabit ₺ | **Toplam ₺/ay** | Efektif ₺/dk |
|-----------|-----------|------------|------------|-----------|-----------------|--------------|
| 1.000 | 333 | 1.510 | 433 | 4.500 | **6.443** | 6.44 |
| 1.500 | 500 | 2.265 | 650 | 4.500 | **7.415** | 4.94 |
| 2.000 | 667 | 3.020 | 867 | 4.500 | **8.387** | 4.19 |
| 3.000 | 1.000 | 4.530 | 1.300 | 4.500 | **10.330** | 3.44 |
| 5.000 | 1.667 | 7.550 | 2.167 | 4.500 | **14.217** | 2.84 |
| 10.000 | 3.333 | 15.100 | 4.333 | 4.500 | **23.933** | 2.39 |

### 8.5 Global vs Hibrit Kıyas

| | Global (USD) | Hibrit (TR) |
|--|--------------|-------------|
| Değişken ₺/dk | ~1.07 | ~1.51 |
| Çağrı başı ₺ | ~5.07 | ~5.83 |
| 2.000 dk/ay toplam ₺ | ~7.380 | ~8.387 |
| Kur riski | Yüksek | **Yok (LLM hariç cüzi)** |
| KVKK yurt dışı aktarım | Var → açık rıza şart | **Yok / minimal** |

> Hibrit ~%10–15 daha pahalı ama kur riskini ve KVKK ses-verisi aktarım yükünü kaldırır.
> Fark, açık rıza red oranı + kur dalgalanması düşünülünce çoğu senaryoda hibrit lehine kapanır.

---

## 9. Uygulanan Optimizasyon — Değişiklikler + Tablo

Aşağıdaki değişiklikler kodda **uygulandı**. Hepsi veri işleme hızını korur veya iyileştirir
(daha kısa konuşma + sentez gecikmesinin kalkması → latency düşer, artmaz).

| # | Değişiklik | Dosya | Maliyet etkisi | Hız etkisi |
|---|------------|-------|----------------|------------|
| 1 | Kısa-yanıt direktifi (1-2 cümle, tek bilgi/iste) | `voice-agent/prompts.py` | TTS karakter ~−%30, LLM çıktı token ↓ | Konuşma kısalır → ↓ latency |
| 2 | Sabit anons TTS cache (KVKK anonsu worker'da 1 kez sentez) | `voice-agent/static_audio.py`, `agent.py` | Anons TTS karakteri çağrı başına ~sıfır | Sentez gecikmesi yok → ↓ latency |
| 3 | Compute birleştirme (worker + backend tek VM, `BACKEND_BASE_URL=localhost`) | deploy/config | Sabit −₺1.300/ay (1 VM eksi) | Localhost çağrı → ↓ latency |

> Hata güvenliği: anons cache başarısız olursa otomatik düz `say`'e düşer — KVKK anonsu asla atlanmaz
> (`static_audio.say_cached` fallback). Test: `voice-agent/tests/test_static_audio.py` (4 test, geçti).

### 9.1 Optimize Dakika Başına Değişken (hibrit)

| Kalem | Önce ₺/dk | Sonra ₺/dk | Neden |
|-------|-----------|------------|-------|
| STT (TR) | 0.70 | 0.70 | değişmedi (arayan konuşması) |
| TTS (TR) | 0.40 | **0.22** | kısa yanıt + anons cache |
| LLM | 0.03 | **0.025** | daha az çıktı token |
| LiveKit | 0.23 | 0.23 | — |
| TR SIP | 0.15 | 0.15 | — |
| **Toplam** | **1.51** | **~1.33** | **−%12** |

Çağrı başı (3 dk + WhatsApp ₺1.30): **₺5.83 → ₺5.28**.
Sabit platform: **₺4.400 → ₺3.100/ay** (compute birleştirme).

### 9.2 Dakika Hacmine Göre (optimize hibrit)

Değişken ₺1.33/dk · WhatsApp ₺1.30/çağrı · sabit ₺3.100/ay.

| Dakika/ay | Çağrı (~) | Değişken ₺ | WhatsApp ₺ | + Sabit ₺ | **Toplam ₺/ay** | Efektif ₺/dk |
|-----------|-----------|------------|------------|-----------|-----------------|--------------|
| 1.000 | 333 | 1.330 | 433 | 3.100 | **4.863** | 4.86 |
| 1.500 | 500 | 1.995 | 650 | 3.100 | **5.745** | 3.83 |
| 2.000 | 667 | 2.660 | 867 | 3.100 | **6.627** | 3.31 |
| 3.000 | 1.000 | 3.990 | 1.300 | 3.100 | **8.390** | 2.80 |
| 5.000 | 1.667 | 6.650 | 2.167 | 3.100 | **11.917** | 2.38 |
| 10.000 | 3.333 | 13.300 | 4.333 | 3.100 | **20.733** | 2.07 |

### 9.3 Hibrit: Önce vs Optimize

| Dakika/ay | Hibrit ₺/ay | Optimize ₺/ay | Kazanç |
|-----------|-------------|---------------|--------|
| 1.000 | 6.443 | 4.863 | **−%25** |
| 2.000 | 8.387 | 6.627 | **−%21** |
| 5.000 | 14.217 | 11.917 | **−%16** |
| 10.000 | 23.933 | 20.733 | **−%13** |

> Düşük hacimde kazanç büyük (sabit maliyet birleştirmesi baskın); yüksek hacimde değişken
> tasarrufu (TTS) baskın. Konuşma kısaldığı için **ortalama çağrı süresi de düşebilir** —
> tabloda 3 dk sabit tutuldu, bu ek tasarruf modellenmedi.

---

## Maliyet Düşürme Kaldıraçları

1. **Sessizlik kırpma (VAD):** Silero VAD zaten pipeline'da — STT/LLM yalnız konuşma anında çalışsın.
2. **Kısa sistem promptu + özet bağlam:** her turda tüm geçmişi göndermek yerine özet → LLM token ↓.
3. **TTS cache:** sabit anonslar (KVKK onayı, karşılama) önceden sentezlenip dosyadan çalınsın → TTS dk ↓.
4. **Compute birleştirme:** voice worker + backend tek VM → sabit ~$24/ay ↓.
5. **Hacim indirimi:** Deepgram/OpenAI/Azure committed-use indirimleri >belli hacimde devreye girer.

---

*Kaynak fiyatlar 2026-06-18 itibarıyla public list-price tahminidir. Kur: 1 USD = 46.42 ₺. Sözleşme öncesi teyit şart.*

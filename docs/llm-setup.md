# LLM Kurulumu — Yerel / OpenAI-uyumlu (self-host) ve bulut

Bu doküman voice-agent'ın kullanacağı **LLM'i (diyalog beyni)** nasıl ayarlayacağını anlatır.
Öncelik: **maliyet düşük + doğal konuşma yüksek** → yerel (self-host, OpenAI-uyumlu) model önerilir.

> Mimari: LLM sağlayıcısı `voice-agent/providers/factory.py::build_llm` içinde **swappable**.
> `.env`'den seçilir, `agent.py` hangi sağlayıcının kullanıldığını bilmez. Ses hattı:
> `STT (Whisper self-host) → LLM (bu doküman) → TTS (Piper self-host)`.

---

## 1. Neden yerel / OpenAI-uyumlu?

- **Maliyet:** Çağrı başına LLM ücreti ~0 (kendi sunucun). Bulut token faturası yok.
- **KVKK / veri:** Konuşma metni Türkiye'deki kendi sunucunda kalır, dışarı çıkmaz.
- **Kilitlenme yok:** OpenAI-uyumlu `/v1` uç sunan her motor çalışır (vLLM, Ollama, LM Studio).
  LiveKit `openai` plugin'i `base_url` override ile bunlara bağlanır — **yeni bağımlılık yok**.

> ⚠️ **Kritik gereksinim — TOOL CALLING.** Ajan `tools.py` ile yoğun fonksiyon çağrısı yapar
> (`check_availability`, `create_appointment`, `create_order`, `create_invoice`). Seçtiğin model
> **fonksiyon/araç çağrısını güvenilir** desteklemeli. Desteklemeyen model randevu/sipariş alamaz.

---

## 2. Sunucu seçenekleri

Üçü de OpenAI-uyumlu `/v1/chat/completions` uç sunar. Öneri sırası:

### A) vLLM — **önerilen** (üretim, tool-calling en sağlam)

```bash
# GPU'lu sunucuda (Docker). Model + tool-calling parser'ı açıkça belirt.
docker run --gpus all -p 8000:8000 \
  -v ~/.cache/huggingface:/root/.cache/huggingface \
  vllm/vllm-openai:latest \
  --model Qwen/Qwen2.5-14B-Instruct \
  --served-model-name Qwen2.5-14B-Instruct \
  --enable-auto-tool-choice \
  --tool-call-parser hermes \
  --api-key sk-local
# Uç: http://<sunucu>:8000/v1   (served-model-name .env'deki LLM_MODEL ile birebir aynı olmalı)
```

- `--enable-auto-tool-choice` + `--tool-call-parser` **şart** (araç çağrısı bunlarla açılır).
  Qwen2.5 için parser `hermes`; Llama-3.1 için `llama3_json`.
- `--api-key` değeri `.env`'deki `LLM_API_KEY` ile eşleşmeli.

### B) Ollama — kolay (geliştirme/pilot, tool desteği modele bağlı)

```bash
ollama serve                        # OpenAI-uyumlu uç: http://localhost:11434/v1
ollama pull qwen2.5:14b-instruct    # tool-calling destekleyen etiket seç
```

- Ollama araç çağrısını **destekler ama modele göre değişir**; Qwen2.5 / Llama-3.1 instruct iyi.
  Küçük/eski modeller araç çağrısında zayıf → randevu akışını mutlaka test et (bkz. §5).
- `LLM_API_KEY` Ollama'da önemsiz; `sk-local` yeterli.

### C) LM Studio — masaüstü (Windows/Mac dev)

- "Local Server" → OpenAI-uyumlu uç (`http://localhost:1234/v1`). Tool-calling destekli GGUF seç.

---

## 3. Model seçimi

| Model | Boyut | Türkçe | Tool-calling | Kaynak (kabaca) | Not |
|-------|-------|--------|--------------|-----------------|-----|
| **Qwen2.5-14B-Instruct** | 14B | Çok iyi | Güçlü | ~28 GB VRAM (bf16) / ~10 GB (AWQ/GPTQ 4-bit) | **Denge — önerilen** |
| Qwen2.5-7B-Instruct | 7B | İyi | İyi | ~16 GB / ~6 GB (4-bit) | Ucuz/hızlı, düşük gecikme |
| Qwen2.5-32B-Instruct | 32B | Çok iyi | Çok güçlü | ~64 GB / ~20 GB (4-bit) | En kaliteli, daha pahalı |
| Llama-3.1-8B-Instruct | 8B | İyi | İyi (`llama3_json`) | ~16 GB / ~6 GB | Alternatif |
| Llama-3.1-70B-Instruct | 70B | Çok iyi | Çok güçlü | çok yüksek | Kalite tavanı |

- **Telefon diyaloğu için gecikme kritik.** 7B/14B + 4-bit kuantizasyon (AWQ/GPTQ) düşük gecikme
  ve düşük VRAM verir; kalite düşüşü telefon için genelde kabul edilebilir.
- GPU yoksa (CPU-only VPS): küçük modeller bile yavaş → gerçek-zamanlı çağrıda **GPU önerilir**.
  CPU zorunluysa 7B-Q4 + kısa yanıt limitleri dene, gecikmeyi ölç.

---

## 4. voice-agent'a bağlama (`voice-agent/.env`)

```env
LLM_PROVIDER=local                       # local | openai_compatible | openai
LLM_MODEL=Qwen2.5-14B-Instruct           # vLLM served-model-name / Ollama tag ile AYNI
LLM_BASE_URL=http://<llm-sunucu>:8000/v1 # OpenAI-uyumlu uç (boş → gerçek OpenAI)
LLM_API_KEY=sk-local                     # vLLM --api-key ile eşleşmeli; Ollama'da önemsiz
SPEECH_LANGUAGE=tr
```

Kod tarafı (referans, değiştirmene gerek yok) — `providers/factory.py`:

```python
if provider in ("openai", "local", "openai_compatible"):
    from livekit.plugins import openai
    base_url = os.getenv("LLM_BASE_URL")
    if base_url:
        return openai.LLM(model=model, base_url=base_url,
                          api_key=os.getenv("LLM_API_KEY", "sk-local"))
    return openai.LLM(model=model)   # base_url yok → gerçek OpenAI
```

`docker-compose` ile çalıştırıyorsan LLM sunucusunu ayrı servis yap ve `LLM_BASE_URL`'i
servis adına (`http://llm:8000/v1`) yönlendir.

---

## 5. Tool-calling'i doğrula (şart)

Model araç çağırmıyorsa randevu/sipariş çalışmaz. Hızlı manuel test:

```bash
cd voice-agent
pip install -r requirements.txt
python agent.py dev
# Kayıtlı tenant DID'ini ara → "yarın saat 3'e randevu istiyorum" de.
# Beklenen: ajan check_availability / create_appointment tool'unu çağırır (log'da görünür).
```

Düz sohbet edip araç çağırmıyorsa: (a) tool-parser bayraklarını kontrol et (vLLM),
(b) tool-calling'i daha iyi bir modele geç (Qwen2.5-14B+), (c) Ollama'da model etiketini değiştir.

Otomatik test (factory base_url override'ı):

```bash
cd voice-agent && pytest tests/test_providers.py
```

---

## 6. Bulut OpenAI'ye geri dönüş (isteğe bağlı)

Yerelden vazgeçersen tek satırla bulut modele dön:

```env
LLM_PROVIDER=openai
LLM_MODEL=gpt-4o-mini
LLM_BASE_URL=            # BOŞ bırak → gerçek OpenAI
OPENAI_API_KEY=sk-...    # OpenAI anahtarı
```

`LLM_BASE_URL` boş olduğunda kod otomatik gerçek OpenAI'ye gider (geriye uyumlu).

---

## 7. STT/TTS ile birlikte düşük maliyet

En düşük maliyet için üç sağlayıcı da self-host:

```env
STT_PROVIDER=whisper   # yerel faster-whisper (docs: .env.example STT_BASE_URL)
LLM_PROVIDER=local     # bu doküman
TTS_PROVIDER=piper     # yerel Piper (TTS_BASE_URL)
```

Böylece çağrı başına dış API ücreti ~0 olur; tek maliyet sunucu (GPU/CPU) + telefon hattı.
Telefon hattı ve çağrı yönlendirme için: [netsantral-setup.md](netsantral-setup.md).

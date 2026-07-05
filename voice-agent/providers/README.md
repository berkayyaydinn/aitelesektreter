# Provider Soyutlaması (Swappable)

STT / LLM / TTS sağlayıcıları `.env` ile seçilir; `agent.py` hangi sağlayıcının çalıştığını bilmez.
Amaç: hiçbir dış servise kilitlenmemek (de-risk #3).

## Değiştirme

`.env`:
```
STT_PROVIDER=deepgram   # deepgram | azure | openai
LLM_PROVIDER=openai     # openai
TTS_PROVIDER=azure      # azure | elevenlabs | openai
```

Kod değişmez — sadece değer değişir.

## Yeni Sağlayıcı Ekleme

`factory.py` içindeki ilgili `build_*` fonksiyonuna bir dal ekle, paketi `requirements.txt`'e koy.
`agent.py` ve `tools.py` dokunulmaz.

## Türkçe / Telefon Notu

- STT telefon kalitesinde (8kHz) test edilmeli — bkz. `docs/spikes.md` Spike 3.
- TTS varsayılan: Azure `tr-TR-EmelNeural`. Alternatif ElevenLabs multilingual.

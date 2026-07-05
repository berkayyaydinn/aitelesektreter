# LiveKit SIP Kurulumu (Self-Host)

Gelen telefon çağrısını **kendi sunucumuzdaki** LiveKit'e taşıyıp voice-agent'ı tetikler.
LiveKit server + SIP, `infra/compose/docker-compose.yml` içinde `livekit` ve `livekit-sip`
servisleri olarak çalışır (config: `livekit.yaml` / `sip.yaml`).

## Akış
```
Netgsm SIP trunk (DID) → livekit-sip (5060) → livekit server (7880) → dispatch rule → oda + agent
```

## Adımlar (özet)
0. **Anahtar üret** (bir kez) ve `infra/compose/.env`'e yaz:
   ```
   docker run --rm livekit/livekit-server generate-keys
   # çıktıyı LIVEKIT_API_KEY / LIVEKIT_API_SECRET olarak .env'e koy
   ```
1. Stack'i ayağa kaldır: `docker compose up -d redis livekit livekit-sip`
2. **Netgsm'de** SIP trunk + DID al (IP auth). VPS public IP'sini Netgsm'e whitelist ettir;
   Netgsm SIP IP'sini `inbound-trunk.json` `allowed_addresses`'e yaz + DID'leri doldur.
3. `lk` CLI'yı self-host'a yönlendir, trunk + dispatch uygula:
   ```
   export LIVEKIT_URL=ws://localhost:7880
   export LIVEKIT_API_KEY=...   LIVEKIT_API_SECRET=...
   lk sip inbound create infra/livekit/inbound-trunk.json
   lk sip dispatch create infra/livekit/dispatch-rule.json
   ```
4. Voice worker zaten `telesekreter` agent adıyla compose'ta çalışır (`voice-agent`).

## Firewall (VPS)
Yalnız şunları dışarı aç: `5060/udp+tcp` (tercihen yalnız Netgsm IP'sine), `10000-20000/udp`
(RTP), `50000-60000/udp` (RTC). DB / Redis / STT / TTS / **MinIO (9000/9001) / egress**
portları iç ağda kalır (dışa kapalı). MinIO konsoluna gerekirse SSH tüneliyle eriş.

## Çağrı kaydı (opsiyonel)
`RECORDING_ENABLED=true` ise voice-agent her çağrıda audio-only egress başlatır → MinIO'ya OGG;
`CallLog.RecordingUrl` `s3://...` olarak dolar. `livekit-egress` headless Chrome çalıştırır →
CPU-only VPS'te ağır; pilotta aç, ölçekte ayrı egress sunucusu/GPU. KVKK kayıt anonsu zaten çalınıyor.

## Doğrulama
`docs/spikes.md` Spike 1 — DID'i ara, çağrı odaya düşüyor + ses iki yönlü mü?

## Çoklu DID / Ölçek
MVP'de DID'leri trunk `numbers` listesine ekle. Ölçekte: tek trunk + wildcard, tenant eşleme
tamamen `agent.py` → backend `GET /internal/tenants/by-did/{did}` ile yapılır.

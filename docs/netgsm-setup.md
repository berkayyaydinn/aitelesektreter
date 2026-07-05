# Netgsm Kurulumu (SMS + Ses Hattı)

Netgsm üç iş görür: **SMS** (hatırlatma), **gelen çağrı** (DID/SIP → LiveKit), **giden çağrı** (kampanya SIP).
Her biri ayrı yapılandırılır; hat gelene kadar sistem `console`/`null` ile çalışır.

## 1. SMS

1. Netgsm panelinde **gönderici başlığı** (msgheader) talebi → onay bekle.
2. `.env`: `SMS_PROVIDER=netgsm`, `NETGSM_USERCODE`, `NETGSM_PASSWORD`, `NETGSM_MSGHEADER`.
3. API: `GET https://api.netgsm.com.tr/sms/send/get/` (kod `Messaging/Sms/NetgsmSmsProvider.cs`).
4. Detay + yanıt kodları: [reminders.md](reminders.md).

## 2. Gelen çağrı (inbound) — kod değişikliği yok

Akış hazır (`voice-agent/agent.py` + `did.py`): SIP attribute'undan DID/arayan çıkarılır.

1. Netgsm'den **SIP trunk** + DID numara al; LiveKit'in IP'sini Netgsm'de izinli yap.
2. LiveKit'te **inbound SIP trunk** tanımla (Netgsm SIP kimlik/host).
3. LiveKit **dispatch rule** → gelen çağrıyı oda + telesekreter agent'a yönlendir; DID katılımcı attribute/metadata'sına yazılır (`sip.trunkPhoneNumber`).
4. Backend'de numarayı tenant'a bağla: `PhoneNumbers` tablosuna `Did = <Netgsm DID>` ekle (DID→tenant routing).
5. Numarayı ara → KVKK anonsu + Türkçe ajan açılır.

## 3. Giden çağrı (outbound) — `OUTBOUND_DIALER=livekit`

Kampanya aramaları için `LiveKitOutboundDialer` (LiveKit `CreateSIPParticipant`).

1. Netgsm'den **outbound SIP trunk** → LiveKit'te outbound trunk tanımla, `trunk id` not et.
2. `.env`: `OUTBOUND_DIALER=livekit`, `NETGSM_SIP_OUTBOUND_TRUNK_ID`, `LIVEKIT_URL/API_KEY/API_SECRET`.
3. `CampaignRunner` her aramadan önce İYS onayı + izinli saat (09:00–21:00 TR) kontrol eder (mevcut).
4. Dialer aranan numarayı E.164'e çevirip LiveKit odasına SIP katılımcısı ekler; kampanya script'i katılımcı metadata'sında taşınır (dispatch rule worker'a yönlendirir).

> SIP davranışı canlı hatla doğrulanmalı. JWT üretimi + istek gövdesi birim test edilir (`LiveKitDialerTests`); gerçek arama akışı manuel.

## Güvenlik
- Netgsm şifresi / LiveKit secret yalnız `.env`'de — asla commit etme.
- DB portları `127.0.0.1`'e bağlı; trunk erişimi IP allowlist ile sınırla.

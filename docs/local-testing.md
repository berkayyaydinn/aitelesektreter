# Lokal Test (Postgres / Docker / LiveKit / Meta GEREKMEZ)

Bloklu dış bağımlılıkların alternatifleriyle backend'i tek makinede uçtan uca test et.

| Gerçek bağımlılık | Lokal alternatif | Anahtar |
|-------------------|------------------|---------|
| PostgreSQL | **SQLite** (dosya, otomatik şema) | `DB_PROVIDER=sqlite` |
| Meta WhatsApp | **Console dry-run** (log'a yazar) | `MESSAGING_PROVIDER=console` |
| LiveKit + telefon | **`scripts/smoke_test.py`** (sözleşme taklidi) | — |

## 1. Backend'i lokal modda çalıştır

```bash
cd backend
# Varsayılanlar zaten sqlite + console; sadece iç anahtarı ver.
INTERNAL_API_KEY=test-key ASPNETCORE_URLS=http://localhost:5080 \
  dotnet run --project src/VoiceReception.Api
```

`GET http://localhost:5080/health` → `{"status":"ok","db":"sqlite","messaging":"console"}`

İlk açılışta SQLite şeması otomatik oluşur (`EnsureCreated`), `app.db` dosyası yazılır.

## 2. Uçtan uca smoke test

Voice worker'ın yaptığı çağrı sözleşmesini telefon/LiveKit olmadan birebir test eder:

```bash
INTERNAL_API_KEY=test-key python scripts/smoke_test.py
```

Doğruladığı akış (her çalışmada benzersiz DID):
1. health
2. tenant oluştur + DID + `**21*DID#` yönlendirme talimatı
3. hizmet ekle + çalışma saati ayarla
4. internal `by-did` (worker tenant config çeker)
5. uygunluk slotları (45 dk hizmet → doğru slot matematiği)
6. randevu oluştur → **console dry-run WhatsApp** tetiklenir
7. aynı slota tekrar → `conflict`
8. randevu sonrası uygunluk daralır
9. sipariş oluştur
10. çağrı olayı + KVKK consent
11. anahtarsız erişim → 401

**Beklenen:** `15 geçti, 0 başarısız`. Dry-run mesajı backend log'unda:
`[DRY-RUN mesaj] -> +905... | template=randevu_onayi | parametreler=[...]`

## 3. Birim + integration testleri

```bash
cd backend
dotnet test tests/VoiceReception.Tests/VoiceReception.Tests.csproj
```

→ `12 geçti`:
- 5 birim — SchedulingService (SQLite in-memory)
- 3 integration — `WebApplicationFactory` + izole SQLite, gerçek HTTP (health, 401 auth, tam randevu akışı). Sunucuyu ayrıca başlatmana gerek yok; test kendi host'unu kaldırır.
- 4 İYS/kampanya — onay yok / saat dışı / izinli / CampaignRunner kapısı.

### Coverage (.NET)

```bash
cd backend
dotnet test tests/VoiceReception.Tests/VoiceReception.Tests.csproj --settings coverlet.runsettings
# cobertura -> tests/VoiceReception.Tests/TestResults/<guid>/coverage.cobertura.xml
```

`coverlet.runsettings` auto-generated EF migration'larını hariç tutar. **Satır kapsamı ~%95**
(iş kodu). WhatsAppCloud gerçek gönderim, ConsoleDialer, İYS kapısı, randevu, kampanya hepsi kapsamda.

## 4. Voice worker (Python) birim testleri

LiveKit gerektirmez — saf yardımcılar + `BackendClient` (httpx.MockTransport) test edilir.

```bash
cd voice-agent
pip install -r requirements-dev.txt
python -m pytest -q
```

→ `19 geçti`: prompt kurulumu, DID çıkarımı, provider fabrika hata yolu, BackendClient sözleşmesi
(get/post/hata yutma/HTTP hata), config env parse.

Coverage:
```bash
python -m pytest --cov=prompts --cov=did --cov=backend_client --cov=config --cov=providers --cov-report=term-missing
```
Test edilebilir modüllerde **~%83** (factory.py geçerli sağlayıcı dalları livekit ister → kapsam dışı;
`agent.py`/`tools.py` livekit-bağlı, ayrı CI'da test edilir).

## Gerçek moda geçiş

`.env`:
```
DB_PROVIDER=postgres            # + DATABASE_URL
MESSAGING_PROVIDER=whatsapp_cloud   # + WHATSAPP_* token
```
Kod değişmez — sadece anahtar. Postgres'te `Migrate()` çalışır (`Migrations/Init`).

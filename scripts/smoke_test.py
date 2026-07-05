"""Uçtan uca smoke test — backend internal + tenant API'sini telefon/LiveKit olmadan doğrular.

Sadece Python stdlib (urllib) — ekstra bağımlılık yok. Backend lokal modda (SQLite + console) ayakta
olmalı. Voice worker'ın yaptığı çağrı sözleşmesini birebir taklit eder.

Çalıştır:
    INTERNAL_API_KEY=test-key python scripts/smoke_test.py            # vars. http://localhost:5080
    BASE_URL=http://localhost:5080 python scripts/smoke_test.py
"""
from __future__ import annotations

import datetime
import json
import os
import random
import sys
import urllib.error
import urllib.request

BASE = os.getenv("BASE_URL", "http://localhost:5080")
KEY = os.getenv("INTERNAL_API_KEY", "test-key")

# Her çalışmada benzersiz DID — tekrar çalıştırılabilir (unique index çakışmaz).
DID = f"0850{random.randint(1000000, 9999999)}"


def _next_monday() -> str:
    """Gelecekteki ilk Pazartesi (ISO). Çalışma saati day=1/Pazartesi; geçmiş-filtresine takılmaz."""
    today = datetime.date.today()
    days_ahead = 7 - today.weekday()  # Pazartesi=0 → her zaman 1..7 gün ileri, hep Pazartesi
    return (today + datetime.timedelta(days=days_ahead)).isoformat()


# Randevu tarihi: sabit geçmiş tarih yerine dinamik gelecek Pazartesi (geçmiş-saat reddini önler).
APPT_DATE = _next_monday()

_passed = 0
_failed = 0


def _req(method: str, path: str, body=None, key: str | None = None):
    url = f"{BASE}{path}"
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    if data is not None:
        req.add_header("Content-Type", "application/json")
    if key:
        req.add_header("X-Internal-Key", key)
    try:
        with urllib.request.urlopen(req) as resp:
            raw = resp.read().decode()
            return resp.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        return e.code, None


def check(name: str, cond: bool, detail: str = "") -> None:
    global _passed, _failed
    if cond:
        _passed += 1
        print(f"  PASS  {name}")
    else:
        _failed += 1
        print(f"  FAIL  {name}  {detail}")


def main() -> int:
    print(f"== Smoke test -> {BASE} ==")

    # 1) health
    st, h = _req("GET", "/health")
    check("health 200", st == 200, f"status={st}")
    check("health db=sqlite", bool(h) and h.get("db") == "sqlite", str(h))

    # 2) tenant oluştur (ownerPhone ile — fatura kesme için sahip numarası)
    owner = "+905550009999"
    st, t = _req("POST", "/api/tenants",
                 {"businessName": "Berkay Kuafor", "did": DID, "extraPrompt": "Erkek kuaforu.",
                  "ownerPhone": owner})
    check("tenant created", st == 200 and bool(t), f"status={st}")
    tid, did = t["tenantId"], t["did"]
    check("forwarding instruction", t.get("forwardingInstruction") == f"**21*{DID}#", str(t))

    # 3) hizmet ekle (45 dk)
    st, s = _req("POST", f"/api/tenants/{tid}/services", {"name": "Sac kesimi", "durationMinutes": 45})
    check("service added", st == 200 and bool(s), f"status={st}")
    sid = s["serviceId"]

    # 4) Pazartesi 09:00-12:00 (Day=1)
    st, _ = _req("PUT", f"/api/tenants/{tid}/hours",
                 [{"day": 1, "open": "09:00", "close": "12:00", "isClosed": False}])
    check("hours set", st == 200, f"status={st}")

    print("-- internal API (voice worker sözleşmesi) --")

    # 5) by-did (worker çağrı başında çeker)
    st, bd = _req("GET", f"/internal/tenants/by-did/{did}", key=KEY)
    check("by-did 200", st == 200 and bool(bd), f"status={st}")
    check("by-did has service", bool(bd) and len(bd.get("services", [])) == 1, str(bd))

    # 6) availability (gelecek Pazartesi); 45 dk -> 09:00,09:45,10:30,11:15
    st, av1 = _req("POST", "/internal/availability",
                   {"tenantId": tid, "serviceId": sid, "date": APPT_DATE}, key=KEY)
    check("availability returns slots", st == 200 and av1 and len(av1["slots"]) == 4, str(av1))

    # 7) randevu 09:45
    st, ap = _req("POST", "/internal/appointments",
                  {"tenantId": tid, "serviceId": sid, "date": APPT_DATE, "time": "09:45",
                   "customerName": "Ali Veli", "customerPhone": "+905551112233"}, key=KEY)
    check("appointment booked", st == 200 and ap and ap.get("status") == "booked", str(ap))

    # 8) aynı slota tekrar -> conflict
    st, ap2 = _req("POST", "/internal/appointments",
                   {"tenantId": tid, "serviceId": sid, "date": APPT_DATE, "time": "09:45",
                    "customerName": "Veli Ali", "customerPhone": "+905559998877"}, key=KEY)
    check("duplicate -> conflict", st == 200 and ap2 and ap2.get("status") == "conflict", str(ap2))

    # 9) availability tekrar: 09:45 ve çakışan 09:00,10:30 düşmeli -> 11:15 kalır (45dk)
    st, av2 = _req("POST", "/internal/availability",
                   {"tenantId": tid, "serviceId": sid, "date": APPT_DATE}, key=KEY)
    check("availability shrinks after booking", st == 200 and av2 and len(av2["slots"]) < 4, str(av2))

    # 10) sipariş oluştur
    st, od = _req("POST", "/internal/orders",
                  {"tenantId": tid, "items": "2 sise sampuan", "customerName": "Ali",
                   "customerPhone": "+905551112233"}, key=KEY)
    check("order created", st == 200 and od and od.get("orderId"), str(od))

    # 11) çağrı olayı (call_started + KVKK consent)
    st, _ = _req("POST", "/internal/calls/events",
                 {"tenantId": tid, "did": did, "event": "call_started",
                  "consent": "call_recording_notified", "customerPhone": "+905551112233"}, key=KEY)
    check("call event recorded", st == 200, f"status={st}")

    # 12) fatura — sahip numarasından (callerPhone == ownerPhone) -> Issued
    st, inv = _req("POST", "/internal/invoices",
                   {"tenantId": tid, "callerPhone": owner, "customerName": "Ali",
                    "amount": 1500.5, "description": "Hizmet"}, key=KEY)
    check("invoice issued (owner)", st == 200 and inv and inv.get("status") == "Issued", f"{st} {inv}")

    # 13) fatura — sahip olmayan numara -> 403
    st, _ = _req("POST", "/internal/invoices",
                 {"tenantId": tid, "callerPhone": "+905550000000", "customerName": "X", "amount": 10}, key=KEY)
    check("invoice denied (non-owner) -> 403", st == 403, f"status={st}")

    # 14) yetkisiz erişim -> 401
    st, _ = _req("POST", "/internal/availability",
                 {"tenantId": tid, "serviceId": sid, "date": APPT_DATE})  # anahtarsız
    check("no key -> 401", st == 401, f"status={st}")

    print(f"\n== Sonuç: {_passed} geçti, {_failed} başarısız ==")
    return 0 if _failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())

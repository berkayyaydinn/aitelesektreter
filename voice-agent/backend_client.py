"""Backend internal API istemcisi. Tüm iş kararları backend'e sorulur.

Voice worker iş mantığı tutmaz — tenant config çeker, randevu/sipariş oluşturur, olay bildirir.
Her istek X-Internal-Key ile kimlik doğrular.
"""
from __future__ import annotations

import logging
from typing import Any

import httpx

from config import settings

logger = logging.getLogger("telesekreter")


class BackendClient:
    def __init__(self, transport: httpx.AsyncBaseTransport | None = None) -> None:
        # transport: testlerde httpx.MockTransport enjekte etmek için.
        self._http = httpx.AsyncClient(
            base_url=settings.backend_base_url,
            headers={"X-Internal-Key": settings.internal_api_key},
            timeout=10.0,
            transport=transport,
        )

    async def aclose(self) -> None:
        await self._http.aclose()

    async def get_tenant_by_did(self, did: str) -> dict[str, Any]:
        """Çağrılan DID'den tenant config (prompt, çalışma saati, hizmetler)."""
        resp = await self._http.get(f"/internal/tenants/by-did/{did}")
        resp.raise_for_status()
        return resp.json()

    async def check_availability(self, tenant_id: str, service_id: str, date: str) -> list[str]:
        """Belirli gün için uygun randevu slotları."""
        resp = await self._http.post(
            "/internal/availability",
            json={"tenantId": tenant_id, "serviceId": service_id, "date": date},
        )
        resp.raise_for_status()
        return resp.json().get("slots", [])

    async def create_appointment(self, payload: dict[str, Any]) -> dict[str, Any]:
        """Randevu oluştur (backend çakışmayı doğrular)."""
        resp = await self._http.post("/internal/appointments", json=payload)
        resp.raise_for_status()
        return resp.json()

    async def create_order(self, payload: dict[str, Any]) -> dict[str, Any]:
        resp = await self._http.post("/internal/orders", json=payload)
        resp.raise_for_status()
        return resp.json()

    async def create_invoice(self, payload: dict[str, Any]) -> dict[str, Any]:
        """Fatura kes (backend sahip doğrular; yetkisizse 403)."""
        resp = await self._http.post("/internal/invoices", json=payload)
        resp.raise_for_status()
        return resp.json()

    async def report_call_event(self, payload: dict[str, Any]) -> dict[str, Any] | None:
        """Çağrı olayı / onay / transkript bildir (best-effort).

        Başarılıysa parse edilmiş yanıtı döndürür (call_started → {callLogId}); hata olursa
        None — olay bildirimi çağrıyı düşürmemeli, ama gözlemlenebilirlik için loglanır.
        """
        try:
            resp = await self._http.post("/internal/calls/events", json=payload)
            resp.raise_for_status()
            try:
                return resp.json()
            except ValueError:
                return None
        except httpx.HTTPError:
            logger.warning(
                "Çağrı olayı bildirilemedi: %s", payload.get("event"), exc_info=True
            )
            return None

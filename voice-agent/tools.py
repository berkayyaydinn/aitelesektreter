"""LLM'in çağırabileceği araçlar (function calling).

Her araç backend'e delege eder. İş kuralları (çakışma, çalışma saati) backend'de; burada sadece
LLM ile backend arasında köprü. LiveKit Agents @function_tool ile otomatik şemaya çevrilir.
"""
from __future__ import annotations

import httpx
from livekit.agents import RunContext, function_tool

from backend_client import BackendClient

# Backend geçici erişilemezken LLM'e dönen kısa Türkçe mesaj — çağrı düşmez, kullanıcı bilgilenir.
_BACKEND_DOWN = "Sistem şu an yanıt vermiyor, birazdan tekrar deneyelim."


def build_tools(backend: BackendClient, tenant_id: str):
    """Tenant kapsamlı araç seti üretir."""

    @function_tool
    async def check_availability(ctx: RunContext, service_id: str, date: str) -> str:
        """Bir hizmet için belirtilen gündeki uygun randevu saatlerini getir.

        Args:
            service_id: Hizmet kimliği (tenant config'deki listeden).
            date: ISO tarih, ör. 2026-06-15.
        """
        try:
            slots = await backend.check_availability(tenant_id, service_id, date)
        except httpx.HTTPError:
            return _BACKEND_DOWN
        if not slots:
            return "O gün uygun saat yok."
        return "Uygun saatler: " + ", ".join(slots)

    @function_tool
    async def create_appointment(
        ctx: RunContext,
        service_id: str,
        date: str,
        time: str,
        customer_name: str,
        customer_phone: str,
    ) -> str:
        """Randevu oluştur. Saatin uygun olduğu önce check_availability ile doğrulanmalı.

        Args:
            service_id: Hizmet kimliği.
            date: ISO tarih.
            time: HH:MM.
            customer_name: Müşteri adı.
            customer_phone: Müşteri telefon numarası.
        """
        try:
            result = await backend.create_appointment(
                {
                    "tenantId": tenant_id,
                    "serviceId": service_id,
                    "date": date,
                    "time": time,
                    "customerName": customer_name,
                    "customerPhone": customer_phone,
                }
            )
        except httpx.HTTPError:
            return _BACKEND_DOWN
        # Backend ret sebebini status ile ayırır; her birine özel kısa Türkçe yanıt.
        status = result.get("status")
        if status == "booked":
            return f"Randevu oluşturuldu: {date} {time}. Onay mesajı gönderilecek."
        if status == "conflict":
            return "O saat az önce doldu, başka bir saat önerebilirim."
        if status == "outside_hours":
            return "O saat çalışma saatleri dışında; çalışma saatleri içinde başka bir saat bulalım."
        if status == "past":
            return "Geçmiş bir saate randevu veremem; ileri bir tarih seçelim."
        if status == "service_unavailable":
            return "Bu hizmet şu anda verilemiyor."
        return "Randevu oluşturulamadı; başka bir saat deneyelim."

    @function_tool
    async def create_order(
        ctx: RunContext,
        items: str,
        customer_name: str,
        customer_phone: str,
    ) -> str:
        """Sipariş oluştur.

        Args:
            items: Sipariş kalemleri serbest metin.
            customer_name: Müşteri adı.
            customer_phone: Müşteri telefon numarası.
        """
        try:
            result = await backend.create_order(
                {
                    "tenantId": tenant_id,
                    "items": items,
                    "customerName": customer_name,
                    "customerPhone": customer_phone,
                }
            )
        except httpx.HTTPError:
            return _BACKEND_DOWN
        return f"Sipariş alındı (no: {result.get('orderId', '-')})."

    return [check_availability, create_appointment, create_order]


def build_invoice_tools(backend: BackendClient, tenant_id: str, caller_phone: str):
    """Sahip modu araçları — yalnızca işletme sahibi aradığında eklenir. Fatura kesme.

    Backend ayrıca callerPhone'u sahip numarasıyla doğrular (çift kontrol).
    """

    @function_tool
    async def create_invoice(
        ctx: RunContext,
        customer_name: str,
        amount: float,
        description: str = "",
        customer_phone: str = "",
    ) -> str:
        """Müşteriye fatura kes.

        Args:
            customer_name: Fatura kesilecek müşterinin adı.
            amount: Tutar (TL).
            description: Fatura açıklaması.
            customer_phone: Müşteri telefonu (opsiyonel).
        """
        try:
            result = await backend.create_invoice(
                {
                    "tenantId": tenant_id,
                    "callerPhone": caller_phone,
                    "customerName": customer_name,
                    "amount": amount,
                    "description": description,
                    "customerPhone": customer_phone,
                }
            )
        except Exception:
            return "Fatura kesilemedi (yetki veya sistem hatası)."
        return f"Fatura kesildi: {amount} TL, {customer_name}. Durum: {result.get('status', '-')}."

    return [create_invoice]

"""BackendClient — httpx.MockTransport ile gerçek HTTP olmadan internal API sözleşmesini test eder."""
import httpx
import pytest

from backend_client import BackendClient


def _client(handler):
    return BackendClient(transport=httpx.MockTransport(handler))


async def test_get_tenant_by_did_sends_key_and_parses():
    captured = {}

    def handler(request: httpx.Request) -> httpx.Response:
        captured["url"] = str(request.url)
        captured["key"] = request.headers.get("X-Internal-Key")
        return httpx.Response(200, json={"tenantId": "t1", "services": []})

    client = _client(handler)
    result = await client.get_tenant_by_did("08501112233")

    assert result["tenantId"] == "t1"
    assert captured["url"].endswith("/internal/tenants/by-did/08501112233")
    assert captured["key"] == "test-key"  # conftest INTERNAL_API_KEY
    await client.aclose()


async def test_check_availability_posts_payload_and_returns_slots():
    captured = {}

    def handler(request: httpx.Request) -> httpx.Response:
        import json as _json
        captured["body"] = _json.loads(request.content)
        return httpx.Response(200, json={"slots": ["09:00", "10:00"]})

    client = _client(handler)
    slots = await client.check_availability("t1", "s1", "2026-06-15")

    assert slots == ["09:00", "10:00"]
    assert captured["body"] == {"tenantId": "t1", "serviceId": "s1", "date": "2026-06-15"}
    await client.aclose()


async def test_create_appointment_returns_status():
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json={"status": "booked", "appointmentId": "a1"})

    client = _client(handler)
    result = await client.create_appointment({"tenantId": "t1"})
    assert result["status"] == "booked"
    await client.aclose()


async def test_create_invoice_posts_and_returns_status():
    captured = {}

    def handler(request: httpx.Request) -> httpx.Response:
        import json as _json
        captured["body"] = _json.loads(request.content)
        return httpx.Response(200, json={"invoiceId": "i1", "status": "Issued"})

    client = _client(handler)
    result = await client.create_invoice(
        {"tenantId": "t1", "callerPhone": "+9055", "customerName": "Ali", "amount": 500}
    )
    assert result["status"] == "Issued"
    assert captured["body"]["amount"] == 500
    await client.aclose()


async def test_report_call_event_swallows_errors():
    def handler(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("backend down")

    client = _client(handler)
    # Olay bildirimi çağrıyı düşürmemeli — exception yutulur.
    await client.report_call_event({"tenantId": "t1", "event": "call_ended"})
    await client.aclose()


async def test_default_timeout_is_granular():
    # Genel istekler: connect 3s, read 10s, write 5s, pool 3s (düz 10s yerine).
    captured = {}

    def handler(request: httpx.Request) -> httpx.Response:
        captured["timeout"] = request.extensions.get("timeout")
        return httpx.Response(200, json={"tenantId": "t1"})

    client = _client(handler)
    await client.get_tenant_by_did("08501112233")
    assert captured["timeout"] == {"connect": 3.0, "read": 10.0, "write": 5.0, "pool": 3.0}
    await client.aclose()


async def test_tool_calls_use_short_timeout():
    # Konuşma içi araçlar: settings.tool_timeout_seconds (varsayılan 4.0) —
    # backend takılırsa 10 sn ölü hava yerine hızlı fallback.
    captured = {}

    def handler(request: httpx.Request) -> httpx.Response:
        captured["timeout"] = request.extensions.get("timeout")
        return httpx.Response(200, json={"slots": [], "status": "booked",
                                         "orderId": "o1", "invoiceId": "i1"})

    client = _client(handler)
    for call in (
        lambda: client.check_availability("t1", "s1", "2026-06-15"),
        lambda: client.create_appointment({"tenantId": "t1"}),
        lambda: client.create_order({"tenantId": "t1"}),
        lambda: client.create_invoice({"tenantId": "t1"}),
    ):
        captured.clear()
        await call()
        assert captured["timeout"]["read"] == 4.0, "araç çağrısı kısa timeout kullanmalı"
        assert captured["timeout"]["connect"] == 3.0
    await client.aclose()


async def test_raises_on_http_error():
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(404)

    client = _client(handler)
    with pytest.raises(httpx.HTTPStatusError):
        await client.get_tenant_by_did("missing")
    await client.aclose()

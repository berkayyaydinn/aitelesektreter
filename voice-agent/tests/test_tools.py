"""tools.py — backend hatası çağrıyı düşürmemeli (httpx hatası yakalanıp Türkçe fallback döner).

livekit kurulmadan test: `livekit.agents` sahte modülle (function_tool = kimlik) enjekte edilir,
böylece build_tools ham coroutine'leri döndürür ve doğrudan çağrılabilir.
"""
import sys
import types

import httpx
import pytest

# tools.py import edilmeden ÖNCE livekit.agents'ı sahtele (function_tool kimlik decorator).
_fake_agents = types.ModuleType("livekit.agents")
_fake_agents.function_tool = lambda f: f
_fake_agents.RunContext = object
sys.modules.setdefault("livekit", types.ModuleType("livekit"))
sys.modules["livekit.agents"] = _fake_agents

from backend_client import BackendClient  # noqa: E402
from tools import _BACKEND_DOWN, build_tools  # noqa: E402


def _backend(handler):
    return BackendClient(transport=httpx.MockTransport(handler))


def _down(request: httpx.Request) -> httpx.Response:
    raise httpx.ConnectError("backend down")


async def test_check_availability_returns_fallback_on_backend_error():
    check_availability, _, _ = build_tools(_backend(_down), "t1")
    result = await check_availability(None, "s1", "2026-06-15")
    assert result == _BACKEND_DOWN


async def test_create_appointment_returns_fallback_on_backend_error():
    _, create_appointment, _ = build_tools(_backend(_down), "t1")
    result = await create_appointment(None, "s1", "2026-06-15", "10:00", "Ali", "+9055")
    assert result == _BACKEND_DOWN


async def test_create_order_returns_fallback_on_backend_error():
    _, _, create_order = build_tools(_backend(_down), "t1")
    result = await create_order(None, "2 ekmek", "Ali", "+9055")
    assert result == _BACKEND_DOWN


async def test_check_availability_normal_path_unaffected():
    def ok(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json={"slots": ["09:00"]})

    check_availability, _, _ = build_tools(_backend(ok), "t1")
    result = await check_availability(None, "s1", "2026-06-15")
    assert "09:00" in result

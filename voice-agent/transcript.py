"""Diyalog geçmişinden transkript turlarını çıkarma — saf, LiveKit'ten bağımsız (test edilebilir).

agent.py çağrı bitişinde session geçmişini bu yardımcıyla backend sözleşmesine çevirir.
LiveKit sürümleri arası item şekli değişebildiği için defansif: hem nesne (.role/.content)
hem sözlük ({"role","content"}) biçimlerini tolere eder; tanımadığını atlar.
"""
from __future__ import annotations

from typing import Any

_DIALOG_ROLES = {"user", "assistant"}


def _get(item: Any, key: str) -> Any:
    if isinstance(item, dict):
        return item.get(key)
    return getattr(item, key, None)


def _text_of(item: Any) -> str:
    """Item içeriğini düz metne indirger (str | list[str] | None biçimlerini tolere eder)."""
    content = _get(item, "content")
    if content is None:
        content = _get(item, "text_content") or _get(item, "text")
    if content is None:
        return ""
    if isinstance(content, str):
        return content.strip()
    if isinstance(content, (list, tuple)):
        parts = [c for c in content if isinstance(c, str)]
        return " ".join(parts).strip()
    return str(content).strip()


def extract_turns(items: Any) -> list[dict[str, Any]]:
    """Geçmiş item'larından backend transkript turlarına çevirir.

    Sadece kullanıcı/asistan turları + boş olmayan metin alınır. occurredAt sunucu tarafında atanır.
    """
    if not items:
        return []
    turns: list[dict[str, Any]] = []
    for item in items:
        role = _get(item, "role")
        if role not in _DIALOG_ROLES:
            continue
        text = _text_of(item)
        if not text:
            continue
        turns.append({"role": role, "text": text, "occurredAt": None})
    return turns


def count_tool_calls(items: Any) -> int:
    """Tool etkileşimi sayısı (best-effort): rolü 'tool' olan veya tool_calls taşıyan item'lar."""
    if not items:
        return 0
    count = 0
    for item in items:
        if _get(item, "role") == "tool":
            count += 1
            continue
        tool_calls = _get(item, "tool_calls")
        if tool_calls:
            count += len(tool_calls) if isinstance(tool_calls, (list, tuple)) else 1
    return count

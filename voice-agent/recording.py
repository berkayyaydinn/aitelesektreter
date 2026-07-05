"""Çağrı kaydı için saf yardımcılar — LiveKit/S3 bağımlılığı YOK (kolay birim test).

agent.py bu fonksiyonlarla egress dosya anahtarını + recordingUrl'i üretir; gerçek egress
çağrısı (ctx.api.egress) agent.py içinde yapılır. Burada yan etki yok, sadece string üretimi.
"""
from __future__ import annotations

import re

_SAFE = re.compile(r"[^A-Za-z0-9_.-]+")


def _slug(value: str) -> str:
    """Dosya anahtarı için güvenli parça (S3 key'inde sorun çıkaracak karakterleri sadeleştir)."""
    cleaned = _SAFE.sub("-", (value or "").strip()).strip("-")
    return cleaned or "unknown"


def recording_filepath(room: str, started_iso: str) -> str:
    """Deterministik S3 object key: recordings/{room}/{timestamp}.ogg.

    started_iso: ISO zaman damgası (ör. "2026-06-23T10:15:30+00:00"); ':' S3'te sorun olmasın
    diye sadeleştirilir.
    """
    return f"recordings/{_slug(room)}/{_slug(started_iso)}.ogg"


def recording_url(bucket: str, key: str) -> str:
    """Kalıcı referans: s3://{bucket}/{key}. Admin/CRM oynatma için sonra presign eder."""
    return f"s3://{_slug(bucket)}/{key}"

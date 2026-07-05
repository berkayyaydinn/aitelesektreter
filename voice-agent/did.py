"""Çağrılan DID (numara) çıkarımı — saf, livekit bağımlılığı YOK (kolay birim test).

LiveKit SIP, çağrılan numarayı katılımcı attribute'unda ya da job metadata'da iletir.
Sağlayıcı kurulumuna göre anahtar değişebilir; çözümü tek yerde topla.
"""
from __future__ import annotations

from collections.abc import Iterable

# SIP sağlayıcıya göre çağrılan numarayı taşıyabilecek attribute anahtarları (öncelik sırası).
_DID_ATTRIBUTE_KEYS = ("sip.trunkPhoneNumber", "sip.phoneNumber")


def extract_caller(participant_attributes: Iterable[dict]) -> str | None:
    """Arayanın numarasını (from) döndürür — sahip moduna karar için. Yoksa None.

    SIP sağlayıcıya göre anahtar değişir; arayan numarası genelde `sip.phoneNumber`'da gelir
    (çağrılan/DID ise `sip.trunkPhoneNumber`).
    """
    for attrs in participant_attributes:
        if not isinstance(attrs, dict):
            continue
        caller = attrs.get("sip.phoneNumber")
        if caller:
            return caller
    return None


def extract_did(participant_attributes: Iterable[dict], metadata: str | None) -> str:
    """Katılımcı attribute listesinden (ya da metadata fallback) DID döndürür.

    Args:
        participant_attributes: her uzak katılımcının attributes dict'i.
        metadata: job metadata (attribute yoksa fallback).
    """
    for attrs in participant_attributes:
        if not isinstance(attrs, dict):
            continue
        for key in _DID_ATTRIBUTE_KEYS:
            did = attrs.get(key)
            if did:
                return did
    if metadata:
        return metadata
    raise RuntimeError("Çağrılan DID belirlenemedi (SIP attribute eksik).")

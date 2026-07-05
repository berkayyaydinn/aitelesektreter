"""Sabit anonslar için TTS cache — livekit bağımlılığı YOK (kolay birim test).

Amaç: KVKK kayıt onayı gibi her çağrıda **birebir aynı** olan metinleri her seferinde
yeniden sentezlememek. Worker ömründe bir kez sentezlenir, sonraki çağrılarda hazır ses
kareleri (frame) tekrar oynatılır.

Maliyet etkisi: tekrarlı TTS karakteri ~sıfırlanır (anons her çağrıda ücretliydi).
Hız etkisi: sentez gecikmesi kalkar → anons daha hızlı başlar (latency iyileşir, kötüleşmez).

`tts` ve `session` nesneleri dışarıdan enjekte edilir (duck-typing) — bu modül livekit
import etmez, sahte nesnelerle test edilir.
"""
from __future__ import annotations

import hashlib
from typing import Awaitable, Callable

# text -> sentezlenmiş ses kareleri (worker süreci boyunca paylaşılır)
_FRAME_CACHE: dict[str, list] = {}


def cache_key(text: str, voice: str = "") -> str:
    """Metin + ses kimliğinden kararlı anahtar (aynı metin+ses = aynı anahtar)."""
    return hashlib.sha1(f"{voice}|{text}".encode("utf-8")).hexdigest()


def clear_cache() -> None:
    """Test/yeniden yükleme için cache'i boşalt."""
    _FRAME_CACHE.clear()


def is_cached(text: str, voice: str = "") -> bool:
    return cache_key(text, voice) in _FRAME_CACHE


async def _synthesize_frames(tts, text: str) -> list:
    """TTS akışından tüm ses karelerini topla (tek seferlik sentez)."""
    frames = []
    async for event in tts.synthesize(text):
        frame = getattr(event, "frame", event)
        frames.append(frame)
    return frames


async def say_cached(
    session,
    tts,
    text: str,
    *,
    voice: str = "",
    allow_interruptions: bool = False,
    fallback: Callable[[], Awaitable[None]] | None = None,
) -> None:
    """Metni önbellekten oynat; ilk çağrıda sentezleyip cache'le.

    Herhangi bir hata olursa `fallback` (genelde düz `session.say(text)`) çağrılır —
    cache mekanizması asla anonsu düşürmez (KVKK anonsu kritiktir).
    """
    key = cache_key(text, voice)
    try:
        frames = _FRAME_CACHE.get(key)
        if frames is None:
            frames = await _synthesize_frames(tts, text)
            _FRAME_CACHE[key] = frames

        async def _gen():
            for frame in frames:
                yield frame

        await session.say(text, audio=_gen(), allow_interruptions=allow_interruptions)
    except Exception:
        if fallback is not None:
            await fallback()
        else:
            await session.say(text, allow_interruptions=allow_interruptions)

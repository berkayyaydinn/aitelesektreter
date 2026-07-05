"""Swappable STT/LLM/TTS provider fabrikaları.

Tek giriş noktası: build_stt / build_llm / build_tts. Sağlayıcı seçimi .env'den gelir,
çağıran kod (agent.py) hangi sağlayıcının kullanıldığını bilmez — kilitlenme yok.
"""
from .factory import build_llm, build_stt, build_tts

__all__ = ["build_stt", "build_llm", "build_tts"]

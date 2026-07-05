"""static_audio cache — sahte tts/session ile test (livekit kurmadan).

Doğrulanan: aynı metin worker ömründe bir kez sentezlenir; sonraki çağrılar cache'ten
oynatılır; hata durumunda fallback çağrılır (anons asla düşmez)."""
import pytest

import static_audio
from static_audio import cache_key, clear_cache, is_cached, say_cached


@pytest.fixture(autouse=True)
def _clear():
    clear_cache()
    yield
    clear_cache()


class FakeFrame:
    def __init__(self, i): self.i = i


class FakeTTS:
    """Sentez sayacı tutar — kaç kez gerçek sentez yapıldığını ölçmek için."""
    def __init__(self): self.synth_calls = 0

    def synthesize(self, text):
        self.synth_calls += 1

        class _Stream:
            def __aiter__(self_inner):
                self_inner._n = 0
                return self_inner

            async def __anext__(self_inner):
                if self_inner._n >= 2:
                    raise StopAsyncIteration
                self_inner._n += 1
                return FakeFrame(self_inner._n)

        return _Stream()


class FakeSession:
    def __init__(self): self.say_calls = []

    async def say(self, text, *, audio=None, allow_interruptions=True):
        frames = []
        if audio is not None:
            async for f in audio:
                frames.append(f)
        self.say_calls.append({"text": text, "frames": frames, "audio": audio is not None})


def test_cache_key_stable_and_voice_sensitive():
    assert cache_key("merhaba", "emel") == cache_key("merhaba", "emel")
    assert cache_key("merhaba", "emel") != cache_key("merhaba", "deniz")
    assert cache_key("a") != cache_key("b")


@pytest.mark.asyncio
async def test_synthesizes_once_then_serves_from_cache():
    tts, session = FakeTTS(), FakeSession()

    await say_cached(session, tts, "KVKK anons", voice="emel")
    await say_cached(session, tts, "KVKK anons", voice="emel")
    await say_cached(session, tts, "KVKK anons", voice="emel")

    assert tts.synth_calls == 1            # üç çağrı, tek sentez → maliyet kazancı
    assert is_cached("KVKK anons", "emel")
    assert len(session.say_calls) == 3
    assert all(c["frames"] for c in session.say_calls)  # her çağrı ses kareleri oynattı


@pytest.mark.asyncio
async def test_fallback_when_synthesis_fails():
    class BrokenTTS:
        def synthesize(self, text):
            raise RuntimeError("tts down")

    session = FakeSession()
    called = {"fb": False}

    async def fb():
        called["fb"] = True

    await say_cached(session, BrokenTTS(), "anons", fallback=fb)
    assert called["fb"] is True            # anons asla düşmez


@pytest.mark.asyncio
async def test_fallback_default_plain_say():
    class BrokenTTS:
        def synthesize(self, text):
            raise RuntimeError("down")

    session = FakeSession()
    await say_cached(session, BrokenTTS(), "anons")
    assert session.say_calls[-1]["audio"] is False   # düz say'e düştü

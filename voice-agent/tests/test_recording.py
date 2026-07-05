"""recording.py saf yardımcıları — deterministik key + URL, güvenli karakter sadeleştirme."""
from recording import recording_filepath, recording_url


def test_recording_filepath_deterministic_and_safe():
    key = recording_filepath("call-abc123", "2026-06-23T10:15:30+00:00")
    assert key.startswith("recordings/call-abc123/")
    assert key.endswith(".ogg")
    assert ":" not in key            # S3 key'inde ':' sorun çıkarmasın
    # aynı girdi = aynı anahtar
    assert key == recording_filepath("call-abc123", "2026-06-23T10:15:30+00:00")


def test_recording_filepath_sanitizes_room():
    key = recording_filepath("call/with spaces", "2026-01-01T00:00:00Z")
    assert " " not in key and "/call" not in key.replace("recordings/", "", 1)


def test_recording_url_s3_form():
    assert recording_url("telesekreter-recordings", "recordings/r/t.ogg") == \
        "s3://telesekreter-recordings/recordings/r/t.ogg"

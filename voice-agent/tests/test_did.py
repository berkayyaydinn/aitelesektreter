import pytest

from did import extract_caller, extract_did


def test_extract_from_trunk_phone_number():
    attrs = [{"sip.trunkPhoneNumber": "08501112233"}]
    assert extract_did(attrs, None) == "08501112233"


def test_falls_back_to_phone_number_key():
    attrs = [{"sip.phoneNumber": "08502223344"}]
    assert extract_did(attrs, None) == "08502223344"


def test_prefers_trunk_over_phone_number():
    attrs = [{"sip.phoneNumber": "x", "sip.trunkPhoneNumber": "08509998877"}]
    assert extract_did(attrs, None) == "08509998877"


def test_falls_back_to_metadata_when_no_attributes():
    assert extract_did([{}], "08507776655") == "08507776655"


def test_raises_when_no_did_anywhere():
    with pytest.raises(RuntimeError):
        extract_did([{}], None)


def test_extract_caller_reads_phone_number():
    assert extract_caller([{"sip.phoneNumber": "+905551112233"}]) == "+905551112233"


def test_extract_caller_returns_none_when_absent():
    assert extract_caller([{"sip.trunkPhoneNumber": "0850x"}]) is None


def test_extract_did_skips_none_and_malformed_attrs():
    # Bir katılımcının attributes'ı None/dict-olmayan olsa bile crash etmez, geçerliyi bulur.
    attrs = [None, "bogus", {"sip.trunkPhoneNumber": "08501112233"}]
    assert extract_did(attrs, None) == "08501112233"


def test_extract_caller_skips_none_attrs():
    assert extract_caller([None, {"sip.phoneNumber": "+905551112233"}]) == "+905551112233"

from prompts import RECORDING_NOTICE, build_system_prompt


def test_prompt_includes_business_name_and_services():
    tenant = {
        "businessName": "Berkay Kuafor",
        "businessHoursText": "Monday 09:00-12:00",
        "services": [{"name": "Saç kesimi"}, {"name": "Sakal"}],
    }
    prompt = build_system_prompt(tenant)
    assert "Berkay Kuafor" in prompt
    assert "Saç kesimi" in prompt and "Sakal" in prompt
    assert "Monday 09:00-12:00" in prompt
    assert "check_availability" in prompt  # araç kullanım talimatı


def test_prompt_handles_empty_services():
    prompt = build_system_prompt({"businessName": "X", "services": []})
    assert "Hizmetler: —" in prompt


def test_prompt_appends_extra_prompt():
    prompt = build_system_prompt({"businessName": "X", "extraPrompt": "Sadece randevu al."})
    assert prompt.endswith("Sadece randevu al.")


def test_recording_notice_mentions_recording():
    assert "kayıt" in RECORDING_NOTICE.lower()

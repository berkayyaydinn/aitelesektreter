"""transcript.py saf yardımcıları — nesne ve sözlük item şekilleri, gürültü filtresi."""
from dataclasses import dataclass, field
from typing import Any

from transcript import count_tool_calls, extract_turns


@dataclass
class FakeItem:
    role: str
    content: Any = None
    tool_calls: list = field(default_factory=list)


def test_extract_turns_object_items():
    items = [
        FakeItem("user", "Merhaba"),
        FakeItem("assistant", "Buyurun?"),
    ]
    turns = extract_turns(items)
    assert turns == [
        {"role": "user", "text": "Merhaba", "occurredAt": None},
        {"role": "assistant", "text": "Buyurun?", "occurredAt": None},
    ]


def test_extract_turns_dict_items_and_list_content():
    items = [
        {"role": "user", "content": ["randevu", "almak istiyorum"]},
        {"role": "system", "content": "yoksay"},
        {"role": "assistant", "content": ""},   # boş → atlanır
    ]
    turns = extract_turns(items)
    assert turns == [{"role": "user", "text": "randevu almak istiyorum", "occurredAt": None}]


def test_extract_turns_empty():
    assert extract_turns(None) == []
    assert extract_turns([]) == []


def test_count_tool_calls():
    items = [
        FakeItem("user", "x"),
        FakeItem("assistant", "y", tool_calls=[{"name": "create_appointment"}]),
        FakeItem("tool", "sonuç"),
    ]
    assert count_tool_calls(items) == 2
    assert count_tool_calls(None) == 0

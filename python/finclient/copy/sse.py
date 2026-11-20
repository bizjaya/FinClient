"""Server-sent events from FinCopy's live stream."""
from __future__ import annotations

from typing import List, NamedTuple, Optional


class StreamEvent(NamedTuple):
    """One event from the FinCopy event stream."""

    event: str
    data: str


class SseParser:
    """Reads event-stream lines one at a time.

    A blank line ends an event; comment lines (``: ping``) are skipped; several ``data:`` lines join with a newline; an
    event without a name is a ``message``; an event with no data line is not delivered; other fields are ignored.
    """

    def __init__(self) -> None:
        self._event: Optional[str] = None
        self._data: List[str] = []

    def add(self, line: str) -> Optional[StreamEvent]:
        """Add one line (without its line break); returns the event the line completed, if any."""
        if not line:
            return self._flush()
        if line.startswith(":"):
            return None
        field, separator, value = line.partition(":")
        if separator and value.startswith(" "):
            value = value[1:]
        if field == "event":
            self._event = value
        elif field == "data":
            self._data.append(value)
        return None

    def complete(self) -> Optional[StreamEvent]:
        """End of stream: an event still being read is returned when it carries data."""
        return self._flush() if self._data else None

    def _flush(self) -> Optional[StreamEvent]:
        item = StreamEvent(self._event or "message", "\n".join(self._data)) if self._data else None
        self._event = None
        self._data = []
        return item

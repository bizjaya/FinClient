"""What the generated operation modules import, in one place."""
from __future__ import annotations

from ._encode import seg
from ._paging import Page, iter_items, iter_pages
from ._service import Group, Service

__all__ = ["Group", "Page", "Service", "iter_items", "iter_pages", "seg"]

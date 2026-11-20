"""What the generated operation modules import, in one place."""
from __future__ import annotations

from ._encode import build_qry, seg
from ._paging import Page, iter_items, iter_offset_pages, iter_qry_pages
from ._service import Group, Service

__all__ = ["Group", "Page", "Service", "build_qry", "iter_items", "iter_offset_pages", "iter_qry_pages", "seg"]

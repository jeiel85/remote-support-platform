#!/usr/bin/env python3
from __future__ import annotations

import sys
from pathlib import Path

from pglast import parse_sql


def main() -> int:
    root = Path(sys.argv[1]).resolve()
    sql = (root / "04-backend/database-schema.sql").read_text(encoding="utf-8")
    statements = parse_sql(sql)
    required = [
        "enable row level security",
        "current_setting('app.tenant_id'",
        "foreign key (session_id, tenant_id)",
        "foreign key (device_id, tenant_id)",
    ]
    lowered = sql.lower()
    missing = [invariant for invariant in required if invariant not in lowered]
    if missing:
        raise AssertionError(f"Database invariants missing: {missing}")
    print(f"Parsed {len(statements)} PostgreSQL statements and verified tenant/RLS invariants")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())


"""Repair the two known history indexes on a new SQLite copy, retaining conflicts."""

import argparse
import collections
from contextlib import closing
import datetime as dt
import hashlib
import json
from pathlib import Path
import re
import sqlite3


TABLES = {
    "trading_days": (("account_key", "server_date"), "updated_at_utc"),
    "trade_excursions": (("account_key", "position_id"), "last_sample_at_utc"),
}
PROTECTED = (
    "accounts", "deals", "trades", "trade_review_documents", "daily_journals",
    "period_reviews", "trade_review_metadata", "structured_trade_plans",
    "plan_items", "chart_objects", "playbook_versions", "trade_rule_assessments",
    "trade_campaigns", "improvement_goals", "opportunity_records",
)


def quote(name):
    return '"' + name.replace('"', '""') + '"'


def read_rows(connection, table):
    return [dict(row) for row in connection.execute(
        f"SELECT rowid AS _repair_rowid, * FROM {quote(table)} NOT INDEXED ORDER BY rowid")]


def timestamp(value):
    value = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    if value.tzinfo is None:
        raise ValueError("History timestamp lacks a timezone")
    return value.astimezone(dt.timezone.utc)


def newest(row, table):
    rank = (timestamp(row[TABLES[table][1]]),)
    if table == "trade_excursions":
        rank += (row["is_complete"], row["covered_milliseconds"])
    return rank + (row["_repair_rowid"],)


def digest(connection, table):
    result = hashlib.sha256()
    for row in connection.execute(f"SELECT * FROM {quote(table)} NOT INDEXED ORDER BY rowid"):
        result.update(json.dumps(list(row), ensure_ascii=False, separators=(",", ":")).encode("utf-8"))
        result.update(b"\n")
    return result.hexdigest()


def fingerprint(connection):
    names = [row[0] for row in connection.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name")]
    counts = {name: connection.execute(f"SELECT count(*) FROM {quote(name)} NOT INDEXED").fetchone()[0]
              for name in names}
    hashes = {name: digest(connection, name) for name in PROTECTED if name in names}
    return counts, hashes


def repair_copy(source_path, output_path, fail_after_table=None):
    source_path, output_path = Path(source_path).resolve(), Path(output_path).resolve()
    if source_path == output_path or output_path.exists():
        raise ValueError("Output must be a new database file; in-place repair is not supported")
    output_path.parent.mkdir(parents=True, exist_ok=True)
    # URI read-only access and SQLite backup capture the WAL consistently.
    with closing(sqlite3.connect(source_path.as_uri() + "?mode=ro", uri=True, timeout=30)) as source:
        with closing(sqlite3.connect(output_path)) as destination:
            source.backup(destination)

    connection = sqlite3.connect(output_path, timeout=30)
    connection.row_factory = sqlite3.Row
    try:
        errors = [row[0] for row in connection.execute("PRAGMA integrity_check")]
        if errors != ["ok"] and any(
            not any("sqlite_autoindex_" + table + "_1" in error for table in TABLES)
            for error in errors
        ):
            raise ValueError("Corruption extends beyond the two supported history indexes")
        before_counts, before_hashes = fingerprint(connection)
        definitions, originals, selected, conflicts = {}, {}, {}, []
        all_tables = list(before_counts)
        for name in all_tables:
            if any(row[2] in TABLES for row in connection.execute(f"PRAGMA foreign_key_list({quote(name)})")):
                raise ValueError("An unexpected foreign key references a history table")
        for table, (keys, _) in TABLES.items():
            schema = connection.execute("SELECT sql FROM sqlite_master WHERE type='table' AND name=?", (table,)).fetchone()
            if schema is None or "WITHOUT ROWID" in schema[0].upper():
                raise ValueError("Unsupported history table schema")
            primary_key = [row[1] for row in sorted(connection.execute(f"PRAGMA table_info({quote(table)})"),
                                                   key=lambda row: row[5]) if row[5] > 0]
            if primary_key != list(keys):
                raise ValueError("Unexpected history primary key")
            if connection.execute("SELECT 1 FROM sqlite_master WHERE type='trigger' AND tbl_name=?", (table,)).fetchone():
                raise ValueError("Unexpected history table trigger")
            indexes = [row[0] for row in connection.execute(
                "SELECT sql FROM sqlite_master WHERE type='index' AND tbl_name=? AND sql IS NOT NULL", (table,))]
            definitions[table] = (schema[0], indexes)
            originals[table] = read_rows(connection, table)
            groups = collections.defaultdict(list)
            for row in originals[table]:
                groups[tuple(row[key] for key in keys)].append(row)
            selected[table] = []
            for rows in groups.values():
                kept = max(rows, key=lambda row: newest(row, table))
                selected[table].append(kept)
                if len(rows) > 1:
                    conflicts.append({"table": table, "kept_rowid": kept["_repair_rowid"], "versions": rows})
            selected[table].sort(key=lambda row: row["_repair_rowid"])

        archive_path = output_path.with_suffix(".conflicts.json")
        if archive_path.exists():
            raise ValueError("Conflict archive already exists")
        archive_path.write_text(json.dumps({
            "source": str(source_path), "policy": "latest timestamp; preserve each chosen row without mixing derived values",
            "conflicts": conflicts,
        }, ensure_ascii=False, indent=2), encoding="utf-8")

        connection.execute("PRAGMA foreign_keys=OFF")
        connection.execute("BEGIN IMMEDIATE")
        try:
            for table in TABLES:
                temporary = "_repair_" + table
                schema, indexes = definitions[table]
                create_sql, replaced = re.subn(
                    r'^CREATE TABLE\s+(?:"' + table + r'"|' + table + r')\s*\(',
                    "CREATE TABLE " + quote(temporary) + " (", schema, count=1, flags=re.IGNORECASE)
                if replaced != 1:
                    raise ValueError("Unsupported table declaration")
                connection.execute(create_sql)
                columns = [row[1] for row in connection.execute(f"PRAGMA table_info({quote(table)})")]
                insert_sql = (f"INSERT INTO {quote(temporary)} (rowid," + ",".join(map(quote, columns)) + ") VALUES (" +
                              ",".join("?" for _ in range(len(columns) + 1)) + ")")
                connection.executemany(insert_sql, [
                    [row["_repair_rowid"]] + [row[column] for column in columns] for row in selected[table]])
                connection.execute(f"DROP TABLE {quote(table)}")
                connection.execute(f"ALTER TABLE {quote(temporary)} RENAME TO {quote(table)}")
                for index in indexes:
                    connection.execute(index)
                if fail_after_table == table:
                    raise RuntimeError("Injected repair interruption")
                if read_rows(connection, table) != selected[table]:
                    raise ValueError("Rebuilt history payload differs from the selected originals")

            if [row[0] for row in connection.execute("PRAGMA integrity_check")] != ["ok"]:
                raise ValueError("Rebuilt database failed integrity check")
            if list(connection.execute("PRAGMA foreign_key_check")):
                raise ValueError("Rebuilt database failed foreign key check")
            after_counts, after_hashes = fingerprint(connection)
            expected_counts = before_counts | {table: len(selected[table]) for table in TABLES}
            if after_counts != expected_counts or before_hashes != after_hashes:
                raise ValueError("Unrelated tables or raw trading/review data changed")
            # Ensure primary-key lookups return the exact canonical row, including recovered keys.
            for table, (keys, _) in TABLES.items():
                columns = [row[1] for row in connection.execute(f"PRAGMA table_info({quote(table)})")]
                sql = "SELECT * FROM " + quote(table) + " WHERE " + " AND ".join(quote(key) + "=?" for key in keys)
                for row in selected[table]:
                    actual = connection.execute(sql, [row[key] for key in keys]).fetchone()
                    if actual is None or dict(actual) != {column: row[column] for column in columns}:
                        raise ValueError("History row remains unreadable through its primary key")
            connection.commit()
        except BaseException:
            connection.rollback()
            raise
        return {
            "integrity": "ok", "foreign_keys": "ok", "conflict_groups_archived": len(conflicts),
            "removed_duplicate_rows": {table: len(originals[table]) - len(selected[table]) for table in TABLES},
            "canonical_rows": {table: len(selected[table]) for table in TABLES},
            "protected_data_unchanged": True, "archive": str(archive_path), "output": str(output_path),
        }
    finally:
        connection.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source")
    parser.add_argument("output", help="New output database; the original is never modified")
    options = parser.parse_args()
    print(json.dumps(repair_copy(options.source, options.output), ensure_ascii=False))

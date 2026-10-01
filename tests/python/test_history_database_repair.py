import hashlib
from contextlib import closing
import importlib.util
from pathlib import Path
import sqlite3
import tempfile
import unittest


spec = importlib.util.spec_from_file_location(
    "history_repair", Path(__file__).resolve().parents[2] / "tools" / "repair_history_database.py")
repair = importlib.util.module_from_spec(spec)
spec.loader.exec_module(repair)


class HistoryRepairTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.source = Path(self.directory.name) / "source.db"
        with closing(sqlite3.connect(self.source)) as connection:
            connection.executescript("""
                CREATE TABLE accounts(account_key TEXT PRIMARY KEY);
                CREATE TABLE trading_days (
                    account_key TEXT NOT NULL, server_date TEXT NOT NULL,
                    settings_json TEXT NOT NULL, daily_state_json TEXT NOT NULL, updated_at_utc TEXT NOT NULL,
                    PRIMARY KEY(account_key,server_date), FOREIGN KEY(account_key) REFERENCES accounts(account_key));
                CREATE TABLE trade_excursions (
                    account_key TEXT NOT NULL, position_id INTEGER NOT NULL, minimum_pnl NUMERIC NOT NULL,
                    maximum_pnl NUMERIC NOT NULL, last_sample_at_utc TEXT NOT NULL,
                    is_complete INTEGER NOT NULL, covered_milliseconds INTEGER NOT NULL,
                    PRIMARY KEY(account_key,position_id));
                CREATE TABLE trades(id INTEGER PRIMARY KEY, payload_json TEXT NOT NULL);
                INSERT INTO accounts VALUES ('Broker|1');
                INSERT INTO trading_days VALUES ('Broker|1','2026-09-01','{}','{"realizedPnl":2.15}', '2026-09-01T12:00:00+00:00');
                INSERT INTO trade_excursions VALUES ('Broker|1',42,-1.25,3.45,'2026-09-01T12:00:00+00:00',1,2000);
                INSERT INTO trades VALUES (42,'{"review":"keep all notes","netPnl":2.15}');
                CREATE INDEX excursion_lookup ON trade_excursions(last_sample_at_utc);
            """)
            connection.commit()

    def tearDown(self):
        self.directory.cleanup()

    def test_copy_preserves_original_payloads_indexes_and_source_file(self):
        source_hash = hashlib.sha256(self.source.read_bytes()).hexdigest()
        output = self.source.with_name("repaired.db")
        result = repair.repair_copy(self.source, output)
        self.assertEqual("ok", result["integrity"])
        self.assertTrue(result["protected_data_unchanged"])
        self.assertEqual(source_hash, hashlib.sha256(self.source.read_bytes()).hexdigest())
        with closing(sqlite3.connect(output)) as connection:
            self.assertEqual([(42, '{"review":"keep all notes","netPnl":2.15}')], connection.execute("SELECT * FROM trades").fetchall())
            self.assertEqual((-1.25, 3.45), connection.execute("SELECT minimum_pnl,maximum_pnl FROM trade_excursions WHERE account_key='Broker|1' AND position_id=42").fetchone())
            self.assertIsNotNone(connection.execute("SELECT name FROM sqlite_master WHERE name='excursion_lookup'").fetchone())
            self.assertEqual([], connection.execute("PRAGMA foreign_key_check").fetchall())

    def test_interruption_rolls_back_both_table_and_schema_changes(self):
        output = self.source.with_name("interrupted.db")
        with self.assertRaisesRegex(RuntimeError, "interruption"):
            repair.repair_copy(self.source, output, fail_after_table="trading_days")
        with closing(sqlite3.connect(self.source)) as source, closing(sqlite3.connect(output)) as interrupted:
            self.assertEqual(source.execute("SELECT name,sql FROM sqlite_master ORDER BY name").fetchall(),
                             interrupted.execute("SELECT name,sql FROM sqlite_master ORDER BY name").fetchall())
            self.assertEqual(source.execute("SELECT * FROM trading_days").fetchall(), interrupted.execute("SELECT * FROM trading_days").fetchall())
            self.assertEqual([("ok",)], interrupted.execute("PRAGMA integrity_check").fetchall())

    def test_refuses_existing_output_and_in_place_repair(self):
        original = self.source.read_bytes()
        with self.assertRaises(ValueError):
            repair.repair_copy(self.source, self.source)
        self.assertEqual(original, self.source.read_bytes())


if __name__ == "__main__":
    unittest.main()

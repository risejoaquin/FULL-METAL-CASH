#!/usr/bin/env python3
"""Read-only AC-DEV UAT: one atomic cash sale, one shift and one close.

Does not infer physical printing or remote sync ACK from local records.
Does not write or repair SQLite. Defaults target QA-001 figures only.
"""
import argparse
from pathlib import Path
import sqlite3
import sys


def require(condition, message):
    if not condition:
        raise ValueError(message)
    print("PASS | " + message)


def one(db, sql, args, label):
    rows = db.execute(sql, args).fetchall()
    require(len(rows) == 1, label + f" (rows={len(rows)})")
    return rows[0]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--db", required=True, type=Path)
    ap.add_argument("--shift-id", default="")
    ap.add_argument("--sku", default="AMERICANO-12")
    ap.add_argument("--opening", type=int, default=10000)
    ap.add_argument("--total", type=int, default=4500)
    ap.add_argument("--tendered", type=int, default=5000)
    ap.add_argument("--counted", type=int, default=14500)
    a = ap.parse_args()
    require(a.db.is_file(), "SQLite file exists")
    require(a.total > 0 and a.tendered >= a.total and a.opening >= 0 and a.counted >= 0, "Valid nonnegative cash figures")
    db = sqlite3.connect(a.db.resolve().as_uri() + "?mode=ro", uri=True)
    db.row_factory = sqlite3.Row
    try:
        require(db.execute("PRAGMA integrity_check").fetchone()[0] == "ok", "SQLite integrity_check")
        sql = """SELECT * FROM local_cash_shifts
                 WHERE status='closed' AND opening_amount_cents=?
                   AND counted_cash_cents=? AND expected_cash_cents=?
                   AND difference_cents=0"""
        params = [a.opening, a.counted, a.counted]
        if a.shift_id:
            sql += " AND id=?"
            params.append(a.shift_id)
        shifts = db.execute(sql + " ORDER BY closed_at_utc DESC", params).fetchall()
        require(bool(shifts), "Matching CLOSED shift with zero difference exists")
        shift = shifts[0]
        sid = shift["id"]
        require(bool(shift["closed_at_utc"] and shift["closed_by_user_id"]), "Closed shift has audit timestamp and operator")
        sale = one(db, "SELECT * FROM local_checkout_commits WHERE cash_shift_id=?", (sid,), "Exactly one checkout on shift")
        sale_id = sale["local_sale_id"]
        require(sale["total_cents"] == a.total and sale["tendered_cents"] == a.tendered
                and sale["change_cents"] == a.tendered - a.total, "Cash total, amount received and change")
        one(db, """SELECT 1 FROM offline_sales WHERE local_sale_id=?
                   AND total_cents=? AND paid_cents=? AND currency='MXN'""",
            (sale_id, a.total, a.total), "Sale persisted once")
        one(db, """SELECT 1 FROM local_checkout_lines
                   WHERE local_sale_id=? AND sku=? AND quantity=1 AND unit_price_cents=?""",
            (sale_id, a.sku, a.total), "SKU and unit price are correct")
        require(db.execute("SELECT COUNT(*) FROM local_checkout_lines WHERE local_sale_id=?",
                           (sale_id,)).fetchone()[0] == 1, "No extra sale lines")
        one(db, """SELECT 1 FROM local_sale_payments WHERE shift_id=? AND local_sale_id=?
                   AND method_code='cash' AND amount_cents=? AND tendered_cents=?
                   AND change_cents=?""",
            (sid, sale_id, a.total, a.tendered, a.tendered-a.total), "Single matching cash payment")
        one(db, """SELECT 1 FROM local_cash_movements
                   WHERE shift_id=? AND movement_type='sale_cash'
                   AND source_type='offline_sale' AND source_id=? AND amount_cents=?""",
            (sid, sale_id, a.total), "Single matching cash movement")
        for table, condition in (
            ("local_sale_payments", ""),
            ("local_cash_movements", " AND movement_type='sale_cash'"),
        ):
            n = db.execute(f"SELECT COUNT(*) FROM {table} WHERE shift_id=?{condition}", (sid,)).fetchone()[0]
            require(n == 1, "No duplicate " + table)
        open_movement = one(db, """SELECT amount_cents FROM local_cash_movements
                               WHERE shift_id=? AND movement_type='opening'""", (sid,), "One opening ledger movement")
        require(open_movement["amount_cents"] == a.opening, "Opening movement amount")
        extra = db.execute("""SELECT
           COALESCE(SUM(CASE WHEN movement_type='cash_in' THEN amount_cents ELSE 0 END),0),
           COALESCE(SUM(CASE WHEN movement_type='cash_out' THEN amount_cents ELSE 0 END),0)
           FROM local_cash_movements WHERE shift_id=?""", (sid,)).fetchone()
        require(a.opening + a.total + extra[0] - extra[1] == a.counted, "Calculated ledger equals counted cash")
        event = one(db, """SELECT status FROM local_outbox_events
                          WHERE id=? AND event_type='sale.completed' AND schema_version=4""",
                    (sale["outbox_event_id"],), "Schema v4 outbox event created once")
        job = one(db, """SELECT status FROM local_print_jobs
                        WHERE id=? AND sale_id=? AND receipt_number=?""",
                  (sale["print_job_id"], sale_id, sale["receipt_number"]), "Print job created once")
        print(f"INFO | shift={sid}; sale={sale_id}; outbox_status={event['status']}; print_status={job['status']}")
        print("NOTE | Print job != physical ticket; outbox event != server ACK")
        print("QA-002 / QA-003: PASS — LOCAL SQL FINANCIAL RECONCILIATION")
    finally:
        db.close()


if __name__ == "__main__":
    try:
        main()
    except (ValueError, sqlite3.Error, OSError) as error:
        print("QA-002 / QA-003: FAIL — " + str(error), file=sys.stderr)
        sys.exit(1)

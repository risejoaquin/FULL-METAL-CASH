#!/usr/bin/env python3
"""Read-only correlation of one WPF SQLite sale with local ACK and PostgreSQL.

Never pushes, requeues, processes, or generates a sale. Run only with the
local, isolated Docker Compose project 'full-metal-cash'.
"""
import argparse
import json
import re
import sqlite3
import subprocess
import sys
import uuid
from pathlib import Path


def cmd(args):
    result = subprocess.run(args, text=True, encoding="utf-8",
                            errors="replace", capture_output=True, check=False)
    if result.returncode:
        raise RuntimeError(f"Command failed ({args[0]}), exit={result.returncode}: "
                           f"{result.stderr.strip()[:550]}")
    return result.stdout.strip()


def row_one(db, sql, params):
    rows = db.execute(sql, params).fetchall()
    if len(rows) != 1:
        raise RuntimeError(f"Expected exactly 1 local row, got {len(rows)}")
    return rows[0]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--db", required=True, type=Path)
    parser.add_argument("--sale-id", required=True)
    opts = parser.parse_args()
    sale_id = str(uuid.UUID(opts.sale_id))

    if not opts.db.is_file():
        raise RuntimeError("SQLite database not found.")
    branch = cmd(["git", "branch", "--show-current"])
    if branch != "AC-DEV":
        raise RuntimeError("Refusing to audit outside AC-DEV")
    compose = json.loads(cmd(["docker", "compose", "config", "--format", "json"]))
    if compose.get("name") != "full-metal-cash":
        raise RuntimeError("Refusing to audit a non-laboratory Compose project")
    running = json.loads(cmd(["docker", "inspect", "solidpos-postgres"]))[0]
    if running["Config"].get("Labels", {}).get("com.docker.compose.project") != "full-metal-cash":
        raise RuntimeError("PostgreSQL container belongs to a different project")
    if not running["State"].get("Running"):
        raise RuntimeError("Local PostgreSQL container is not running")

    with sqlite3.connect(opts.db.resolve().as_uri() + "?mode=ro", uri=True) as db:
        db.row_factory = sqlite3.Row
        commit = row_one(db,
            "SELECT local_sale_id, outbox_event_id, total_cents FROM local_checkout_commits WHERE local_sale_id=?",
            (sale_id,))
        event = row_one(db, """SELECT id, tenant_id, store_id, terminal_id, event_type,
                        schema_version, status, synced_at_utc
                        FROM local_outbox_events WHERE id=?""",
                        (commit["outbox_event_id"],))
        sale = row_one(db, "SELECT local_sale_id, total_cents FROM offline_sales WHERE local_sale_id=?", (sale_id,))
        print(f"LOCAL SALE | {sale_id} | amount={sale['total_cents']} cents")
        print(f"LOCAL EVENT | {event['id']} | status={event['status']} | syncedAt={event['synced_at_utc']}")
        if commit["total_cents"] != sale["total_cents"] or event["event_type"] != "sale.completed" or event["schema_version"] != 4:
            raise RuntimeError("Local checkout and outbox event do not reconcile")
        ack = db.execute("""SELECT batch_id, remote_status, acknowledged_at_utc
                             FROM local_sync_acknowledgements
                             WHERE outbox_event_id=?""", (event["id"],)).fetchall()
        print(f"LOCAL ACKS | {len(ack)}")
        for item in ack:
            print(f"LOCAL ACK | batch={item['batch_id']} | {item['remote_status']} | at={item['acknowledged_at_utc']}")
        tenant = str(uuid.UUID(event["tenant_id"]))
        store = str(uuid.UUID(event["store_id"]))
        terminal = str(uuid.UUID(event["terminal_id"]))
        event_id = str(uuid.UUID(event["id"]))
        if len(ack) != 1 or not event["synced_at_utc"]:
            print("RESULT | LOCAL ACK NOT YET VERIFIED. Do not requeue or resend.")
        else:
            print("PASS | One persisted local ACK for this specific checkout event")

    # Validated UUIDs are the ONLY interpolated parameters in these SELECTs.
    sql = f"""
SELECT json_build_object(
 'inboxCount', (SELECT COUNT(*) FROM pos.sync_inbox_events
    WHERE tenant_id='{tenant}'::uuid AND store_id='{store}'::uuid
      AND terminal_id='{terminal}'::uuid AND event_id='{event_id}'::uuid),
 'inbox', (SELECT json_build_object('status', status, 'eventType', event_type,
      'createdAt', created_at, 'processedAt', processed_at,
      'errorCode', error_code, 'batchId', batch_id)
    FROM pos.sync_inbox_events WHERE tenant_id='{tenant}'::uuid
      AND store_id='{store}'::uuid AND terminal_id='{terminal}'::uuid
      AND event_id='{event_id}'::uuid LIMIT 1),
 'saleCount', (SELECT COUNT(*) FROM pos.sales
    WHERE tenant_id='{tenant}'::uuid AND store_id='{store}'::uuid
      AND terminal_id='{terminal}'::uuid AND local_sale_id='{sale_id}'::uuid),
 'sale', (SELECT json_build_object('id', id, 'status', status,
       'totalCents', total_cents, 'paidCents', paid_cents)
    FROM pos.sales WHERE tenant_id='{tenant}'::uuid AND store_id='{store}'::uuid
      AND terminal_id='{terminal}'::uuid AND local_sale_id='{sale_id}'::uuid LIMIT 1)
)::text;
"""
    raw = cmd(["docker", "compose", "exec", "-T", "postgres", "psql",
               "-X", "-A", "-t", "-v", "ON_ERROR_STOP=1", "-U", "solidpos",
               "-d", "solidpos", "-c", sql])
    result = json.loads(raw)
    print(f"SERVER INBOX | count={result['inboxCount']} | {result['inbox']}")
    print(f"SERVER SALE | count={result['saleCount']} | {result['sale']}")
    if result["inboxCount"] != 1:
        print("RESULT | NO MATCHING SERVER EVENT. Do not resubmit until investigated.")
        return 2
    if result["inbox"]["status"] != "processed":
        print(f"RESULT | ACK/INGESTION ONLY, SERVER STATUS={result['inbox']['status']}. "
              "Do not claim server financial processing yet.")
        return 3
    sale_remote = result["sale"]
    if result["saleCount"] != 1 or not sale_remote or sale_remote["totalCents"] != commit["total_cents"]:
        print("RESULT | SERVER PROCESSING/SUCCESS IS NOT FULLY RECONCILED")
        return 4
    if len(ack) != 1:
        print("RESULT | SERVER SALE EXISTS, BUT LOCAL ACK TRACE IS MISSING")
        return 5
    print("QA-004 | PASS: EXACT EVENT INGESTED + PROCESSED; ONE MATCHING POSTGRESQL SALE")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (ValueError, RuntimeError, sqlite3.Error, OSError, json.JSONDecodeError) as exc:
        print("QA-004 | CANNOT VERIFY: " + str(exc), file=sys.stderr)
        sys.exit(6)

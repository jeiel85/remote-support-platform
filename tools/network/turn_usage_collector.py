#!/usr/bin/env python3
"""Durably forwards coturn Redis total-traffic events to the signed API boundary."""

from __future__ import annotations

import base64
import datetime as dt
import hashlib
import hmac
import json
import os
import re
import socket
import sqlite3
import ssl
import time
import urllib.request
import uuid
from pathlib import Path

TRAFFIC = re.compile(r"^rcvp=(\d+), rcvb=(\d+), sentp=(\d+), sentb=(\d+)$")
CHANNEL = re.compile(
    r"^turn/realm/[^/]{1,127}/user/(?P<username>\d{1,12}:[A-Za-z0-9_-]{24})/"
    r"allocation/(?P<allocation>\d{18})/total_traffic$"
)


def required(name: str) -> str:
    value = os.environ.get(name, "")
    if not value:
        raise RuntimeError(f"{name} is required")
    return value


def read_secret(path_name: str) -> str:
    value = Path(required(path_name)).read_text(encoding="ascii").strip()
    if not value or "\n" in value or "\r" in value:
        raise RuntimeError(f"{path_name} did not contain one secret line")
    return value


def parse_event(channel: str, message: str, region: str, transport: str, node_id: str) -> bytes:
    channel_match = CHANNEL.fullmatch(channel)
    traffic_match = TRAFFIC.fullmatch(message)
    if not channel_match or not traffic_match:
        raise ValueError("unrecognized coturn total-traffic event")
    now = dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z")
    event_id = uuid.uuid5(uuid.NAMESPACE_URL, f"rsp-turn:{node_id}:{channel}:{message}")
    payload = {
        "eventId": str(event_id),
        "username": channel_match.group("username"),
        "region": region,
        "transport": transport,
        "nodeId": node_id,
        "bytesFromClient": int(traffic_match.group(2)),
        "bytesToClient": int(traffic_match.group(4)),
        "startedAt": now,
        "endedAt": now,
    }
    return json.dumps(payload, separators=(",", ":"), sort_keys=True).encode("utf-8")


def redis_command(*parts: str) -> bytes:
    encoded = [part.encode("utf-8") for part in parts]
    return b"*%d\r\n" % len(encoded) + b"".join(
        b"$%d\r\n%s\r\n" % (len(part), part) for part in encoded
    )


def read_resp(stream):
    prefix = stream.read(1)
    if not prefix:
        raise ConnectionError("Redis connection closed")
    line = stream.readline()
    if not line.endswith(b"\r\n"):
        raise ConnectionError("Malformed Redis response")
    value = line[:-2]
    if prefix == b"+":
        return value
    if prefix == b"-":
        raise ConnectionError(value.decode("utf-8", "replace"))
    if prefix == b":":
        return int(value)
    if prefix == b"$":
        length = int(value)
        if length < 0:
            return None
        payload = stream.read(length)
        if len(payload) != length or stream.read(2) != b"\r\n":
            raise ConnectionError("Truncated Redis bulk string")
        return payload
    if prefix == b"*":
        return [read_resp(stream) for _ in range(int(value))]
    raise ConnectionError("Unsupported Redis response")


def post_usage(body: bytes, endpoint: str, key: bytes) -> None:
    timestamp = str(int(time.time()))
    signature = base64.urlsafe_b64encode(
        hmac.new(key, timestamp.encode("ascii") + b"\n" + body, hashlib.sha256).digest()
    ).rstrip(b"=").decode("ascii")
    request = urllib.request.Request(endpoint, data=body, method="POST", headers={
        "Content-Type": "application/json",
        "X-RSP-Turn-Timestamp": timestamp,
        "X-RSP-Turn-Signature": signature,
    })
    with urllib.request.urlopen(request, timeout=10) as response:
        if response.status // 100 != 2:
            raise RuntimeError(f"usage endpoint returned {response.status}")


def drain(connection: sqlite3.Connection, endpoint: str, key: bytes) -> None:
    for event_id, body in connection.execute("select event_id, body from pending order by inserted_at limit 100"):
        post_usage(body, endpoint, key)
        connection.execute("delete from pending where event_id = ?", (event_id,))
        connection.commit()


def run() -> None:
    region = required("TURN_REGION")
    transport = required("TURN_TRANSPORT").upper()
    if transport not in {"UDP", "TCP", "TLS"}:
        raise RuntimeError("TURN_TRANSPORT must be UDP, TCP, or TLS")
    node_id = required("TURN_NODE_ID")
    endpoint = required("TURN_USAGE_ENDPOINT")
    metering_key = base64.b64decode(read_secret("TURN_METERING_KEY_FILE"), validate=True)
    if len(metering_key) < 32:
        raise RuntimeError("TURN metering key must contain at least 256 bits")
    redis_password = read_secret("TURN_REDIS_PASSWORD_FILE")
    redis_host = required("TURN_REDIS_HOST")
    redis_port = int(os.environ.get("TURN_REDIS_PORT", "6379"))
    database = Path(os.environ.get("TURN_USAGE_SPOOL", "/var/lib/rsp-turn-usage/spool.db"))
    database.parent.mkdir(parents=True, exist_ok=True)
    connection = sqlite3.connect(database)
    connection.execute("create table if not exists pending(event_id text primary key, body blob not null, inserted_at integer not null)")
    connection.commit()

    while True:
        try:
            drain(connection, endpoint, metering_key)
            raw_socket = socket.create_connection((redis_host, redis_port), timeout=10)
            if os.environ.get("TURN_REDIS_TLS", "true").lower() == "true":
                raw_socket = ssl.create_default_context().wrap_socket(raw_socket, server_hostname=redis_host)
            stream = raw_socket.makefile("rb")
            raw_socket.sendall(redis_command("AUTH", redis_password))
            if read_resp(stream) != b"OK":
                raise ConnectionError("Redis authentication failed")
            raw_socket.sendall(redis_command("PSUBSCRIBE", "turn/realm/*/user/*/allocation/*/total_traffic"))
            _ = read_resp(stream)
            while True:
                event = read_resp(stream)
                if not isinstance(event, list) or len(event) != 4 or event[0] != b"pmessage":
                    continue
                channel = event[2].decode("utf-8")
                message = event[3].decode("ascii")
                body = parse_event(channel, message, region, transport, node_id)
                event_id = json.loads(body)["eventId"]
                connection.execute("insert or ignore into pending values(?,?,?)", (event_id, body, int(time.time())))
                connection.commit()
                drain(connection, endpoint, metering_key)
        except (OSError, ValueError, RuntimeError, ConnectionError):
            time.sleep(2)


if __name__ == "__main__":
    run()

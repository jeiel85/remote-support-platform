#!/usr/bin/env python3
from pathlib import Path
import json
import sys

root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
sys.path.insert(0, str(root / "tools/network"))
from turn_usage_collector import parse_event  # noqa: E402
config = (root / "deploy/coturn/turnserver.conf.template").read_text(encoding="utf-8")
lock = (root / "deploy/coturn/SOURCE.lock").read_text(encoding="utf-8")
server = (root / "src/server/RemoteSupport.Server/Program.cs").read_text(encoding="utf-8")
peer_access = (root / "src/server/RemoteSupport.Server/PeerAccessService.cs").read_text(encoding="utf-8")
native = (root / "src/client/native/src/transport.cpp").read_text(encoding="utf-8")
signaling_edge = (root / "deploy/signaling/nginx.conf.template").read_text(encoding="utf-8")

required_config = [
    "use-auth-secret", "secure-stun", "no-cli", "no-multicast-peers",
    "denied-peer-ip=10.0.0.0-10.255.255.255", "no-tlsv1", "no-tlsv1_1",
    "user-quota={{USER_QUOTA}}", "total-quota={{TOTAL_QUOTA}}",
    "max-bps={{MAX_BPS}}", "bps-capacity={{BPS_CAPACITY}}", "prometheus", "{{LISTENER_POLICY}}",
    "{{EXTRA_DENIED_PEERS}}",
]
missing = [value for value in required_config if value not in config]
if missing:
    raise SystemExit(f"coturn hardening is incomplete: {missing}")
if "allow-loopback-peers" in config or "no-auth" in config or "prometheus-username-labels" in config:
    raise SystemExit("coturn contains a forbidden peer/auth/metrics setting")
if "version=4.14.0" not in lock or "image_index_digest=sha256:" not in lock:
    raise SystemExit("coturn source/image pin is missing")
if 'MapGet("/v1/signaling"' not in server or 'turn-credentials"' not in server or 'DPOP_PROOF_REPLAYED' not in peer_access:
    raise SystemExit("Goal 07 server routes are missing")
if "RTC_TRANSPORT_POLICY_RELAY" not in native:
    raise SystemExit("native forced-relay test policy is missing")
if "hash $rsp_session_shard consistent" not in signaling_edge or "access_log off" not in signaling_edge:
    raise SystemExit("signaling edge shard routing or secret-log suppression is missing")
if "static-auth-secret=" in "\n".join(
    line for line in config.splitlines() if "{{TURN_SHARED_SECRET}}" not in line
):
    raise SystemExit("a static TURN credential was committed")
fixture = json.loads(parse_event(
    "turn/realm/turn.example/user/1783000000:abcdefghijklmnopqrstuvwx/allocation/000000000000000001/total_traffic",
    "rcvp=1, rcvb=1000, sentp=2, sentb=2000", "ap-northeast-2", "TLS", "turn-a"
))
if fixture["bytesFromClient"] != 1000 or fixture["bytesToClient"] != 2000 or fixture["transport"] != "TLS":
    raise SystemExit("TURN usage collector fixture failed")
print("Verified Goal 07 signaling, TURN hardening, immutable pin, quotas, and relay-only probe contract")

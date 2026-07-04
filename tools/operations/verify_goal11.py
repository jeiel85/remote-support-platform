#!/usr/bin/env python3
from __future__ import annotations

import json
import sys
from pathlib import Path

import yaml

sys.path.insert(0, str(Path(__file__).parent))
from evaluate_update_rollout import evaluate  # noqa: E402


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    rules_path = root / "deploy/observability/prometheus-rules.yaml"
    rules = yaml.safe_load(rules_path.read_text(encoding="utf-8"))
    found: dict[str, dict[str, object]] = {}
    for group in rules["groups"]:
        for rule in group["rules"]:
            found[rule["alert"]] = rule
    required_alerts = {
        "ControlApiFastBurn", "ControlApiSlowBurn", "SessionAuthorizationFailure", "SignalingConnectFailure",
        "TurnAllocationFailure", "TurnPortExhaustion", "TurnNicSaturation", "AuditPipelineStalled",
        "UpdateFailureSpike", "SigningAnomaly", "CrossTenantAccessSignal",
    }
    require(required_alerts == set(found), f"alert catalog mismatch: {required_alerts ^ set(found)}")
    for name, rule in found.items():
        runbook = root / str(rule["annotations"]["runbook"])
        require(runbook.is_file(), f"{name} runbook is missing: {runbook}")
    rule_text = rules_path.read_text(encoding="utf-8").lower()
    for forbidden in ("tenant_id", "session_id", "user_email", "device_name"):
        require(forbidden not in rule_text, f"unbounded metric label is forbidden: {forbidden}")

    dashboard = json.loads((root / "deploy/observability/grafana-dashboard.json").read_text(encoding="utf-8"))
    titles = {panel["title"] for panel in dashboard["panels"]}
    require({"Control API success", "Connection and signaling", "TURN allocation and capacity", "Crash-free sessions",
             "Update rollout", "Audit and security"} <= titles, "dashboard coverage is incomplete")
    variables = {item["name"] for item in dashboard["templating"]["list"]}
    require(not variables & {"tenant", "tenant_id", "session", "device"}, "dashboard exposes a high-cardinality identity variable")

    collector = (root / "deploy/observability/otel-collector.yaml").read_text(encoding="utf-8")
    for key in ("authorization", "enduser.email", "session.sdp", "clipboard.content", "chat.content", "file.content"):
        require(f"key: {key}" in collector, f"collector privacy deletion is missing: {key}")
    updater = (root / "src/client/managed/RemoteSupport.Security/SecureUpdateCoordinator.cs").read_text(encoding="utf-8")
    for invariant in ("HighestSeenSequence", "ProbeHealthAsync", "RollbackAsync", "UPDATE_ROLLBACK_BLOCKED"):
        require(invariant in updater, f"updater invariant is missing: {invariant}")
    bundle = (root / "src/client/managed/RemoteSupport.Observability/SupportBundleBuilder.cs").read_text(encoding="utf-8")
    require("SupportBundleApproval" in bundle and "uploadPerformed = false" in bundle, "support bundle approval boundary is missing")
    healthy = {"currentPercentage": 5, "attemptedInstallations": 1000, "successfulInstallations": 998,
               "sessions": 1000, "crashes": 2, "bootLoops": 0, "signatureFailures": 0, "observationMinutes": 120}
    evaluate(healthy, 25)
    evaluate({**healthy, "currentPercentage": 25}, 0, halt=True)
    try:
        evaluate({**healthy, "signatureFailures": 1}, 25)
    except AssertionError:
        pass
    else:
        raise AssertionError("bad canary evidence did not stop rollout promotion")
    drill = json.loads((root / "07-delivery/evidence/goal-11-drill.json").read_text(encoding="utf-8"))
    require(drill["restore"]["integrity"] == "PASS" and drill["restore"]["measuredRpoRecords"] == 0,
            "committed restore drill evidence is invalid")
    require(all(drill[name]["result"] == "PASS" for name in ("turnReplacement", "alerts", "privacy")),
            "committed Goal 11 drill evidence is incomplete")
    print("Verified Goal 11 updater, bounded observability, alert/runbook linkage, dashboard, and support-bundle privacy boundary")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

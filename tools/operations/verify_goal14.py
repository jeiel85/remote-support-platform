#!/usr/bin/env python3
from __future__ import annotations

import sys
from pathlib import Path


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def read(path: Path) -> str:
    require(path.is_file(), f"expected file is missing: {path}")
    return path.read_text(encoding="utf-8")


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()

    threat_model = read(root / "05-security/unattended-threat-model.md")
    require("two-party" in threat_model.lower() or "Two-party" in threat_model, "dedicated threat model is missing the two-party enrollment control")

    policy_engine = read(root / "src/server/RemoteSupport.Server/GovernancePolicyEngine.cs")
    for invariant in ("UNATTENDED_REQUIRES_DEVICE", "UNATTENDED_NOT_ENABLED_FOR_DEVICE",
                       "UNATTENDED_SESSION_SCOPE_REQUIRED", "STEP_UP_MFA_REQUIRED", "UNATTENDED_ACCESS"):
        require(invariant in policy_engine, f"unattended policy invariant is missing: {invariant}")

    governance = read(root / "src/server/RemoteSupport.Server/GovernanceService.cs")
    for invariant in ("RequestUnattendedEnrollment", "ConfirmUnattendedEnrollment", "RevokeUnattendedEnrollment",
                       "UNATTENDED_ACCESS"):
        require(invariant in governance, f"unattended enrollment invariant is missing: {invariant}")
    require('"VIEW_SCREEN", "REMOTE_INPUT", "CLIPBOARD_TEXT", "FILE_TRANSFER", "CHAT", "MULTI_MONITOR",' in governance,
            "default tenant feature list must not include UNATTENDED_ACCESS (disabled by default)")

    session_service = read(root / "src/server/RemoteSupport.Server/AttendedSessionService.cs")
    require('"MANAGED_ATTENDED" or "UNATTENDED"' in session_service,
            "managed-host-decision must accept UNATTENDED sessions")
    require("TerminateSessionsForDevice" in session_service, "device-revocation session cascade is missing")

    program = read(root / "src/server/RemoteSupport.Server/Program.cs")
    for path in ("unattended-enrollment-requests", "unattended-enrollment-confirmations", "TerminateSessionsForDevice"):
        require(path in program, f"unattended enrollment endpoint wiring is missing: {path}")

    orchestrator = read(root / "src/client/managed/RemoteSupport.ManagedHost.Service/ManagedHostOrchestrator.cs")
    require("isUnattended" in orchestrator and "approved = true" in orchestrator,
            "orchestrator does not auto-approve unattended sessions independent of local notification result")

    agent_csproj = read(root / "src/client/managed/RemoteSupport.Agent.App/RemoteSupport.Agent.App.csproj")
    require("RemoteSupport.ManagedHost.Service" not in agent_csproj,
            "the portable Agent must not reference the managed-host unattended capability")

    for evidence_path in ("07-delivery/evidence/goal-14.md",):
        require((root / evidence_path).is_file(), f"Goal 14 evidence document is missing: {evidence_path}")

    print("Verified Goal 14 unattended policy gates, two-party enrollment, device-revocation session cascade, "
          "and portable-agent exclusion")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

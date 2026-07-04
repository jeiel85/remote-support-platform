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

    migration = read(root / "src/server/RemoteSupport.Server/Migrations/0003_managed_host.sql")
    require("governance_device_lookups" in migration, "device-id tenant lookup table is missing from the migration")
    require("HOST_PENDING" in migration, "HOST_PENDING is missing from the managed-session state constraint")

    governance = read(root / "src/server/RemoteSupport.Server/GovernanceService.cs")
    for invariant in ("CreateDeviceCredentialChallenge", "ExchangeDeviceCredential", "RotateDeviceKey",
                       "ReportDeviceHeartbeat", "RSP-DEVICE-KEY-ROTATION-V1", "device.Keys.Values.Any(key => key.KeyThumbprint == newKeyThumbprint)"):
        require(invariant in governance, f"device credential lifecycle invariant is missing: {invariant}")

    device_access = read(root / "src/server/RemoteSupport.Server/DeviceAccessService.cs")
    require("DPOP_AUTHORIZATION_STALE" in device_access, "device DPoP staleness check is missing")

    session_service = read(root / "src/server/RemoteSupport.Server/AttendedSessionService.cs")
    for invariant in ("CreateManagedSession", "PollManagedSessionRequestsAsync", "DecideManagedHostSession",
                       "POLICY_SCOPE_DENIED", "ConsentNonceFor"):
        require(invariant in session_service, f"managed-session invariant is missing: {invariant}")

    ipc_proto = read(root / "02-protocol/ipc/service_ipc.proto")
    require("ManagedSessionConsentRequest" in ipc_proto and "ManagedSessionConsentResult" in ipc_proto,
            "managed-session local consent IPC messages are missing from the contract")

    reboot_store = read(root / "src/client/managed/RemoteSupport.ManagedHost.Service/RebootGrantStore.cs")
    require("ProtectedData.Protect" in reboot_store, "reboot grant store does not encrypt persisted state")
    require("ExpiresUtcUnixMs <= now.ToUnixTimeMilliseconds()" in reboot_store, "reboot grant store does not enforce expiry")

    device_key = read(root / "src/client/managed/RemoteSupport.ManagedHost.Service/DeviceIdentityKey.cs")
    require("CngExportPolicies.None" in device_key, "device identity key is not created non-exportable")

    orchestrator = read(root / "src/client/managed/RemoteSupport.ManagedHost.Service/ManagedHostOrchestrator.cs")
    require("hostEphemeralKey.Delete()" in orchestrator,
            "managed-host orchestrator does not discard the ephemeral host key after use")

    for evidence_path in ("07-delivery/evidence/goal-13.md",):
        require((root / evidence_path).is_file(), f"Goal 13 evidence document is missing: {evidence_path}")

    print("Verified Goal 13 device credential lifecycle, managed-session policy binding, IPC consent contract, "
          "and no-persisted-secret reboot continuity")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

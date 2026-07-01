# Product Scope and Release Trains

## Product intent

A Windows-first commercial remote-support platform with explicit user visibility, strong identity separation, metadata-only SaaS control plane by default, and separately gated managed/unattended capabilities.

## Release A — Engineering foundation

Contracts, Windows capture/render/encode, LAN transport, input topology, and security spikes. Not distributed to customers.

## Release B — Attended Beta

Portable host Agent, authenticated Operator Console, one-time support code, explicit signed host consent, screen/input, text clipboard, file transfer, chat, ICE/TURN, mandatory signed transport binding, signed packages, telemetry and abuse reporting.

## Release C — Attended GA

Multi-tenant policy/audit, Admin Portal, production operations, accessibility/localization, signed update chain, commercial/legal review, external security testing, and all `ATTENDED_GA` plus applicable `ALL_RELEASES` gates. No Windows Service and no unattended access.

## Release D — Managed Host

Installed Windows Service, device enrollment/key lifecycle, authenticated host command delivery, managed attended sessions, reboot continuity, device health and centrally revocable management. It has an independent release gate and does not automatically permit unattended sessions.

## Release E — Unattended GA

MFA- and policy-gated unattended access with local deployment authorization, notification/disclosure, abuse controls, incident readiness, legal/privacy approval, and a distinct external security review.

## Explicit exclusions from initial attended GA

Session recording, audio/USB/printer redirection, macOS/Linux hosts, mobile control, browser-only host capture, hidden operation, generic remote shell, credential harvesting, secure-desktop bypass, and any mechanism intended to conceal remote access.

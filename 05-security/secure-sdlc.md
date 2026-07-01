# Secure Software Development Lifecycle

## 1. Baselines

- OWASP ASVS 5.0 Level 2 for control-plane web/API controls.
- NIST SSDF practices for organizational secure development.
- Threat modeling for each new privileged or content-bearing feature.
- Native-code security gates beyond ordinary web application controls.

## 2. Pipeline gates

On every merge:

- formatting and static analysis;
- unit and contract tests;
- secret scanning;
- dependency and license scan;
- SAST;
- native compiler warnings as errors;
- SBOM generation.

On release candidates:

- integration and tenant isolation tests;
- DAST/API security tests;
- native ASan/UBSan or platform-equivalent test build;
- protocol/file parser fuzzing;
- installer/update validation;
- signed artifact verification;
- AV/EDR compatibility scan;
- Windows compatibility matrix.

Before GA and major security releases:

- independent penetration test;
- threat-model review;
- incident-response exercise;
- dependency risk review;
- legal/privacy review.

## 3. Vulnerability handling

- published security contact and disclosure policy;
- severity and exploitability triage;
- fix/mitigation owner and deadline by policy;
- coordinated release and customer advisory;
- CVE request when appropriate;
- retrospective and test addition.

## 4. Native fuzz targets

- DataChannel envelope parser;
- Protobuf message limits;
- file manifest/chunk validation;
- image/cursor metadata parser;
- signaling SDP/candidate validation adapter;
- updater metadata parser;
- IPC message parser.

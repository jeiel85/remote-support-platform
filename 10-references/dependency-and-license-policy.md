# Dependency and License Policy

## 1. Approval criteria

- maintained project and responsive security process;
- compatible license for intended commercial distribution;
- no hidden copyleft obligations incompatible with product distribution model;
- reproducible source/package and integrity verification;
- known CVEs reviewed;
- native ABI and platform support validated;
- replacement or fork plan for critical dependency.

## 2. High-risk dependencies

Require explicit owner and review:

- native WebRTC;
- coturn image/source;
- media codec/encoder wrappers;
- cryptographic library;
- installer/updater framework;
- identity/OIDC client;
- serialization/parser libraries.

## 3. Inventory fields

- name/version/commit;
- source URL;
- package hash;
- license and notices;
- transitive dependencies;
- owner;
- security update cadence;
- production usage/module;
- last review date.

## 4. Distribution

Produce third-party notices and source-offer materials where licenses require them. Do not assume “open source” means unrestricted redistribution or modification.

## Media codec review

H.264 support is a technical baseline, not a conclusion that commercial distribution is royalty-free. The dependency inventory must identify whether encoding/decoding is supplied by Windows, WebRTC, a GPU driver, or a redistributed library. Legal review records patent/licensing conclusions for the actual product, territories, revenue model, and binary distribution. Adding AV1, VP9, HEVC, FFmpeg, OpenH264, or a vendor SDK requires a fresh review of license terms, patent claims, notices, update duties, and export posture.

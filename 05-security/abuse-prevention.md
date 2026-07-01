# Abuse Prevention and Trust

Remote-support products are frequently abused through social engineering. Abuse prevention is a product requirement.

## 1. Tenant onboarding

- verified email/domain for commercial tenants;
- risk-based manual review for high-volume or consumer-targeting tenants;
- payment and identity signals consistent with privacy rules;
- acceptable-use acknowledgment;
- initial limits on sessions, relay bandwidth and file transfer.

## 2. Runtime signals

- excessive code-resolution attempts;
- many hosts from one operator/source in short intervals;
- high rejection rate;
- unusual relay geography;
- repeated executable transfer attempts;
- account/device changes followed by unattended sessions;
- session duration/volume anomalies;
- abuse reports.

Signals create review cases; they do not expose session content.

## 3. Host trust UX

- verified organization badge only after verification;
- operator and organization displayed before consent;
- explicit requested scopes;
- anti-scam warning in consumer attended mode;
- one-click report and disconnect;
- support code expiry visible;
- no operator-supplied arbitrary HTML or branding in consent dialog.

## 4. Enforcement

- temporary session block;
- account step-up challenge;
- tenant rate reduction;
- operator suspension;
- tenant suspension;
- credential/device revocation;
- preservation of minimal audit evidence under policy;
- appeal/review workflow.

## 5. Internal access

Platform employees cannot initiate customer sessions by default. Break-glass access is time-bound, approved, logged and visible to tenant administrators where contractually appropriate.

# Key and Secret Management

## 1. Key classes

| Key | Lifetime | Protection |
|---|---|---|
| portable session key | one session | memory, zero on exit |
| installed device key | device enrollment life | Windows key store/non-exportable where feasible |
| TLS key | certificate lifecycle | managed secret/certificate system |
| TURN REST secret | rotated regularly | secret manager, region-scoped |
| token signing key | planned rotation | HSM/managed KMS preferred |
| update online key | release role lifecycle | hardware/managed signing service |
| update offline/root key | long, rarely used | offline or strong hardware isolation |
| Authenticode key | certificate lifecycle | trusted managed/hardware signing |

## 2. Rotation

- keys have IDs/versions;
- verification accepts bounded overlap;
- generation and activation are separate steps;
- rollback/retirement is explicit;
- compromised key revocation has a runbook;
- device key rotation re-proves possession and updates policy state.

## 3. Secret hygiene

- no secrets in repository, CI variables visible to untrusted jobs or build logs;
- production secrets are environment-specific;
- developer local secrets use local secret store and templates;
- crash dumps and telemetry scrub token-like values;
- support tools never display private keys.

## 4. Update signing separation

A code-signing certificate alone is not the entire updater trust model. Artifact publisher identity and release authorization are verified separately. Storage/CDN credentials cannot create an accepted update without signing authority.

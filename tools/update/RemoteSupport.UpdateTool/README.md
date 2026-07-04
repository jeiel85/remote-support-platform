# Update metadata and release verifier

The tool emits Ed25519 threshold-role documents one signature at a time and
performs the same root/manifest/artifact checks as the production updater.
Production `sign` runs only in a protected job that injects
`RS_UPDATE_SIGNING_KEY_BASE64URL`; the repository, ordinary build jobs and
untrusted pull requests never receive that value. Combine independently
produced signatures with `combine --inputs first.json;second.json --output
threshold.json`; the command rejects different payloads and duplicate key IDs.
`keygen-development` exists only for local deterministic tests.

The `verify` command is the clean-worker release verifier. It requires a
trusted root, signed manifest, exact immutable artifact and expected binding
values, and additionally asks Windows to validate the Authenticode chain.

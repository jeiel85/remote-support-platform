# Secure updater

`RemoteSupport.Updater` is the fail-closed Windows update runner. `apply`
validates the embedded/bootstrap update root, threshold-signed manifest,
product/channel/architecture binding, expiry, staged-rollout cohort, monotonic
release sequence, exact artifact size/SHA-256 and Windows Authenticode trust.
It then asks the setup executable to stage an atomic install, runs the product
smoke health check, and commits or restores the last-known-good directory.

The local update state keeps `highestSeenSequence` separate from the active
release. A failed canary may restore the previous binary, but cannot lower the
security floor or make the same/older manifest eligible again. Invoke
`recover` before checking for another update when startup detects a pending
transaction. Neither command downloads metadata over a non-HTTPS transport;
the product fetch layer is responsible for bounded HTTPS downloads into a
private staging directory before invoking this runner.

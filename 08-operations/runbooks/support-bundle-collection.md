# Runbook — Support Bundle Collection

1. Ask the user/admin to open the local preview and review the exact included files and excluded sensitive categories.
2. Generate only after the preview ID receives explicit approval. A changed snapshot requires a new preview and approval.
3. Confirm `privacy.json` says `uploadPerformed: false`; generation never uploads automatically.
4. Transfer through the approved support channel only after the user/admin separately authorizes upload.
5. Restrict access to the ticket team, record purpose and expiry, and delete at the support-retention deadline.

Bundles contain product/runtime metadata, bounded connection health and stable
error codes. They exclude bearer/enrollment/TURN credentials, emails/device
names, SDP/IP details, screen/keystroke/clipboard/chat/file content and raw
crash dumps. Do not add arbitrary log files to bypass this allowlist.

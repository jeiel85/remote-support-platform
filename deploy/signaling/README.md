# Signaling edge deployment

The 60-second ticket begins with a 16-character opaque, server-keyed session
shard followed by a random 256-bit authenticator. The shard is routing metadata
only and grants nothing; the worker atomically consumes and validates the full
ticket from PostgreSQL-backed session state.

Render `nginx.conf.template` with two or more private signaling worker
addresses. Consistent hashing keeps both peers for one session on the same
worker without exposing a session ID. Query logging is disabled so the ticket
does not enter access logs. Restrict worker ingress to the edge, terminate TLS
1.2/1.3 at the edge, and preserve the original host/protocol used by DPoP REST
endpoints. The WebSocket ticket itself authenticates the upgrade.

On worker loss, an established WebRTC connection remains active. Both peers
request fresh tickets; consistent hashing selects the current healthy ring.
The persisted per-peer signaling sequence continues, while a media failure
uses the separate reconnect-grant/epoch flow.

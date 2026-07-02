# TURN Service

## Deployment

TURN is deployed outside the application pod/network path because its traffic and failure profile differ from REST APIs.

Each node has:

- dedicated public IP;
- restricted relay UDP port range;
- security group/firewall denying private/control-plane destinations;
- protected metrics endpoint;
- immutable image and minimal host OS;
- automated config validation;
- no persistent customer content.

## Credential generation

Use short-lived credentials derived from a server-side secret or supported OAuth method. Username encodes expiry and opaque session/tenant reference without sensitive identifiers. Credential lifetime is slightly longer than expected negotiation and renewed only for active authorized sessions.

The implemented credential is valid for at most ten minutes and never exceeds
the peer token or session expiry. Issuance requires a current DPoP-bound peer
token. Username format is `<unix-expiry>:<24-char-opaque-id>` and coturn REST
password format is `base64(HMAC-SHA1(shared-secret, username))`. Persistence
keeps only the opaque ID and issuance dimensions, never the password or shared
secret.

## Configuration requirements

- authenticated TURN only;
- realm set consistently;
- no loopback/multicast/private peer relay;
- TLS certificate automation and expiry monitoring;
- disabled web admin by default;
- explicit listening/relay IPs;
- log sanitization and rotation;
- quotas and bandwidth limits aligned with product plan;
- patched coturn version and CVE gate.

## Metrics

- allocations active/created/failed;
- bytes and packets relayed by protocol/region;
- auth failures;
- allocation duration;
- CPU, memory, NIC, packet drops and socket errors;
- relay port utilization;
- source/tenant anomaly rates.

Prometheus username labels stay disabled. coturn publishes final allocation
traffic through protected Redis; `tools/network/turn_usage_collector.py`
spools events in SQLite before sending them to the HMAC-authenticated internal
usage endpoint. The API joins the opaque username to session/tenant and stores
idempotent, bounded region/node/transport byte dimensions.

## Scaling trigger examples

Scale before any sustained condition:

- NIC > 60–70% of validated safe throughput;
- CPU softirq/user load beyond tested headroom;
- packet drop/error above threshold;
- relay port allocation pressure;
- p95 allocation latency or failure increase.

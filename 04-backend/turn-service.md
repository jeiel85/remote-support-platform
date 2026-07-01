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

## Scaling trigger examples

Scale before any sustained condition:

- NIC > 60–70% of validated safe throughput;
- CPU softirq/user load beyond tested headroom;
- packet drop/error above threshold;
- relay port allocation pressure;
- p95 allocation latency or failure increase.

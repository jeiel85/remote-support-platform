# Hardened regional coturn deployment

This directory pins coturn 4.14.0 by immutable multi-platform image digest and
records the corresponding signed release tag commit. Before promotion, the
image digest must pass the repository CVE gate and signature/provenance policy.
The upstream source is <https://github.com/coturn/coturn/tree/4.14.0> and the
official container is <https://hub.docker.com/r/coturn/coturn>.

Run separate `TURN_LISTENER_MODE=UDP`, `TCP`, and `TLS` pools; advertise their
corresponding URLs from the control plane. The production-like compose file
uses host networking because TURN relay port
NAT through a container bridge obscures capacity and is not the production
topology. Deploy each node on a dedicated public IP and allow only:

- `3478/udp,tcp`, `5349/tcp`, and the configured UDP relay range publicly;
- `9641/tcp` from the protected metrics network only;
- Redis statistics traffic to the protected management network only.

The API and coturn must read the exact same base64 text from the TURN shared
secret. The API never returns that secret: it returns only a username and the
time-limited coturn REST HMAC. Rotate by briefly accepting two regional pools,
draining the old pool, then destroying its secret.

The config denies loopback, link-local, private, carrier-grade NAT, benchmark,
multicast, and IPv6 ULA destinations. Cloud firewall policy must duplicate the
deny boundary; coturn configuration is defense in depth, not the sole control.
`TURN_DENIED_PEERS_FILE` is mandatory and adds the environment's public control
plane, database, observability, metadata, and management address ranges as
validated `denied-peer-ip=` lines.
Prometheus username labels remain disabled because time-limited usernames
would create unbounded cardinality. Per-session usage instead comes from the
Redis stats event collector and is sent to the signed internal usage endpoint.

Run `tools/network/turn_usage_collector.py` as one supervised sidecar per TURN
listener pool with a persistent spool volume. Supply `TURN_REGION`,
`TURN_TRANSPORT`, `TURN_NODE_ID`, `TURN_USAGE_ENDPOINT`, `TURN_REDIS_HOST`, and
secret-file paths `TURN_METERING_KEY_FILE` and `TURN_REDIS_PASSWORD_FILE`.
Redis TLS is required by default. Configure separate UDP, TCP, and TLS listener
pools (or an equivalent trusted transport label source) so the metering
transport dimension is authoritative and bounded.

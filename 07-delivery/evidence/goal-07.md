# Goal 07 evidence

Date: 2026-07-02. Local environment: Windows x64, .NET 10, pinned
libdatachannel 0.24.3/libjuice, no Docker/WSL distribution or public two-peer
network lab available.

Automated server integration proves:

- RFC 9449 ES256 DPoP binds peer token, key, method, URI, `ath`, session,
  scopes, permission revision and epoch; repeated `jti` and cross-session use
  fail;
- signaling tickets are random, hashed at rest, single-use, and valid for at
  most 60 seconds;
- two authenticated WebSockets complete `HELLO`, relay a bounded fingerprinted
  SDP offer with server-observed identity, and reconnect without changing the
  active transport epoch;
- sequence gaps/replay, malformed SDP, malformed ICE, expired authorization,
  consumed tickets, and expired unused tickets fail closed;
- issued TURN data contains UDP, TCP and TLS routes, expires within ten minutes,
  and matches coturn's REST HMAC without exposing the shared secret;
- signed collector usage is joined to the correct session and recorded once by
  bounded region, node, transport and byte dimensions.

Infrastructure evidence includes coturn 4.14.0 source/image pins, authenticated
secret injection, TLS 1.2+ policy, private/control-network destination denies,
restricted relay range, nonzero allocation/bandwidth quotas, protected metrics,
Redis stats collection with durable spool, a capacity model, and a redacted
direct/TURN UDP/TCP/TLS route probe.

Final local automated result: 16/16 Debug native tests, 43/43 Debug managed
tests (including 7 server HTTP/WebSocket integrations), 12/12 Release native
tests, zero-warning Release solution build, database/Goal-07 static integration
verification, design-bundle validation, and repository secret scan all pass.
Physical NAT/firewall, anonymous/expired allocation against a live coturn node,
quota saturation, TLS scanning, two-node failover and measured relay-node
capacity remain mandatory deployment-lab promotion evidence because this host
has no container runtime, WSL distribution, public relay address, or elevated
firewall control. The runnable matrix and probe are checked in under
`deploy/network-lab` and `eng/run-goal07-network-lab.ps1`; these external facts
are not represented as locally passed tests.

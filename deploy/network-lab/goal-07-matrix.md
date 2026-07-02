# Goal 07 network lab matrix

Run each profile from two disposable Windows peers in an isolated cloud lab.
The control API and signaling endpoint remain reachable on 443/TCP. Only the
listed peer/TURN paths are allowed. Capture route class from
`rs_transport_get_stats`; never capture raw SDP, credentials, or media.

| Profile | Peer egress policy | Probe | Expected route |
|---|---|---|---|
| direct | peer UDP and TURN allowed | `direct` | `DIRECT_UDP` |
| relay UDP | peer-to-peer denied; TURN 3478/UDP allowed | `turn-udp` | `TURN_UDP` |
| relay TCP | all UDP denied; TURN 3478/TCP allowed | `turn-tcp` | `TURN_TCP` |
| relay TLS | all UDP and 3478 denied; TURN 5349/TCP allowed | `turn-tls` | `TURN_TLS` |
| impairment | chosen route plus 3% loss, 80 ms RTT, 20 ms jitter | matching route | connects under 10 s and remains bound |
| signaling loss | established peer transport; drop only 443/TCP signaling | reconnect ticket | media/data remains active; epoch unchanged |

TURN/TCP and TURN/TLS client control connections require the native build with
`-DRS_ICE_BACKEND=libnice`; the default `libjuice` build supports TURN/UDP.
Use `eng/run-goal07-network-lab.ps1` for each route and retain its redacted JSON
evidence. The `-CheckInvalidCredential` switch must pass for every TURN route.

Quota validation uses a dedicated node configured with `user-quota=2` and a
small `total-quota`. Hold two allocations for one credential with coturn
`turnutils_uclient -h`; the third must fail. Repeat at total quota and verify
the Prometheus allocation-failure counter and alert. Never weaken the peer-IP
deny list for this test; use public disposable peer addresses.

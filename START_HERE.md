# Start Here

Run `python 11-bootstrap/validate-bundle.py .` in the bundle root, read `IMPLEMENTATION_READINESS.md`, then execute goals in `07-delivery/implementation-order.md` without merging release trains. Goal 01 creates the repository, contract generation, disposable PostgreSQL integration tests, Windows CI, dependency locks and ADR workflow.

Implementation principles:

1. Never bypass signed consent, device-key proof, peer authorization, transport binding or scope enforcement to accelerate a demo.
2. The attended release contains no LocalSystem service and no unattended capability.
3. Managed Host begins only after attended GA and unattended begins only after Managed Host approval.
4. Every requirement closes with its CSV acceptance case and retained evidence.
5. A failed Windows native spike changes an ADR and affected contracts before feature work continues.

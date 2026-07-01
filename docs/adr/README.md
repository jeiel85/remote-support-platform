# Architecture Decision Workflow

Canonical product ADRs remain in `01-architecture/adr`. An implementation change that alters a trust boundary, public contract, process boundary, native toolchain, media path, persistence invariant, or release boundary must add or supersede an ADR there before code review.

Each ADR records status, date, context, decision, alternatives considered, security and operational consequences, migration or rollback behavior, and links to affected requirements/tests. CI validates design links and the design manifest. A behavior difference without an ADR is a release blocker under NFR-MNT-005.


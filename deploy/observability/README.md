# Production observability assets

The server and clients emit OpenTelemetry-compatible `ActivitySource` and
`Meter` data under the `RemoteSupport` namespace. Deploy the collector with an
approved authenticated backend endpoint and inject its endpoint/authorization
header at runtime;
the checked-in file contains no exporter secret. The privacy processor deletes
known sensitive attributes before batching. Collector access controls and
retention remain part of the environment deployment.

Prometheus scrapes `/internal/metrics` with the protected
`Observability:MetricsBearerToken`. The endpoint returns 404 for a missing or
wrong credential and exposes only route templates and bounded status classes,
never raw paths, tenant IDs, support codes or customer content. Run
`promtool test rules prometheus-rule-tests.yaml` in the monitoring image before
promotion. Dashboard variables intentionally exclude tenant/session identity.

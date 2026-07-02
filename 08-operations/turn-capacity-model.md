# TURN capacity model and promotion gate

The checked-in node defaults are safety ceilings, not a capacity claim:

- 2,000 relay UDP ports (50000–51999);
- 1,600 concurrent allocations (`total-quota`);
- two allocations per ephemeral username (`user-quota`);
- 10 MB/s per allocation and 625 MB/s aggregate server capacity;
- a scheduling target of at most 600 two-allocation sessions per node.

At two allocations per session, ports permit 1,000 sessions and quota permits
800. With the 600-session scheduling target, quota headroom is 25% and port
headroom is 40%. A region requires at least two independently drainable nodes.

Promotion additionally requires a measured limit from the exact image, VM,
kernel, NIC, relay range, and packet-size distribution. Increase concurrency
until the first of CPU, packet drops, allocation p95, NIC, or port pressure
crosses its threshold. Define safe capacity as 65% of that first measured
limit, then set the scheduler target to the lower of measured safe capacity,
quota capacity, and port capacity. The result record must include image digest,
host profile, packets/s, bidirectional bytes/s, p95 allocation latency, CPU,
drops, test duration, and 95% confidence interval.

Scale or drain before sustained NIC utilization exceeds 65%, packet drops
exceed 0.1%, allocation p95 exceeds 250 ms, CPU exceeds the measured safe
threshold, or free relay ports fall below 25%. Credentials stop advertising a
draining node before allocations are terminated.

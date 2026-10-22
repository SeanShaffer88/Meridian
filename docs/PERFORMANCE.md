# Performance Plan

No representative device-level performance measurements have been recorded in this document. Automated builds and tests are not performance evidence.

## Metrics to capture on Windows 11
- Cold startup to interactive window.
- Navigation latency for folders with 1k, 10k, and 100k entries.
- UI responsiveness during enumeration, live name/type filtering and file copies.
- Idle and active CPU usage, working set, and private bytes.
- Copy throughput for small files, large files, and mixed trees.
- Cancellation responsiveness and memory growth over repeated navigation.

Record OS build, CPU, storage, RAM, runtime, build configuration, folder composition, number of repetitions, median, and p95. Compare against a documented baseline rather than inventing universal targets. Use representative local SSD, HDD, and network scenarios where available.

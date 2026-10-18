# Performance Plan

No performance claims are made yet; measurements have not been collected.

The [verified x64 startup smoke](VERIFICATION_REPORT.md) checks survival for ten seconds and presence of a main window. It does not measure startup-to-interactive latency or establish any budget. The proposed limits and bounded algorithms in feature documents are resource policies, not benchmark results.

## Metrics to capture on Windows 11
- Cold startup to interactive window.
- Navigation latency for folders with 1k, 10k, and 100k entries.
- UI responsiveness during enumeration and file copies.
- Idle and active CPU usage, working set, and private bytes.
- Copy throughput for small files, large files, and mixed trees.
- Cancellation responsiveness and memory growth over repeated navigation.

Record OS build, CPU, storage, RAM, runtime, build configuration, folder composition, number of repetitions, median, and p95. Compare against a documented baseline rather than inventing universal targets. Use representative local SSD, HDD, and network scenarios where available.

Use disposable generated datasets with recorded file counts/size distribution and the exact commit. Separate cold and warm runs and avoid counting data creation or compilation as application time. Record failures/cancellation latency as well as successful throughput. Include x64/ARM64 and decoded-media memory tests where suitable devices are available; report unavailable hardware instead of extrapolating.

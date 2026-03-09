---
name: maui-profiling
description: Build lightweight profiling context for a running .NET MAUI app using local MauiDevFlow status, network, and visual-tree summaries instead of uploading raw trace artifacts. Use when investigating slow screens, performance regressions, excessive network chatter, or deciding whether deeper trace capture is needed.
---

# MAUI Profiling Context

Use structured, local summaries first. Avoid asking for raw `.nettrace`, `gcdump`, or other large artifacts unless the lightweight snapshot is insufficient.

## Workflow

1. Run `get_profiling_catalog` to understand supported scenarios and platform capabilities.
2. Run `list_profiling_targets` to discover locally running MauiDevFlow-enabled apps.
3. Run `get_profiling_snapshot` for the relevant target.
4. Review:
   - runtime status (platform, device type, WebView readiness)
   - recent network behavior (sample size, failures, latency, slowest requests)
   - visual tree complexity (element counts, depth, top element types)
5. Use the summary to narrow the investigation before requesting any deeper traces.

## Guidance

- Prefer the default lightweight snapshot first.
- Increase `networkSampleSize` only when recent request volume is too low to explain the issue.
- If multiple targets are connected, specify `targetId` from `list_profiling_targets`.
- Treat the snapshot as context for follow-up recommendations, not as a full profiler replacement.

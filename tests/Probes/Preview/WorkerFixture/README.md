# One-shot development preview worker

This explicit managed executable calls `PreviewWorker.RunAsync` with a statically
constructed catalog and the existing compiled `.lui` fixture. It never runs a
shipping application entry point. This is development execution, not a sandbox
or NativeAOT proof.

Launch with `--request <absolute request.json>`. Keep standard input open during
capture; `stop` followed by a newline, or EOF, cancels setup and capture.
`card/empty` captures normally; `card/fractional` captures a 160 by 120 logical
viewport at binary32 scale 1.1 as 176 by 132 physical pixels;
`card/setup-wait` waits for cancellation;
`card/cleanup-fail` rejects success after an authored cleanup failure.

The request selects an existing empty caller-owned local fixed-drive directory.
The worker rejects reparse points and writes `frame.png`, then atomically publishes
`result.json` only after successful scenario/host disposal. The coordinator must
independently validate the result, PNG, freshness and actual process termination.
Output is logging rather than protocol. A process-lifetime background reader is
disarmed on return and ends when the executable exits; `RunAsync` is not a reusable
in-process session API. Ignored cancellation or hanging cleanup is bounded by the
external job supervisor, which must distinguish forced exit from managed cleanup.

Image readiness is an explicit optional `PrepareImagesAsync` hosting callback
receiving the application and operation token. Its completion declares the
author's required resources ready; it does not imply global idleness. The worker
uses a cooperative deadline of at most 60 seconds and bounds encoded PNGs to
32 MiB and each physical dimension to 8192 pixels.

# One-shot development preview worker

This explicit managed executable calls `PreviewWorker.RunAsync` with a statically
constructed catalog and the existing compiled `.lui` fixture. It never runs a
shipping application entry point. This is development execution, not a sandbox
or NativeAOT proof.

Launch with `--request <absolute request.json>`. Keep standard input open during
capture; `stop` followed by a newline, or EOF, cancels setup and capture.
Worker protocol version 2 supports catalog discovery without executing setup,
theme factories or root callbacks. Discovery atomically publishes `catalog.json`.
Capture requests supply the complete effective width, height, device scale,
color scheme, contrast and density, using catalog defaults for unmodified values.
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
32 MiB, each physical dimension to 8192 pixels and the image to 16,777,216 pixels
before allocating the host. Catalogs contain at most 64 scenarios and encoded
metadata is limited to 64 KiB.

Each capture has a fixed appearance. The worker invokes the author's theme
factory with that selected appearance before scenario setup, so the fixture and
host observe the same theme. Presentation changes require a fresh worker;
this entry point does not provide dynamic appearance updates or persistent
interactive sessions. Zoom belongs to the editor display rather than capture.

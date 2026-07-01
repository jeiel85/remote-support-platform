# Goal 05 Evidence

Date: 2026-07-02. Environment: Windows x64, LLVM-MinGW, .NET 10.

- `AT-FR-INP-001-local-scope-and-lifecycle` proves that a view-only context
  cannot inject forged pointer input, strict event sequencing is enforced, and
  emergency disable releases tracked pointer/key state before rejecting more
  input.
- `AT-FR-INP-002-coordinate-normalization` covers negative virtual origins,
  exact 0/65535 endpoints, center rounding, portrait dimensions and out-of-
  bounds rejection.
- `ViewportMapperTests` covers fit letterboxing, actual renderer geometry for
  stretch/zoom/pan, portrait post-rotation dimensions and explicit mixed-DPI
  conversion.
- The native capability query reports `INPUT_SECURE_DESKTOP_UNSUPPORTED`,
  `INPUT_UIPI_BLOCKED`, `INPUT_DISABLED`, or `INPUT_AVAILABLE`. Actual
  `SendInput` success requires the returned count to equal the requested batch.

Deterministic tests use a Debug-only no-op sink so CI never moves the developer's
real pointer or keyboard. Release rejects that flag. Hardware-lab qualification
for real injection, UAC/UIPI and IME behavior is tracked in
`06-quality/compatibility-matrix.md`; the portable build never claims secure-
desktop control.

Debug passed all 16 native tests and all 27 managed tests. Release passed all
12 applicable native tests and built the managed solution with zero warnings
and zero errors. Direct execution of the Debug-only injection harness against
the Release DLL failed at injector creation as required (`exit=2`).

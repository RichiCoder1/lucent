# Accessibility Insights attach crash

- WER report: `Report.wer` (SHA-256 in `Report.wer.sha256`)
- Failure: `NativeStackProbe.exe`, `ntdll.dll`, exception `0xc0000005`, offset `0x5561b`
- No dump or installed WinDbg/cdb/dotnet-dump/procdump was available in the archived report or PATH.

## Exact ABI mismatch

Windows SDK 10.0.26100.0 `UIAutomationCore.idl:530-540` declares
`IRawElementProviderFragmentRoot : IUnknown` with exactly two methods after
IUnknown: `ElementProviderFromPoint(double,double,fragment**)` and
`GetFocus(fragment**)`. `UIAutomationCore.h:1464-1512` confirms the same five
slot C/C++ vtable.

The crashing build instead put the six `IRawElementProviderFragment` methods
before those two methods. A real client calling FragmentRoot slot 3 as
`ElementProviderFromPoint` therefore entered `Navigate(int,fragment**)` with
incompatible x64 argument registers/pointer levels. Accessibility Insights
exercised that root hit-test call; the narrower helper did not. The corrected
provider restores the SDK five-slot vtable, directly invokes both root slots
in the host ABI check, and the broad external regression repeatedly calls
`AutomationElement.FromPoint` while enumerating the visible tree.

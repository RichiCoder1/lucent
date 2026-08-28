# Research: Lucent Native issue #3 UIA transport

## Current result

The SDL NativeAOT host passes the real external UIA proof. The non-AOT helper
reads `Name=NativeStackProbe UIA root` and
`AutomationId=NativeStackProbe.Root` three times, including a worker-thread
read. The host records six genuine root `WM_GETOBJECT` deliveries, nine
`GetPropertyValue` calls, Simple interface creation, successful disconnect
(`HRESULT 0`), subclass removal, provider release, and HWND destruction.

The direct-Win32 issue #19 discriminator is therefore superseded and must not
be used to start Win32 IME or adapter work.

## Primary-contract chronology

1. SDK `10.0.26100.0` `UIAutomationCore.idl:412-430` defines
   `IRawElementProviderSimple` IID
   `d6dd68d1-86fd-4332-8666-9abedea2d24c` and four HRESULT slots:
   `ProviderOptions`, `GetPatternProvider`, `GetPropertyValue`, and
   `HostRawElementProvider`.
2. Earlier source-generated declarations omitted `[PreserveSig]`, producing
   an extra managed return pointer and a non-SDK vtable. That analysis is
   historical: the active provider is a hand-written `ComWrappers` vtable with
   exactly the four SDK slots; no generated COM provider remains.
3. `UIAutomationCoreApi.h:890-892` declares `UiaReturnRawElementProvider` as
   `LRESULT` and `UiaHostProviderFromHwnd` as
   `HRESULT(HWND, IRawElementProviderSimple**)`; `:931` declares
   `UiaDisconnectProvider` as HRESULT. The active imports match these pointer
   levels and record the disconnect HRESULT.
4. Valid `WM_GETOBJECT` messages are generated only by the real external
   client. Synthetic checks send invalid IDs only and chain them to the default
   procedure.

## Sources

- Local headers: `C:/Program Files (x86)/Windows Kits/10/Include/10.0.26100.0/um/UIAutomationCore.idl`, `UIAutomationCore.h`, and `UIAutomationCoreApi.h`.
- Microsoft: [Handling the WM_GETOBJECT Message](https://learn.microsoft.com/en-us/windows/win32/winauto/handling-the-wm-getobject-message).
- Microsoft: [Implement a Server-Side UI Automation Provider](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-serversideprovider).
- Microsoft: [IRawElementProviderSimple](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcore/nn-uiautomationcore-irawelementprovidersimple).

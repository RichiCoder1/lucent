# M5 challenge A: density restyle

Add a keyboard- and UIA-accessible header control that switches the Issue Browser between Comfortable and Compact density. Density is an application preference expressed through typed style tokens, not an operating-system appearance setting or Core policy. It changes virtual-row height, spacing, and typography without persistence.

At a mid-list position with an issue selected and keyboard-focused, Comfortable → Compact → Comfortable must preserve the top visible issue key and its relative viewport offset, selection, focus, UIA identity, and the existing realization bound. The challenge must pass light, dark, high-contrast, managed, and NativeAOT paths without application-owned synchronization or a test-only application mode.


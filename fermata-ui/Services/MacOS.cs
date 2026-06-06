/// Native macOS helpers — compiled only on macOS.
#if !WINDOWS
using System.Runtime.InteropServices;

namespace FermataUI.Services;

internal static class MacOS
{
    // NSApplicationActivationPolicy values:
    //   Regular   = 0  (Dock icon, appears in Cmd+Tab)
    //   Accessory = 1  (no Dock icon, no Cmd+Tab, still receives events)
    //   Prohibited= 2  (no Dock, no activation at all)
    private const long NSApplicationActivationPolicyAccessory = 1;

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_long(IntPtr receiver, IntPtr selector, long arg);

    /// <summary>
    /// Switches the app to Accessory activation policy so it never shows a Dock icon.
    /// Must be called before Avalonia initialises.
    /// </summary>
    public static void SetAccessoryActivationPolicy()
    {
        try
        {
            var nsAppClass  = objc_getClass("NSApplication");
            var sharedApp   = sel_registerName("sharedApplication");
            var setPolicy   = sel_registerName("setActivationPolicy:");
            var app         = objc_msgSend(nsAppClass, sharedApp);
            objc_msgSend_long(app, setPolicy, NSApplicationActivationPolicyAccessory);
        }
        catch
        {
            // Non-fatal — the app still works, it just might show a Dock icon.
        }
    }
}
#endif

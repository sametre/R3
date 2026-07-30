using System.Runtime.InteropServices;

namespace R3.Desktop.Platform;

internal static class WindowsAppIdentity
{
    private const string R3ApplicationId = "R3.ERP.Desktop";

    public static void Initialize()
    {
        int result = SetCurrentProcessExplicitAppUserModelID(R3ApplicationId);
        if (result < 0)
            Marshal.ThrowExceptionForHR(result);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}

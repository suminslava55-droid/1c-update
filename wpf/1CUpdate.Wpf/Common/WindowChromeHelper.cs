using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OneCUpdate.Common;

/// <summary>Тёмный заголовок окна в Windows 10/11.</summary>
public static class WindowChromeHelper
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeOld = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void EnableDarkTitleBar(Window window)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                window.SourceInitialized += (_, _) => Apply(window);
                return;
            }

            Apply(window);
        }
        catch
        {
            // Не критично: тема содержимого останется тёмной.
        }
    }

    private static void Apply(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int enabled = 1;
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeOld, ref enabled, sizeof(int));
        }
    }
}

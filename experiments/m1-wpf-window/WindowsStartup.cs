using System.IO;
using System.Security;
using System.Windows;
using Microsoft.Win32;

namespace Aeris.WindowProbe;

internal static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "AerisAlive";

    internal static string Assets => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "assets"));

    internal static void Bind(DesktopControlWindow control, bool automation)
    {
        string registryPath = automation ? @"Software\AerisAlive\StartupTest" : RunKey;
        var toggle = control.StartWithWindows;
        bool registered;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath);
            registered = key?.GetValue(EntryName) is string { Length: > 0 };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            toggle.IsEnabled = false;
            toggle.ToolTip = "无法读取 Windows 自启动设置。";
            return;
        }

        toggle.IsChecked = registered;
        toggle.Checked += (_, _) => SetRegistration(true);
        toggle.Unchecked += (_, _) => SetRegistration(false);

        void SetRegistration(bool enabled)
        {
            if (enabled == registered) return;
            try
            {
                if (enabled)
                {
                    string command = $"\"{Environment.ProcessPath}\" --startup";
                    if (command.Length > 260)
                    {
                        throw new InvalidOperationException("路径过长。请将温迪放到较短的路径或使用安装版，再试一次。");
                    }
                    using var key = Registry.CurrentUser.CreateSubKey(registryPath);
                    key.SetValue(EntryName, command, RegistryValueKind.String);
                }
                else
                {
                    using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true);
                    key?.DeleteValue(EntryName, throwOnMissingValue: false);
                }
                registered = enabled;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
            {
                toggle.IsChecked = registered;
                string message = ex is InvalidOperationException
                    ? ex.Message
                    : "无法更改 Windows 自启动设置。请检查当前用户的权限后重试。";
                MessageBox.Show(control, message, "VENTI ALIVE", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}

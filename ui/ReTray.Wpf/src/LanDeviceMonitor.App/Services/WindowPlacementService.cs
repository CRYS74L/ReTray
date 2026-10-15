using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Screen = System.Windows.Forms.Screen;

namespace LanDeviceMonitor.Services;

internal sealed class WindowPlacementService : IDisposable
{
    private const int ShowNormal = 1;
    private const int ShowMinimized = 2;
    private const int ShowMaximized = 3;
    private const int RestoreToMaximized = 0x0002;
    private readonly Window _window;
    private readonly string _placementPath;
    private readonly DispatcherTimer _saveTimer;
    private bool _sourceInitialized;
    private bool _isRestoring;
    private bool _isDisposed;

    public WindowPlacementService(Window window)
    {
        _window = window;
        _placementPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ReTray",
            "window-placement.json");
        _saveTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(300),
            DispatcherPriority.Background,
            (_, _) => SaveNow(),
            window.Dispatcher)
        {
            IsEnabled = false
        };

        _window.LocationChanged += WindowPlacementChanged;
        _window.SizeChanged += WindowPlacementChanged;
        _window.StateChanged += WindowPlacementChanged;
    }

    public bool Restore()
    {
        _sourceInitialized = true;
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero || !File.Exists(_placementPath))
        {
            return false;
        }

        try
        {
            var saved = JsonSerializer.Deserialize<SavedWindowPlacement>(
                File.ReadAllText(_placementPath));
            var workAreas = Screen.AllScreens.Select(screen => screen.WorkingArea);
            if (saved is null || !HasVisibleArea(saved, workAreas))
            {
                return false;
            }

            var placement = new NativeWindowPlacement
            {
                Length = Marshal.SizeOf<NativeWindowPlacement>(),
                ShowCommand = saved.IsMaximized ? ShowMaximized : ShowNormal,
                NormalPosition = new NativeRect
                {
                    Left = saved.Left,
                    Top = saved.Top,
                    Right = saved.Right,
                    Bottom = saved.Bottom
                }
            };

            _isRestoring = true;
            _window.WindowStartupLocation = WindowStartupLocation.Manual;
            return SetWindowPlacement(handle, ref placement);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        finally
        {
            _isRestoring = false;
        }
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        if (_isDisposed || !_sourceInitialized || _isRestoring)
        {
            return;
        }

        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var placement = new NativeWindowPlacement
        {
            Length = Marshal.SizeOf<NativeWindowPlacement>()
        };
        if (!GetWindowPlacement(handle, ref placement))
        {
            return;
        }

        var saved = new SavedWindowPlacement(
            placement.NormalPosition.Left,
            placement.NormalPosition.Top,
            placement.NormalPosition.Right,
            placement.NormalPosition.Bottom,
            placement.ShowCommand == ShowMaximized ||
            placement.ShowCommand == ShowMinimized &&
            (placement.Flags & RestoreToMaximized) != 0);
        if (!HasVisibleArea(saved, Screen.AllScreens.Select(screen => screen.WorkingArea)))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_placementPath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = $"{_placementPath}.tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(saved));
            File.Move(temporaryPath, _placementPath, overwrite: true);
        }
        catch (IOException)
        {
            // 窗口布局只影响下次启动，不应因短暂写入失败影响当前应用。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上；保留上一次成功保存的位置。
        }
    }

    internal static bool HasVisibleArea(
        SavedWindowPlacement placement,
        IEnumerable<Rectangle> workAreas)
    {
        if (placement.Right - placement.Left < 400 ||
            placement.Bottom - placement.Top < 300)
        {
            return false;
        }

        var bounds = Rectangle.FromLTRB(
            placement.Left,
            placement.Top,
            placement.Right,
            placement.Bottom);
        return workAreas.Any(workArea =>
        {
            var intersection = Rectangle.Intersect(bounds, workArea);
            return intersection.Width >= 80 && intersection.Height >= 80;
        });
    }

    private void WindowPlacementChanged(object? sender, EventArgs e)
    {
        if (!_sourceInitialized || _isRestoring || _isDisposed)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _saveTimer.Stop();
        _window.LocationChanged -= WindowPlacementChanged;
        _window.SizeChanged -= WindowPlacementChanged;
        _window.StateChanged -= WindowPlacementChanged;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(
        IntPtr windowHandle,
        ref NativeWindowPlacement windowPlacement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(
        IntPtr windowHandle,
        ref NativeWindowPlacement windowPlacement);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public NativePoint MinimumPosition;
        public NativePoint MaximumPosition;
        public NativeRect NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

internal sealed record SavedWindowPlacement(
    int Left,
    int Top,
    int Right,
    int Bottom,
    bool IsMaximized);

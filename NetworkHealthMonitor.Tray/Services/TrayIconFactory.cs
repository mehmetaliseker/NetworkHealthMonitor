using System.Drawing;
using System.Runtime.InteropServices;

namespace NetworkHealthMonitor.Tray.Services;

public sealed class TrayIconFactory : IDisposable
{
    private readonly Dictionary<WorkerServiceState, Icon> _icons = new();

    public Icon GetIcon(WorkerServiceState state)
    {
        var key = state switch
        {
            WorkerServiceState.Running => WorkerServiceState.Running,
            WorkerServiceState.Stopped => WorkerServiceState.Stopped,
            WorkerServiceState.StartPending or WorkerServiceState.StopPending => WorkerServiceState.StartPending,
            WorkerServiceState.NotInstalled => WorkerServiceState.NotInstalled,
            WorkerServiceState.Error => WorkerServiceState.Error,
            _ => WorkerServiceState.Unknown
        };

        if (_icons.TryGetValue(key, out var icon))
        {
            return icon;
        }

        icon = CreateIcon(key);
        _icons[key] = icon;
        return icon;
    }

    public void Dispose()
    {
        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }

        _icons.Clear();
    }

    private static Icon CreateIcon(WorkerServiceState state)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using var outer = new SolidBrush(Color.FromArgb(28, 31, 38));
            graphics.FillEllipse(outer, 1, 1, 30, 30);

            using var inner = new SolidBrush(GetColor(state));
            graphics.FillEllipse(inner, 7, 7, 18, 18);

            using var pen = new Pen(Color.White, 3);
            if (state == WorkerServiceState.Running)
            {
                graphics.DrawLines(pen, new[] { new Point(11, 16), new Point(15, 20), new Point(22, 12) });
            }
            else if (state == WorkerServiceState.Stopped)
            {
                graphics.DrawLine(pen, 11, 16, 21, 16);
            }
            else if (state is WorkerServiceState.StartPending or WorkerServiceState.StopPending)
            {
                graphics.DrawArc(pen, 10, 10, 12, 12, 25, 285);
            }
            else if (state == WorkerServiceState.NotInstalled)
            {
                graphics.DrawLine(pen, 12, 12, 20, 20);
                graphics.DrawLine(pen, 20, 12, 12, 20);
            }
            else
            {
                graphics.DrawLine(pen, 16, 10, 16, 18);
                graphics.FillEllipse(Brushes.White, 14, 21, 4, 4);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static Color GetColor(WorkerServiceState state)
    {
        return state switch
        {
            WorkerServiceState.Running => Color.FromArgb(22, 163, 74),
            WorkerServiceState.Stopped => Color.FromArgb(107, 114, 128),
            WorkerServiceState.StartPending or WorkerServiceState.StopPending => Color.FromArgb(245, 158, 11),
            WorkerServiceState.NotInstalled => Color.FromArgb(37, 99, 235),
            WorkerServiceState.Error => Color.FromArgb(220, 38, 38),
            _ => Color.FromArgb(100, 116, 139)
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

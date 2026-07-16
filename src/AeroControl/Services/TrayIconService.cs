using System.Drawing;
using System.Windows.Forms;

namespace AeroControl.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private bool _disposed;

    public TrayIconService()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open AeroControl", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "AeroControl",
            ContextMenuStrip = menu,
            Visible = false
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public void SetVisible(bool visible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _icon.Visible = visible;
    }

    public void ShowAlert(string title, string message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = ToolTipIcon.Warning;
        _icon.ShowBalloonTip(5_000);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        GC.SuppressFinalize(this);
    }
}

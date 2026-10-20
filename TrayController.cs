using System.Windows.Threading;

namespace Prtsc;

internal sealed class TrayController : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon icon;
    private readonly System.Windows.Forms.ContextMenuStrip menu;
    public TrayController(Dispatcher dispatcher, Action gallery, Action capture, Action windowCapture, Action editor, Action exit, Action? record = null, Action? stop = null)
    {
        icon = AppIcon.CreateTrayIcon();
        menu = CreateMenu(dispatcher, gallery, capture, windowCapture, editor, exit, record, stop);
        tray = new System.Windows.Forms.NotifyIcon { Text = "Prtsc — クリックでギャラリー", Icon = icon, ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) dispatcher.BeginInvoke(gallery); };
    }
    internal static System.Windows.Forms.ContextMenuStrip CreateMenu(Dispatcher dispatcher, Action gallery, Action capture, Action windowCapture, Action editor, Action exit, Action? record = null, Action? stop = null)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        void Add(string title, Action action) => menu.Items.Add(title, null, (_, _) => dispatcher.BeginInvoke(action));
        Add("ギャラリーを開く", gallery); Add("範囲をキャプチャ (PrtSc)", capture);
        Add("ウィンドウを選択して撮影", windowCapture); Add("編集画面を開く", editor);
        if (record is not null) Add("動画を録画", record);
        if (stop is not null) { Add("録画を停止して保存", stop); menu.Items[menu.Items.Count - 1].Enabled = false; }
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator()); Add("終了", exit);
        return menu;
    }
    public void Dispose() { tray.Visible = false; tray.Dispose(); menu.Dispose(); icon.Dispose(); }
    internal void UpdateRecording(bool active)
    {
        foreach (var item in menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>())
            if (item.Text == "録画を停止して保存") item.Enabled = active;
            else if (item.Text != "終了") item.Enabled = !active;
        tray.Text = active ? "Prtsc — 録画中（右クリックで停止）" : "Prtsc — クリックでギャラリー";
    }
}

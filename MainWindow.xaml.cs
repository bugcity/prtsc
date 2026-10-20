using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;
using Point = System.Windows.Point;

namespace Prtsc;

public partial class MainWindow : Window
{
    private readonly CaptureStore store;
    private readonly ObservableCollection<CaptureEntry> entries;
    private readonly Stack<Mark> redo = new();
    private CaptureEntry? current;
    private BitmapSource? original;
    private Mark? draft;
    private string tool = "Arrow", color = "#F43F5E";
    private bool capturing;
    private HwndSource? hwndSource;
    private IntPtr hwnd;
    private const int HotkeyId = 4812;

    public MainWindow(CaptureStore? captureStore = null)
    {
        store = captureStore ?? new CaptureStore();
        InitializeComponent();
        entries = new(store.Load()); History.ItemsSource = entries;
        CountLabel.Text = $"{entries.Count} 枚";
        if (entries.Count > 0) History.SelectedIndex = 0;
        SetTool("Arrow");
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle; hwndSource = HwndSource.FromHwnd(hwnd); hwndSource?.AddHook(WindowMessage);
            if (!RegisterHotKey(hwnd, HotkeyId, 0x4000, 0x2C))
            {
                HotkeyLabel.Text = "PrtSc：登録できませんでした";
                HotkeyLabel.ToolTip = "他の撮影アプリ、または Windows の Print Screen キー設定を確認してください。撮影ボタンは利用できます。";
                StatusLabel.Text = "PrtSc が競合しています。左の撮影ボタンを使えます。";
            }
        };
        Closed += (_, _) => { UnregisterHotKey(hwnd, HotkeyId); hwndSource?.RemoveHook(WindowMessage); };
        PreviewKeyDown += OnKeyDown;
    }
    private IntPtr WindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam.ToInt32() == HotkeyId) { handled = true; _ = Capture(CaptureMode.Region); }
        return IntPtr.Zero;
    }
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException or NotSupportedException or ArgumentException or InvalidOperationException or System.Security.SecurityException or FileFormatException)
        { StatusLabel.Text = "操作を完了できませんでした"; MessageBox.Show(this, ex.Message, "Prtsc", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private async void CaptureRegion(object sender, RoutedEventArgs e) => await Capture(CaptureMode.Region);
    private async void CaptureFull(object sender, RoutedEventArgs e) => await Capture(CaptureMode.FullScreen);
    private async void CaptureWindow(object sender, RoutedEventArgs e) => await Capture(CaptureMode.Window);
    private async Task Capture(CaptureMode mode)
    {
        if (capturing || !IsEnabled) return;
        capturing = true;
        try
        {
            Hide(); await Task.Delay(240);
            var targets = mode == CaptureMode.FullScreen ? [] : WindowCapture.Enumerate();
            var desktop = CaptureService.Desktop();
            BitmapSource? result = desktop;
            string? windowTitle = null;
            if (mode != CaptureMode.FullScreen)
            {
                var overlay = new RegionWindow(desktop, targets, mode == CaptureMode.Window);
                overlay.ShowDialog(); result = overlay.Result; windowTitle = overlay.SelectedTitle;
            }
            ShowEditor();
            if (result != null) Run(() => AddCapture(result, windowTitle ?? "キャプチャ " + DateTime.Now.ToString("HH:mm:ss")));
            else StatusLabel.Text = "撮影をキャンセルしました";
        }
        catch (Exception ex) { ShowEditor(); MessageBox.Show(this, ex.Message, "撮影に失敗しました", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { capturing = false; }
    }
    private void ShowEditor()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
    private void AddCapture(BitmapSource bitmap, string title)
    {
        var entry = new CaptureEntry { Title = title };
        store.Create(entry, bitmap); entries.Insert(0, entry); History.SelectedItem = entry;
        CountLabel.Text = $"{entries.Count} 枚"; StatusLabel.Text = "キャプチャを保存しました · ドラッグして注釈を追加";
    }
    private void HistoryChanged(object sender, SelectionChangedEventArgs e)
    {
        draft = null; Editor.ReleaseMouseCapture(); redo.Clear();
        current = History.SelectedItem as CaptureEntry;
        if (current is null) { original = null; ImageView.Visibility = Visibility.Collapsed; EmptyState.Visibility = Visibility.Visible; TitleLabel.Text = "キャプチャを、伝わる一枚に。"; DetailLabel.Text = "撮影して、書き込んで、すぐ共有。"; return; }
        Run(() =>
        {
            original = store.Original(current); TitleLabel.Text = current.Title;
            DetailLabel.Text = $"{original.PixelWidth:N0} × {original.PixelHeight:N0} px  ·  {current.Created:yyyy/MM/dd HH:mm}";
            EmptyState.Visibility = Visibility.Collapsed; ImageView.Visibility = Visibility.Visible; Redraw();
            StatusLabel.Text = "注釈は自動保存されます · A 矢印 / R 四角 / T 文字";
        });
    }
    private void Redraw()
    {
        if (original is null || current is null) return;
        AnnotationRenderer.Draw(Editor, original, current.Marks);
        if (draft is not null) AnnotationRenderer.Add(Editor, draft);
    }
    private void Commit()
    {
        if (current is null || original is null) return;
        Redraw();
        Run(() => { store.Save(current, AnnotationRenderer.Render(original, current.Marks)); History.Items.Refresh(); StatusLabel.Text = "保存済み · " + current.Marks.Count + " 件の注釈"; });
    }
    private void ChooseTool(object sender, RoutedEventArgs e) => SetTool((string)((Button)sender).Tag);
    private void SetTool(string value)
    {
        tool = value;
        foreach (var button in Tools.Children.OfType<Button>())
        {
            bool active = (string)button.Tag == value;
            button.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(active ? "#EEF0FF" : "#FFFFFF"));
            button.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(active ? "#8183F6" : "#E2E8F0"));
        }
    }
    private void ChooseColor(object sender, RoutedEventArgs e)
    {
        color = (string)((Button)sender).Tag; CurrentColor.Fill = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(color)!;
    }
    private Point Position(MouseEventArgs e)
    {
        var p = e.GetPosition(Editor); return new Point(Math.Clamp(p.X, 0, Editor.Width), Math.Clamp(p.Y, 0, Editor.Height));
    }
    private void BeginDraw(object sender, MouseButtonEventArgs e)
    {
        if (current is null || original is null) return;
        var point = Position(e);
        var mark = new Mark { Tool = tool, Color = color, Width = StrokeSlider.Value, X = point.X, Y = point.Y, X2 = point.X, Y2 = point.Y };
        if (tool == "Text")
        {
            var text = AskText();
            if (string.IsNullOrWhiteSpace(text)) return;
            current.Marks.Add(mark with { Text = text }); redo.Clear(); Commit(); return;
        }
        if (tool == "Number")
        {
            current.Marks.Add(mark with { Text = (current.Marks.Count(m => m.Tool == "Number") + 1).ToString() }); redo.Clear(); Commit(); return;
        }
        draft = mark;
        if (tool == "Pen") draft.Points.Add([point.X, point.Y]);
        Editor.CaptureMouse(); e.Handled = true;
    }
    private void MoveDraw(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (draft is null) return;
        var p = Position(e); draft = draft with { X2 = p.X, Y2 = p.Y };
        if (draft.Tool == "Pen") draft.Points.Add([p.X, p.Y]);
        Redraw();
    }
    private void EndDraw(object sender, MouseButtonEventArgs e)
    {
        if (draft is null || current is null) return;
        var p = Position(e); var completed = draft with { X2 = p.X, Y2 = p.Y }; draft = null; Editor.ReleaseMouseCapture();
        if (Math.Abs(completed.X2 - completed.X) + Math.Abs(completed.Y2 - completed.Y) >= 3 || completed.Points.Count > 2)
        { current.Marks.Add(completed); redo.Clear(); Commit(); }
        else Redraw();
    }
    private void LostCapture(object sender, System.Windows.Input.MouseEventArgs e) { if (draft is not null) { draft = null; Redraw(); } }
    private string? AskText()
    {
        var input = new System.Windows.Controls.TextBox { Margin = new Thickness(0, 12, 0, 15), Padding = new Thickness(10), AcceptsReturn = true, Height = 105, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var dialog = new Window { Title = "テキストを追加", Owner = this, Width = 420, Height = 245, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = Brushes.White };
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "画像に入れるテキスト", FontWeight = FontWeights.SemiBold }); panel.Children.Add(input);
        var button = new Button { Content = "追加", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80 };
        button.Click += (_, _) => dialog.DialogResult = true;
        panel.Children.Add(button); dialog.Content = panel;
        dialog.Loaded += (_, _) => input.Focus(); dialog.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) dialog.Close(); };
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }
    private void Undo(object? sender, RoutedEventArgs? e)
    {
        if (current is null || current.Marks.Count == 0) return;
        redo.Push(current.Marks[^1]); current.Marks.RemoveAt(current.Marks.Count - 1); Commit();
    }
    private void Redo(object? sender, RoutedEventArgs? e)
    {
        if (current is null || redo.Count == 0) return;
        current.Marks.Add(redo.Pop()); Commit();
    }
    private void OpenImage(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" };
        if (dialog.ShowDialog(this) == true) Import(dialog.FileName);
    }
    private void Import(string path) => Run(() => AddCapture(CaptureStore.ReadImage(path), Path.GetFileNameWithoutExtension(path)));
    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
            foreach (var path in paths.Where(p => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" }.Contains(Path.GetExtension(p).ToLowerInvariant()))) Import(path);
    }
    private void CopyImage(object? sender, RoutedEventArgs? e)
    {
        if (original is null || current is null) return;
        Run(() => { Clipboard.SetImage(AnnotationRenderer.Render(original, current.Marks)); StatusLabel.Text = "注釈付きの画像をコピーしました"; });
    }
    private void ExportImage(object sender, RoutedEventArgs e)
    {
        if (original is null || current is null) return;
        var name = string.Concat(current.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG 画像|*.png", FileName = name + ".png", AddExtension = true };
        if (dialog.ShowDialog(this) == true) Run(() => { CaptureStore.WriteImage(dialog.FileName, AnnotationRenderer.Render(original, current.Marks)); StatusLabel.Text = "保存しました · " + dialog.FileName; });
    }
    private void DeleteCapture(object sender, RoutedEventArgs e)
    {
        if (current is null) return;
        if (MessageBox.Show(this, "このキャプチャを履歴から削除しますか？", "キャプチャを削除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Run(() => { var entry = current; store.Delete(entry); entries.Remove(entry); if (entries.Count > 0) History.SelectedIndex = 0; CountLabel.Text = $"{entries.Count} 枚"; });
    }
    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.Z: Undo(null, null); break;
                case Key.Y: Redo(null, null); break;
                case Key.C: CopyImage(null, null); break;
                case Key.V: Run(() => { if (Clipboard.GetImage() is { } pasted) AddCapture(pasted, "貼り付け " + DateTime.Now.ToString("HH:mm:ss")); }); break;
                case Key.S: ExportImage(this, e); break;
                case Key.O: OpenImage(this, e); break;
                default: return;
            }
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.Escape && draft is not null) { draft = null; Editor.ReleaseMouseCapture(); Redraw(); }
            var value = e.Key switch { Key.A => "Arrow", Key.R => "Rectangle", Key.E => "Ellipse", Key.P => "Pen", Key.H => "Highlight", Key.T => "Text", Key.N => "Number", _ => null };
            if (value is not null) SetTool(value);
        }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

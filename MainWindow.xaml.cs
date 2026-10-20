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
    private AnnotationDocument? document;
    private readonly Func<BitmapSource, Task> copyToClipboard;
    private readonly bool enableShell;
    private TrayController? tray;
    private GalleryWindow? gallery;
    private CaptureToast? toast;
    private bool closing, exitRequested, syncingSelection, moving;
    private int selectedIndex = -1;
    private string dragPart = "Move";
    private Point dragStart;
    private Mark? dragOriginal, selectionDraft;
    private CaptureEntry? current;
    private BitmapSource? original;
    private Mark? draft;
    private string tool = "Arrow", color = "#F43F5E";
    private string arrowStyle = "Tapered";
    private bool capturing;
    private HwndSource? hwndSource;
    private IntPtr hwnd;
    private const int HotkeyId = 4812;

    public MainWindow(CaptureStore? captureStore = null, Func<BitmapSource, Task>? clipboardWriter = null, bool enableShell = true)
    {
        store = captureStore ?? new CaptureStore();
        copyToClipboard = clipboardWriter ?? ImageClipboard.CopyAsync; this.enableShell = enableShell;
        InitializeComponent();
        syncingSelection = true; ArrowStyleBox.ItemsSource = ArrowStyles.Options; ArrowStyleBox.SelectedValue = arrowStyle; syncingSelection = false;
        entries = new(store.Load()); History.ItemsSource = entries;
        CountLabel.Text = $"{entries.Count} 枚";
        if (entries.Count > 0) History.SelectedIndex = 0;
        SetTool("Arrow");
        SourceInitialized += (_, _) =>
        {
            if (!enableShell) return;
            tray = new TrayController(Dispatcher, ShowGallery, () => _ = Capture(CaptureMode.Region), () => _ = Capture(CaptureMode.Window), ShowEditor, RequestExit);
            hwnd = new WindowInteropHelper(this).Handle; hwndSource = HwndSource.FromHwnd(hwnd); hwndSource?.AddHook(WindowMessage);
            if (!RegisterHotKey(hwnd, HotkeyId, 0x4000, 0x2C))
            {
                HotkeyLabel.Text = "PrtSc：登録できませんでした";
                HotkeyLabel.ToolTip = "他の撮影アプリ、または Windows の Print Screen キー設定を確認してください。撮影ボタンは利用できます。";
                StatusLabel.Text = "PrtSc が競合しています。左の撮影ボタンを使えます。";
            }
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && tray is not null) HideToTray(); };
        Closing += (_, e) =>
        {
            if (exitRequested) return;
            e.Cancel = true; CancelGesture(); HideToTray();
        };
        Closed += (_, _) =>
        {
            closing = true; toast?.Close(); gallery?.Close(); tray?.Dispose();
            if (hwnd != IntPtr.Zero) UnregisterHotKey(hwnd, HotkeyId); hwndSource?.RemoveHook(WindowMessage);
            if (enableShell) System.Windows.Application.Current.Shutdown();
        };
        PreviewKeyDown += OnKeyDown;
    }
    internal void PrepareForShutdown() => exitRequested = true;
    internal void RequestExit()
    {
        if (closing) return;
        PrepareForShutdown(); Close();
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
        if (capturing || !IsEnabled || closing) return;
        capturing = true;
        bool restoreEditor = IsVisible && WindowState != WindowState.Minimized;
        bool restoreGallery = gallery?.IsVisible == true;
        try
        {
            toast?.Close(); gallery?.Hide(); Hide(); await Task.Delay(240);
            if (closing) return;
            var targets = mode == CaptureMode.FullScreen ? [] : WindowCapture.Enumerate();
            var desktop = CaptureService.Desktop();
            BitmapSource? result = desktop;
            string? windowTitle = null;
            if (mode != CaptureMode.FullScreen)
            {
                var overlay = new RegionWindow(desktop, targets, mode == CaptureMode.Window);
                overlay.ShowDialog(); result = overlay.Result; windowTitle = overlay.SelectedTitle;
            }
            if (closing) return;
            if (result != null)
            {
                var entry = AddCapture(result, windowTitle ?? "キャプチャ " + DateTime.Now.ToString("HH:mm:ss"), false);
                bool copied = false;
                try { await CopyEntryAsync(entry); copied = true; }
                catch (Exception ex) { StatusLabel.Text = "保存しました。コピーはギャラリーから再試行できます · " + ex.Message; }
                if (closing) return;
                HideToTray();
                toast = new CaptureToast(entry, copied, () => OpenEntry(entry));
                var activeToast = toast;
                toast.Closed += (_, _) => { if (toast == activeToast) toast = null; };
                toast.Show();
            }
            else
            {
                StatusLabel.Text = "撮影をキャンセルしました";
                if (restoreEditor) ShowEditor();
                if (restoreGallery) { gallery?.Show(); gallery?.Activate(); }
            }
        }
        catch (Exception ex) { if (!closing) { ShowEditor(); MessageBox.Show(this, ex.Message, "撮影に失敗しました", MessageBoxButton.OK, MessageBoxImage.Warning); } }
        finally { capturing = false; }
    }
    private void ShowEditor()
    {
        if (closing) return;
        ShowInTaskbar = true;
        WindowActivation.Show(this);
    }
    private void HideToTray() { Hide(); ShowInTaskbar = false; }
    private void ShowGallery()
    {
        if (capturing || closing || !IsEnabled) return;
        if (gallery is null)
        {
            gallery = new GalleryWindow(entries, CopyEntryAsync, OpenEntry, () => _ = Capture(CaptureMode.Region));
            gallery.Closed += (_, _) => gallery = null;
        }
        WindowActivation.Show(gallery); toast?.Close();
    }
    private void OpenEntry(CaptureEntry entry)
    {
        if (!entries.Contains(entry)) return;
        History.SelectedItem = entry;
        ShowEditor();
        toast?.Close(); gallery?.Hide();
    }
    internal Task CopyEntryAsync(CaptureEntry entry)
        => copyToClipboard(AnnotationRenderer.Render(store.Original(entry), entry.Marks));
    internal CaptureEntry AddCapture(BitmapSource bitmap, string title, bool select = true)
    {
        var entry = new CaptureEntry { Title = title };
        store.Create(entry, bitmap); entries.Insert(0, entry);
        if (select) History.SelectedItem = entry;
        CountLabel.Text = $"{entries.Count} 枚"; StatusLabel.Text = "キャプチャを保存しました · ドラッグして注釈を追加";
        return entry;
    }
    private void HistoryChanged(object sender, SelectionChangedEventArgs e)
    {
        draft = null; moving = false; selectionDraft = null; selectedIndex = -1; Editor.ReleaseMouseCapture();
        current = History.SelectedItem as CaptureEntry;
        document = current is null ? null : new AnnotationDocument(current); UpdateSelectionControls();
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
        AnnotationRenderer.Draw(Editor, original, current.Marks.Select((mark, i) => i == selectedIndex && selectionDraft is not null ? selectionDraft : mark));
        if (draft is not null) AnnotationRenderer.Add(Editor, draft);
        if (selectedIndex >= 0 && selectedIndex < current.Marks.Count) AnnotationSelection.Draw(Editor, selectedIndex, selectionDraft ?? current.Marks[selectedIndex]);
    }
    private void Commit()
    {
        if (current is null || original is null) return;
        Redraw();
        Run(() => { store.Save(current, AnnotationRenderer.Render(original, current.Marks)); StatusLabel.Text = "保存済み · " + current.Marks.Count + " 件の注釈"; });
    }
    private void ChooseTool(object sender, RoutedEventArgs e) => SetTool((string)((Button)sender).Tag);
    private void SetTool(string value)
    {
        CancelGesture();
        tool = value;
        if (value != "Select") { selectedIndex = -1; UpdateSelectionControls(); Redraw(); }
        Editor.Cursor = value == "Select" ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.Cross;
        foreach (var button in Tools.Children.OfType<Button>())
        {
            bool active = (string)button.Tag == value;
            button.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(active ? "#EEF0FF" : "#FFFFFF"));
            button.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(active ? "#8183F6" : "#E2E8F0"));
        }
        UpdateArrowControls();
    }
    private void ChooseColor(object sender, RoutedEventArgs e)
    {
        color = (string)((Button)sender).Tag; CurrentColor.Fill = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(color)!;
        if (SelectedMark is { } selected && document is not null)
        { document.Change(marks => marks[selectedIndex] = selected with { Color = color }); Commit(); }
    }
    private Mark? SelectedMark => current is not null && selectedIndex >= 0 && selectedIndex < current.Marks.Count ? current.Marks[selectedIndex] : null;
    private void UpdateSelectionControls()
    {
        if (EditTextButton is null) return;
        var mark = SelectedMark;
        EditTextButton.IsEnabled = mark?.Tool is "Text" or "Number";
        DeleteMarkButton.IsEnabled = mark is not null;
        UpdateArrowControls();
        SelectionLabel.Text = mark is null ? "V で注釈を選択 · ドラッグで移動 · 文字はダブルクリックで編集" : "選択中 · ドラッグで移動 / 端の丸でサイズ変更 · Delete で注釈を削除";
        if (mark is null) return;
        syncingSelection = true;
        StrokeSlider.Value = mark.Width; color = mark.Color; CurrentColor.Fill = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(color)!;
        syncingSelection = false;
    }
    private void UpdateArrowControls()
    {
        if (ArrowStyleBox is null) return;
        ArrowStyleBox.IsEnabled = tool == "Arrow" || SelectedMark?.Tool == "Arrow";
        if (SelectedMark is not { Tool: "Arrow" } selected) return;
        arrowStyle = ArrowStyles.Options.Any(option => option.Key == selected.ArrowStyle) ? selected.ArrowStyle : "Open";
        bool wasSyncing = syncingSelection; syncingSelection = true; ArrowStyleBox.SelectedValue = arrowStyle; syncingSelection = wasSyncing;
    }
    private void ArrowStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncingSelection || ArrowStyleBox.SelectedItem is not ArrowStyleOption option) return;
        arrowStyle = option.Key;
        if (SelectedMark is not { Tool: "Arrow" } selected || document is null || selected.ArrowStyle == arrowStyle) return;
        CancelGesture(); document.Change(marks => marks[selectedIndex] = selected with { ArrowStyle = arrowStyle }); Commit();
    }
    internal void SelectAnnotation(int index)
    {
        selectedIndex = current is not null && index >= 0 && index < current.Marks.Count ? index : -1;
        SetTool("Select"); UpdateSelectionControls(); Redraw();
    }
    private void StrokeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (syncingSelection || SelectedMark is not { } selected || document is null) return;
        document.Change(marks => marks[selectedIndex] = selected with { Width = e.NewValue }); Commit();
    }
    private Point Position(MouseEventArgs e)
    {
        var p = e.GetPosition(Editor); return new Point(Math.Clamp(p.X, 0, Editor.Width), Math.Clamp(p.Y, 0, Editor.Height));
    }
    private void BeginDraw(object sender, MouseButtonEventArgs e)
    {
        if (current is null || original is null || document is null) return;
        var point = Position(e);
        if (e.ClickCount == 2)
        {
            selectedIndex = AnnotationSelection.HitTest(Editor, current.Marks, point);
            SetTool("Select"); UpdateSelectionControls(); Redraw();
            if (SelectedMark?.Tool is "Text" or "Number") EditSelectedText(this, e);
            e.Handled = true; return;
        }
        if (tool == "Select")
        {
            dragPart = "Move";
            if (SelectedMark is { } selected && AnnotationSelection.HasEndpoints(selected) && (point - new Point(selected.X, selected.Y)).Length <= 10) dragPart = "Start";
            else if (SelectedMark is { } endMark && AnnotationSelection.HasEndpoints(endMark) && (point - new Point(endMark.X2, endMark.Y2)).Length <= 10) dragPart = "End";
            else selectedIndex = AnnotationSelection.HitTest(Editor, current.Marks, point);
            UpdateSelectionControls(); Redraw();
            if (SelectedMark is not { } chosen) return;
            moving = true; dragStart = point; dragOriginal = chosen; selectionDraft = chosen;
            Editor.CaptureMouse(); e.Handled = true; return;
        }
        var mark = new Mark { Tool = tool, ArrowStyle = arrowStyle, Color = color, Width = StrokeSlider.Value, X = point.X, Y = point.Y, X2 = point.X, Y2 = point.Y };
        if (tool == "Text")
        {
            var text = AskText();
            if (string.IsNullOrWhiteSpace(text)) return;
            document.Change(marks => marks.Add(mark with { Text = text })); Commit(); return;
        }
        if (tool == "Number")
        {
            document.Change(marks => marks.Add(mark with { Text = (current.Marks.Count(m => m.Tool == "Number") + 1).ToString() })); Commit(); return;
        }
        draft = mark;
        if (tool == "Pen") draft.Points.Add([point.X, point.Y]);
        Editor.CaptureMouse(); e.Handled = true;
    }
    private void MoveDraw(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (moving && dragOriginal is not null)
        {
            selectionDraft = TransformSelection(Position(e));
            Redraw(); return;
        }
        if (draft is null) return;
        var p = Position(e); draft = draft with { X2 = p.X, Y2 = p.Y };
        if (draft.Tool == "Pen") draft.Points.Add([p.X, p.Y]);
        Redraw();
    }
    private void EndDraw(object sender, MouseButtonEventArgs e)
    {
        if (moving && selectionDraft is not null && document is not null)
        {
            var mark = TransformSelection(Position(e)); moving = false; selectionDraft = null; Editor.ReleaseMouseCapture();
            if ((Position(e) - dragStart).Length >= 0.5) { document.Change(marks => marks[selectedIndex] = mark); Commit(); }
            else Redraw();
            return;
        }
        if (draft is null || current is null || document is null) return;
        var p = Position(e); var completed = draft with { X2 = p.X, Y2 = p.Y }; draft = null; Editor.ReleaseMouseCapture();
        if (Math.Abs(completed.X2 - completed.X) + Math.Abs(completed.Y2 - completed.Y) >= 3 || completed.Points.Count > 2)
        { document.Change(marks => marks.Add(completed)); Commit(); }
        else Redraw();
    }
    private Mark TransformSelection(Point position) => dragPart switch
    {
        "Start" => dragOriginal! with { X = position.X, Y = position.Y },
        "End" => dragOriginal! with { X2 = position.X, Y2 = position.Y },
        _ => AnnotationDocument.Move(dragOriginal!, position - dragStart)
    };
    private void LostCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (draft is null && !moving) return;
        draft = null; moving = false; selectionDraft = null; Redraw();
    }
    private void CancelGesture()
    {
        if (draft is null && !moving) return;
        draft = null; moving = false; selectionDraft = null; dragOriginal = null;
        Editor.ReleaseMouseCapture(); Redraw();
    }
    private string? AskText(string initialText = "")
    {
        var dialog = new TextDialog(initialText) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Input.Text.Trim() : null;
    }
    private void EditSelectedText(object sender, RoutedEventArgs e)
    {
        if (SelectedMark is not { } selected || document is null || selected.Tool is not ("Text" or "Number")) return;
        var text = AskText(selected.Text); if (string.IsNullOrWhiteSpace(text)) return;
        document.Change(marks => marks[selectedIndex] = selected with { Text = text }); Commit();
    }
    private void DeleteSelected(object sender, RoutedEventArgs e)
    {
        if (SelectedMark is null || document is null) return;
        CancelGesture();
        document.Change(marks => marks.RemoveAt(selectedIndex)); selectedIndex = -1; UpdateSelectionControls(); Commit();
    }
    private void Undo(object? sender, RoutedEventArgs? e)
    {
        CancelGesture();
        if (document?.Undo() != true) return;
        selectedIndex = -1; UpdateSelectionControls(); Commit();
    }
    private void Redo(object? sender, RoutedEventArgs? e)
    {
        CancelGesture();
        if (document?.Redo() != true) return;
        selectedIndex = -1; UpdateSelectionControls(); Commit();
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
    private async void CopyImage(object? sender, RoutedEventArgs? e)
    {
        if (original is null || current is null) return;
        try { await CopyEntryAsync(current); StatusLabel.Text = "注釈付きの画像をコピーしました"; }
        catch (Exception ex) { StatusLabel.Text = "コピーできませんでした · " + ex.Message; }
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
            if (e.Key == Key.Escape) { draft = null; moving = false; selectionDraft = null; selectedIndex = -1; Editor.ReleaseMouseCapture(); UpdateSelectionControls(); Redraw(); }
            if (e.Key == Key.Delete) { DeleteSelected(this, e); e.Handled = true; }
            var value = e.Key switch { Key.V => "Select", Key.A => "Arrow", Key.R => "Rectangle", Key.E => "Ellipse", Key.P => "Pen", Key.H => "Highlight", Key.T => "Text", Key.N => "Number", _ => null };
            if (value is not null) SetTool(value);
        }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

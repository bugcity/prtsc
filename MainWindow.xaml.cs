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
    private readonly CaptureSettingsStore settingsStore;
    private CaptureSettings settings;
    private readonly CaptureDragExport dragExport;
    private readonly ThumbnailDrag historyDrag;
    private bool settingsOpen;
    private AnnotationDocument? document;
    private readonly Func<BitmapSource, Task> copyToClipboard;
    private readonly bool enableShell;
    private TrayController? tray;
    private GalleryWindow? gallery;
    private CaptureToast? toast;
    private bool closing, exitRequested, syncingSelection, moving, restoringEditor;
    private WindowState lastEditorState = WindowState.Normal;
    private readonly HashSet<int> selectedIndices = [];
    private int primaryIndex = -1;
    private int selectedIndex { get => primaryIndex; set { primaryIndex = value; selectedIndices.Clear(); if (value >= 0) selectedIndices.Add(value); } }
    private Dictionary<int, Mark>? dragSelection;
    private string dragPart = "Move";
    private Point dragStart;
    private Mark? dragOriginal, selectionDraft;
    private CaptureEntry? current;
    private BitmapSource? original;
    private Mark? draft;
    private string tool = "Arrow", color = "#F43F5E";
    private string arrowStyle = "Tapered";
    private bool capturing;
    private bool fitToView = true, cropping;
    private double displayZoom = 1;
    private Point cropStart;
    private CropRegion? cropDraft;
    private HwndSource? hwndSource;
    private IntPtr hwnd;
    private const int HotkeyId = 4812;
    private int activeHotkeyId = HotkeyId;
    private bool hotkeyRegistered;

    public MainWindow(CaptureStore? captureStore = null, Func<BitmapSource, Task>? clipboardWriter = null, bool enableShell = true)
    {
        store = captureStore ?? new CaptureStore();
        settingsStore = new CaptureSettingsStore(Path.Combine(store.Root, "settings.json")); settings = settingsStore.Load();
        color = settings.Color; arrowStyle = settings.ArrowStyle;
        dragExport = new CaptureDragExport(store);
        copyToClipboard = clipboardWriter ?? ImageClipboard.CopyAsync; this.enableShell = enableShell;
        syncingSelection = true; InitializeComponent();
        StrokeSlider.Value = settings.StrokeWidth;
        CurrentColor.Fill = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(color)!;
        UpdateHotkeyLabel();
        ArrowStyleBox.ItemsSource = ArrowStyles.Options; ArrowStyleBox.SelectedValue = arrowStyle; syncingSelection = false;
        entries = new(store.Load()); History.ItemsSource = entries;
        historyDrag = new ThumbnailDrag(dragExport.Create, ex => StatusLabel.Text = "ドラッグできませんでした · " + ex.Message); historyDrag.Attach(History);
        Run(() => { dragExport.Cleanup(); EnforceCaptureLimit(); });
        CountLabel.Text = $"{entries.Count} 枚";
        if (entries.Count > 0) History.SelectedIndex = 0;
        SetTool("Arrow");
        SourceInitialized += (_, _) =>
        {
            if (!enableShell) return;
            tray = new TrayController(Dispatcher, ShowGallery, () => _ = Capture(CaptureMode.Region), () => _ = Capture(CaptureMode.Window), ShowEditor, RequestExit, () => _ = RecordVideo(), StopVideoRecording);
            hwnd = new WindowInteropHelper(this).Handle; hwndSource = HwndSource.FromHwnd(hwnd); hwndSource?.AddHook(WindowMessage);
            hotkeyRegistered = HotkeyBinding.Register(hwnd, activeHotkeyId, settings.HotkeyModifiers, settings.HotkeyKey);
            if (!hotkeyRegistered)
            {
                HotkeyLabel.Text = HotkeyBinding.Label(settings.HotkeyModifiers, settings.HotkeyKey) + "：登録できませんでした";
                HotkeyLabel.ToolTip = "他の撮影アプリ、または Windows の Print Screen キー設定を確認してください。撮影ボタンは利用できます。";
                StatusLabel.Text = "撮影ホットキーが競合しています。設定で変更できます。";
            }
            if (settings.StartWithWindows) Run(() => StartupRegistration.Apply(true));
        };
        StateChanged += (_, _) =>
        {
            if (restoringEditor) return;
            if (WindowState == WindowState.Minimized && tray is not null) HideToTray();
            else if (IsVisible && WindowState != WindowState.Minimized) lastEditorState = WindowState;
        };
        Closing += (_, e) =>
        {
            if (exitRequested) return;
            e.Cancel = true; CancelGesture(); HideToTray();
        };
        Closed += (_, _) =>
        {
            foreach (var player in videoPlayers.Values.ToArray()) player.Close();
            recording?.Dispose();
            closing = true; toast?.Close(); gallery?.Close(); tray?.Dispose();
            if (hwnd != IntPtr.Zero) UnregisterHotKey(hwnd, activeHotkeyId); hwndSource?.RemoveHook(WindowMessage);
            if (enableShell) System.Windows.Application.Current.Shutdown();
        };
        PreviewKeyDown += OnKeyDown;
    }
    internal void PrepareForShutdown() => exitRequested = true;
    internal void RequestExit()
    {
        if (closing) return;
        if (recordingBusy) { exitAfterRecording = true; recordingSetup?.Close(); StopVideoRecording(); return; }
        PrepareForShutdown(); Close();
    }
    private IntPtr WindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam.ToInt32() == activeHotkeyId) { handled = true; _ = Capture(CaptureMode.Region); }
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
        if (capturing || recordingBusy || settingsOpen || !IsEnabled || closing) return;
        capturing = true;
        bool restoreEditor = IsVisible && WindowState != WindowState.Minimized;
        bool restoreGallery = gallery?.IsVisible == true;
        try
        {
            toast?.Close(); gallery?.Hide(); Hide(); await Task.Delay(240);
            if (closing) return;
            var targets = mode == CaptureMode.FullScreen ? [] : await WindowCapture.EnumerateTargetsAsync();
            if (closing) return;
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
                if (restoreEditor)
                {
                    if (current is null) History.SelectedItem = entry;
                    ShowEditor();
                }
                else HideToTray();
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
        if (recordingBusy) { recordingControls?.Activate(); return; }
        if (closing || settingsOpen) return;
        restoringEditor = true;
        try
        {
            // Restore while still hidden, before changing taskbar ownership or showing the HWND.
            if (WindowState == WindowState.Minimized) WindowState = lastEditorState;
            ShowInTaskbar = true;
            WindowActivation.Show(this);
        }
        finally { restoringEditor = false; }
    }
    internal void HideToTray()
    {
        if (WindowState != WindowState.Minimized) lastEditorState = WindowState;
        Hide(); ShowInTaskbar = false;
        // A tray-hidden window must not retain a minimized show state for the next gallery edit.
        if (WindowState == WindowState.Minimized) WindowState = lastEditorState;
    }
    private void ShowGallery()
    {
        if (recordingBusy) { recordingControls?.Activate(); return; }
        if (capturing || closing || settingsOpen || !IsEnabled) return;
        if (gallery is null)
        {
            gallery = new GalleryWindow(entries, CopyEntryAsync, OpenEntry, () => _ = Capture(CaptureMode.Region), dragExport.Create, () => OpenSettings(this, new RoutedEventArgs()), () => _ = RecordVideo());
            gallery.Closed += (_, _) => gallery = null;
        }
        WindowActivation.Show(gallery); toast?.Close();
    }
    private void OpenEntry(CaptureEntry entry)
    {
        if (!entries.Contains(entry)) return;
        if (entry.IsVideo) { OpenVideo(entry); return; }
        History.SelectedItem = entry;
        ShowEditor();
        toast?.Close(); gallery?.Hide();
    }
    internal Task CopyEntryAsync(CaptureEntry entry)
        => entry.IsVideo ? ImageClipboard.CopyFilesAsync(dragExport.Create(entry)) : copyToClipboard(AnnotationRenderer.Render(store.Original(entry), entry.Marks, entry.Crop));
    internal CaptureEntry AddCapture(BitmapSource bitmap, string title, bool select = true)
    {
        var entry = new CaptureEntry { Title = title };
        store.Create(entry, bitmap); entries.Insert(0, entry);
        if (select) History.SelectedItem = entry;
        EnforceCaptureLimit();
        CountLabel.Text = $"{entries.Count} 枚"; StatusLabel.Text = "キャプチャを保存しました · ドラッグして注釈を追加";
        return entry;
    }
    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        if (settingsOpen || capturing || recordingBusy || closing) return;
        settingsOpen = true;
        try
        {
            var dialog = new SettingsDialog(settings.MaxCaptures, entries.Count, settings) { Owner = gallery?.IsVisible == true ? gallery : this };
            if (dialog.ShowDialog() == true) Run(() => ApplyApplicationSettings(settings with { MaxCaptures = dialog.Limit, HotkeyModifiers = dialog.HotkeyModifiers, HotkeyKey = dialog.HotkeyKey, StartWithWindows = dialog.StartWithWindows, RenderingMethod = dialog.RenderingMethod }));
        }
        finally { settingsOpen = false; }
    }
    internal void ApplyCaptureLimit(int limit)
    {
        var next = settings with { MaxCaptures = limit }; settingsStore.Save(next); settings = next;
        EnforceCaptureLimit(); StatusLabel.Text = limit == 0 ? "保存枚数：無制限" : $"保存枚数：最大 {limit} 枚";
    }
    private void EnforceCaptureLimit()
    {
        foreach (var entry in CaptureRetention.Excess(entries, settings.MaxCaptures))
        {
            CloseVideo(entry); store.Delete(entry); entries.Remove(entry);
        }
        CountLabel.Text = $"{entries.Count} 枚";
    }
    private void HistoryChanged(object sender, SelectionChangedEventArgs e)
    {
        draft = null; moving = false; cropping = false; cropDraft = null; fitToView = true; selectionDraft = null; selectedIndex = -1; Editor.ReleaseMouseCapture();
        dragOriginal = null; dragSelection = null; original = null;
        Editor.Children.Clear(); VideoThumbnail.Source = null;
        current = History.SelectedItem as CaptureEntry;
        document = current is null || current.IsVideo ? null : new AnnotationDocument(current); UpdateSelectionControls();
        VideoPreview.Visibility = Visibility.Collapsed; AnnotationProperties.IsEnabled = current?.IsVideo != true;
        foreach (UIElement child in Tools.Children) child.IsEnabled = current?.IsVideo != true || ReferenceEquals(child, DeleteCaptureButton);
        ExportButton.Content = current?.IsVideo == true ? "MP4 を保存 ↗" : "PNG を保存 ↗";
        DeleteCaptureButton.Content = current?.IsVideo == true ? "動画を削除" : "画像を削除";
        if (current?.IsVideo == true)
        {
            original = null; CropActions.Visibility = ImageView.Visibility = EmptyState.Visibility = Visibility.Collapsed;
            VideoPreview.Visibility = Visibility.Visible; VideoThumbnail.Source = current.Thumbnail;
            TitleLabel.Text = current.Title; DetailLabel.Text = $"{current.VideoWidth} × {current.VideoHeight} px · MP4 · {TimeSpan.FromSeconds(current.DurationSeconds):hh\\:mm\\:ss}";
            StatusLabel.Text = "録画済みの動画 · 再生・MP4保存・ファイルとしてコピーできます"; return;
        }
        if (current is null) { original = null; CropActions.Visibility = Visibility.Collapsed; ImageView.Visibility = Visibility.Collapsed; EmptyState.Visibility = Visibility.Visible; TitleLabel.Text = "キャプチャを、伝わる一枚に。"; DetailLabel.Text = "撮影して、書き込んで、すぐ共有。"; return; }
        Run(() =>
        {
            original = store.Original(current); TitleLabel.Text = current.Title;
            UpdateImageDetail();
            EmptyState.Visibility = Visibility.Collapsed; ImageView.Visibility = Visibility.Visible; Redraw();
            StatusLabel.Text = "注釈は自動保存されます · A 矢印 / R 四角 / T 文字";
        });
    }
    private void Redraw()
    {
        if (original is null || current is null) return;
        AnnotationRenderer.Draw(Editor, original, current.Marks.Select((mark, i) => PreviewMark(i, mark)), current.Crop);
        if (draft is not null) AnnotationRenderer.Add(Editor, ImageCrop.DisplayMark(draft, current.Crop), Math.Max(1, original.PixelWidth - draft.X));
        foreach (int index in selectedIndices.OrderBy(i => i))
            if (index < current.Marks.Count) AnnotationSelection.Draw(Editor, index, ImageCrop.DisplayMark(PreviewMark(index, current.Marks[index]), current.Crop), selectedIndices.Count == 1);
        CropActions.Visibility = tool == "Crop" ? Visibility.Visible : Visibility.Collapsed;
        ApplyCropButton.IsEnabled = cropDraft is { Width: > 0, Height: > 0 };
        if (tool == "Crop" && cropDraft is not null) DrawCropOverlay(cropDraft);
        if (fitToView) FitDisplay();
    }
    private void Commit()
    {
        if (current is null || original is null) return;
        Redraw();
        Run(() => { store.Save(current, AnnotationRenderer.Render(original, current.Marks, current.Crop)); UpdateImageDetail(); StatusLabel.Text = "保存済み · " + current.Marks.Count + " 件の注釈"; });
    }
    private void UpdateImageDetail()
    {
        if (original is null || current is null) return;
        var bounds = ImageCrop.Bounds(original, current.Crop);
        DetailLabel.Text = $"{bounds.Width:N0} × {bounds.Height:N0} px  ·  {(current.Crop is null ? "" : "トリム済み · ")}{current.Created:yyyy/MM/dd HH:mm}";
    }
    internal void SetDisplayZoom(double scale)
    {
        if (!double.IsFinite(scale)) throw new ArgumentOutOfRangeException(nameof(scale));
        fitToView = false; ApplyDisplayZoom(Math.Clamp(scale, 0.01, 8));
    }
    private void ApplyDisplayZoom(double scale)
    {
        displayZoom = scale; EditorScale.ScaleX = scale; EditorScale.ScaleY = scale;
        ZoomLabel.Text = $"{scale * 100:0}%";
    }
    private void FitDisplay()
    {
        if (original is null || current is null || EditorViewport.ActualWidth <= 34 || EditorViewport.ActualHeight <= 34) return;
        var bounds = ImageCrop.Bounds(original, current.Crop);
        ApplyDisplayZoom(Math.Clamp(Math.Min((EditorViewport.ActualWidth - 34) / bounds.Width, (EditorViewport.ActualHeight - 34) / bounds.Height), 0.01, 8));
    }
    private void ViewportChanged(object sender, SizeChangedEventArgs e) { if (fitToView) FitDisplay(); }
    private void ZoomIn(object sender, RoutedEventArgs e) => SetDisplayZoom(displayZoom * 1.25);
    private void ZoomOut(object sender, RoutedEventArgs e) => SetDisplayZoom(displayZoom / 1.25);
    private void ZoomActual(object sender, RoutedEventArgs e) => SetDisplayZoom(1);
    private void ZoomFit(object sender, RoutedEventArgs e) { fitToView = true; FitDisplay(); }
    private void ZoomWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || original is null) return;
        var anchor = e.GetPosition(Editor); var cursor = e.GetPosition(ImageView);
        SetDisplayZoom(displayZoom * (e.Delta > 0 ? 1.15 : 1 / 1.15)); ImageView.UpdateLayout();
        var mapped = Editor.TranslatePoint(anchor, ImageView);
        ImageView.ScrollToHorizontalOffset(ImageView.HorizontalOffset + mapped.X - cursor.X);
        ImageView.ScrollToVerticalOffset(ImageView.VerticalOffset + mapped.Y - cursor.Y); e.Handled = true;
    }
    private int HitTestAnnotation(Point point)
    {
        if (current is null) return -1;
        var offset = new Vector(current.Crop?.X ?? 0, current.Crop?.Y ?? 0);
        return AnnotationSelection.HitTest(Editor, current.Marks.Select(mark => ImageCrop.DisplayMark(mark, current.Crop)).ToList(), point - offset);
    }
    private void DrawCropOverlay(CropRegion crop)
    {
        double x = crop.X - (current?.Crop?.X ?? 0), y = crop.Y - (current?.Crop?.Y ?? 0);
        void Area(double left, double top, double width, double height)
        {
            var shade = new System.Windows.Shapes.Rectangle { Width = Math.Max(0, width), Height = Math.Max(0, height), Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(115, 15, 23, 42)), IsHitTestVisible = false };
            Canvas.SetLeft(shade, left); Canvas.SetTop(shade, top); Editor.Children.Add(shade);
        }
        Area(0, 0, Editor.Width, y); Area(0, y + crop.Height, Editor.Width, Editor.Height - y - crop.Height);
        Area(0, y, x, crop.Height); Area(x + crop.Width, y, Editor.Width - x - crop.Width, crop.Height);
        var frame = new System.Windows.Shapes.Rectangle { Width = crop.Width, Height = crop.Height, Stroke = Brushes.White, StrokeThickness = 2 / displayZoom, IsHitTestVisible = false };
        Canvas.SetLeft(frame, x); Canvas.SetTop(frame, y); Editor.Children.Add(frame);
    }
    internal void ApplyCrop(CropRegion crop)
    {
        if (current is null || original is null || document is null) return;
        var valid = ImageCrop.Bounds(original, crop); var visible = ImageCrop.Bounds(original, current.Crop);
        if (valid.X < visible.X || valid.Y < visible.Y || valid.X + valid.Width > visible.X + visible.Width || valid.Y + valid.Height > visible.Y + visible.Height)
            throw new ArgumentException("現在の表示範囲内でトリムしてください。", nameof(crop));
        CancelGesture(); document.Crop(crop); selectedIndex = -1; SetTool("Select"); UpdateSelectionControls();
        fitToView = true; Commit();
    }
    private void ApplyCropClick(object sender, RoutedEventArgs e) { if (cropDraft is { Width: > 0, Height: > 0 } crop) Run(() => ApplyCrop(crop)); }
    private void CancelCropClick(object sender, RoutedEventArgs e) => SetTool("Select");
    private void ChooseTool(object sender, RoutedEventArgs e) => SetTool((string)((Button)sender).Tag);
    private void SetTool(string value)
    {
        CancelGesture();
        tool = value;
        if (value != "Select")
        {
            selectedIndex = -1; UpdateSelectionControls();
            syncingSelection = true; StrokeSlider.Value = settings.StrokeWidth; color = settings.Color; arrowStyle = settings.ArrowStyle;
            CurrentColor.Fill = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(color)!;
            ArrowStyleBox.SelectedValue = arrowStyle; syncingSelection = false; Redraw();
        }
        Editor.Cursor = value == "Select" ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.Cross;
        foreach (var button in Tools.Children.OfType<Button>())
        {
            if (button.Tag is not string tag) continue;
            bool active = tag == value;
            button.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(active ? "#EEF0FF" : "#FFFFFF"));
            button.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(active ? "#8183F6" : "#E2E8F0"));
        }
        UpdateArrowControls();
        Redraw();
    }
    private void ChooseColor(object sender, RoutedEventArgs e)
    {
        color = (string)((Button)sender).Tag; CurrentColor.Fill = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(color)!;
        SavePreferences(settings with { Color = color });
        ChangeSelected(mark => mark with { Color = color });
    }
    private Mark? SelectedMark => current is not null && selectedIndex >= 0 && selectedIndex < current.Marks.Count ? current.Marks[selectedIndex] : null;
    private void UpdateSelectionControls()
    {
        if (EditTextButton is null) return;
        var mark = SelectedMark;
        EditTextButton.IsEnabled = selectedIndices.Count == 1 && (mark?.Tool is "Text" or "Number");
        DeleteMarkButton.IsEnabled = mark is not null;
        UpdateArrowControls();
        SelectionLabel.Text = mark is null ? "Ctrl/Shift＋クリックで複数選択 · Ctrl+A ですべて選択" : $"{selectedIndices.Count} 件選択 · Ctrl+D 複製 · 矢印キーで移動（Shiftで10px）";
        if (mark is null) return;
        syncingSelection = true;
        StrokeSlider.Value = mark.Width; color = mark.Color; CurrentColor.Fill = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(color)!;
        syncingSelection = false;
    }
    private void UpdateArrowControls()
    {
        if (ArrowStyleBox is null) return;
        ArrowStyleBox.IsEnabled = tool == "Arrow" || current is not null && selectedIndices.Any(i => current.Marks[i].Tool == "Arrow");
        if (SelectedMark is not { Tool: "Arrow" } selected) return;
        arrowStyle = ArrowStyles.Options.Any(option => option.Key == selected.ArrowStyle) ? selected.ArrowStyle : "Open";
        bool wasSyncing = syncingSelection; syncingSelection = true; ArrowStyleBox.SelectedValue = arrowStyle; syncingSelection = wasSyncing;
    }
    private void ArrowStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncingSelection || ArrowStyleBox.SelectedItem is not ArrowStyleOption option) return;
        arrowStyle = option.Key;
        SavePreferences(settings with { ArrowStyle = arrowStyle });
        ChangeSelected(mark => mark.Tool == "Arrow" ? mark with { ArrowStyle = arrowStyle } : mark);
    }
    internal void SelectAnnotation(int index)
    {
        selectedIndex = current is not null && index >= 0 && index < current.Marks.Count ? index : -1;
        SetTool("Select"); UpdateSelectionControls(); Redraw();
    }
    private void StrokeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (syncingSelection) return;
        if (settings.StrokeWidth != e.NewValue)
            Run(() => { var next = settings with { StrokeWidth = e.NewValue }; settingsStore.Save(next); settings = next; });
        ChangeSelected(mark => mark.Tool == "Text" ? mark : mark with { Width = e.NewValue });
    }
    private Point Position(MouseEventArgs e)
    {
        var p = e.GetPosition(Editor); return new Point(Math.Clamp(p.X, 0, Editor.Width) + (current?.Crop?.X ?? 0), Math.Clamp(p.Y, 0, Editor.Height) + (current?.Crop?.Y ?? 0));
    }
    private void BeginDraw(object sender, MouseButtonEventArgs e)
    {
        if (current is null || original is null || document is null) return;
        var point = Position(e);
        if (tool == "Crop")
        {
            cropStart = point; cropping = true; cropDraft = null; Editor.CaptureMouse(); e.Handled = true; return;
        }
        if (e.ClickCount == 2)
        {
            selectedIndex = HitTestAnnotation(point);
            SetTool("Select"); UpdateSelectionControls(); Redraw();
            if (SelectedMark?.Tool is "Text" or "Number") EditSelectedText(this, e);
            e.Handled = true; return;
        }
        if (tool == "Select")
        {
            dragPart = "Move";
            if (Keyboard.Modifiers is ModifierKeys.Control or ModifierKeys.Shift)
            {
                ToggleSelection(HitTestAnnotation(point)); e.Handled = true; return;
            }
            if (selectedIndices.Count == 1 && SelectedMark is { } selected && AnnotationSelection.HasEndpoints(selected) && (point - AnnotationDocument.Endpoints(selected).Start).Length <= 10) dragPart = "Start";
            else if (selectedIndices.Count == 1 && SelectedMark is { } endMark && AnnotationSelection.HasEndpoints(endMark) && (point - AnnotationDocument.Endpoints(endMark).End).Length <= 10) dragPart = "End";
            else { int hit = HitTestAnnotation(point); if (!selectedIndices.Contains(hit)) selectedIndex = hit; }
            UpdateSelectionControls(); Redraw();
            if (SelectedMark is not { } chosen) return;
            moving = true; dragStart = point; dragOriginal = chosen; selectionDraft = chosen;
            dragSelection = selectedIndices.ToDictionary(i => i, i => current.Marks[i]);
            Editor.CaptureMouse(); e.Handled = true; return;
        }
        var mark = new Mark { Tool = tool, ArrowStyle = arrowStyle, Color = color, Width = StrokeSlider.Value, TextStyle = tool == "Text" ? settings.TextStyle : null, X = point.X, Y = point.Y, X2 = point.X, Y2 = point.Y };
        if (tool == "Text")
        {
            var result = AskText(initialStyle: settings.TextStyle, textColor: color);
            if (result is null) return;
            document.Change(marks => marks.Add(mark with { Text = result.Value.Text, TextStyle = result.Value.Style })); Commit(); return;
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
        if (cropping && original is not null)
        { cropDraft = ImageCrop.FromPoints(cropStart, Position(e), ImageCrop.Bounds(original, current?.Crop)); Redraw(); return; }
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
        if (cropping && original is not null)
        {
            cropDraft = ImageCrop.FromPoints(cropStart, Position(e), ImageCrop.Bounds(original, current?.Crop));
            cropping = false; Editor.ReleaseMouseCapture(); Redraw(); e.Handled = true; return;
        }
        if (moving && selectionDraft is not null && document is not null)
        {
            var mark = TransformSelection(Position(e)); var delta = Position(e) - dragStart;
            moving = false; selectionDraft = null; Editor.ReleaseMouseCapture();
            if (delta.Length >= 0.5) { document.Change(marks => { foreach (var pair in dragSelection!) marks[pair.Key] = dragPart == "Move" ? AnnotationDocument.Move(pair.Value, delta) : mark; }); Commit(); }
            else Redraw();
            dragSelection = null; dragOriginal = null;
            return;
        }
        if (draft is null || current is null || document is null) return;
        var p = Position(e); var completed = draft with { X2 = p.X, Y2 = p.Y }; draft = null; Editor.ReleaseMouseCapture();
        if (completed.Tool == "Pen" && (completed.Points.Count == 0 || (new Point(completed.Points[^1][0], completed.Points[^1][1]) - p).Length > 0.01))
            completed.Points.Add([p.X, p.Y]);
        if (Math.Abs(completed.X2 - completed.X) + Math.Abs(completed.Y2 - completed.Y) >= 3 || completed.Points.Count > 2)
        { document.Change(marks => marks.Add(completed)); Commit(); }
        else Redraw();
    }
    private Mark TransformSelection(Point position) => dragPart switch
    {
        "Start" => AnnotationDocument.ResizeEndpoint(dragOriginal!, true, position),
        "End" => AnnotationDocument.ResizeEndpoint(dragOriginal!, false, position),
        _ => AnnotationDocument.Move(dragOriginal!, position - dragStart)
    };
    private void LostCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (draft is null && !moving && !cropping) return;
        draft = null; moving = false; cropping = false; cropDraft = null; selectionDraft = null; dragOriginal = null; dragSelection = null; Redraw();
    }
    private void CancelGesture()
    {
        if (draft is null && !moving && !cropping && cropDraft is null) return;
        draft = null; moving = false; cropping = false; cropDraft = null; selectionDraft = null; dragOriginal = null; dragSelection = null;
        Editor.ReleaseMouseCapture(); Redraw();
    }
    private (string Text, TextStyle? Style)? AskText(string initialText = "", TextStyle? initialStyle = null, string? textColor = null, bool showFormatting = true)
    {
        var initial = initialStyle ?? settings.TextStyle;
        var dialog = new TextDialog(initialText, initial, textColor ?? color, showFormatting) { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Input.Text)) return null;
        if (dialog.TextFormat is { } format && format != initial) SavePreferences(settings with { TextStyle = format });
        return (dialog.Input.Text.Trim(), dialog.TextFormat);
    }
    private void EditSelectedText(object sender, RoutedEventArgs e)
    {
        if (SelectedMark is not { } selected || document is null || selected.Tool is not ("Text" or "Number")) return;
        var initial = selected.TextStyle ?? new TextStyle { Size = Math.Max(18, selected.Width * 6) };
        var result = AskText(selected.Text, initial, selected.Color, selected.Tool == "Text"); if (result is null) return;
        var style = selected.TextStyle is null && result.Value.Style == initial ? null : result.Value.Style;
        document.Change(marks => marks[selectedIndex] = selected with { Text = result.Value.Text, TextStyle = style }); Commit();
    }
    private void DeleteSelected(object sender, RoutedEventArgs e)
    {
        if (SelectedMark is null || document is null) return;
        CancelGesture();
        var indices = selectedIndices.OrderByDescending(i => i).ToArray();
        document.Change(marks => { foreach (int i in indices) marks.RemoveAt(i); }); selectedIndex = -1; UpdateSelectionControls(); Commit();
    }
    private void Undo(object? sender, RoutedEventArgs? e)
    {
        CancelGesture();
        var previousCrop = current?.Crop;
        if (document?.Undo() != true) return;
        selectedIndex = -1; UpdateSelectionControls(); if (current?.Crop != previousCrop) fitToView = true; Commit();
    }
    private void Redo(object? sender, RoutedEventArgs? e)
    {
        CancelGesture();
        var previousCrop = current?.Crop;
        if (document?.Redo() != true) return;
        selectedIndex = -1; UpdateSelectionControls(); if (current?.Crop != previousCrop) fitToView = true; Commit();
    }
    private void OpenImage(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" };
        if (dialog.ShowDialog(this) == true) Import(dialog.FileName);
    }
    private void Import(string path) => Run(() => AddCapture(CaptureStore.ReadImage(path), Path.GetFileNameWithoutExtension(path)));
    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(CaptureDragExport.CaptureIdFormat)) { e.Handled = true; return; }
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
            foreach (var path in paths.Where(p => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" }.Contains(Path.GetExtension(p).ToLowerInvariant()))) Import(path);
    }
    private async void CopyImage(object? sender, RoutedEventArgs? e)
    {
        if (current is null || original is null && !current.IsVideo) return;
        var entry = current;
        try { await CopyEntryAsync(entry); StatusLabel.Text = entry.IsVideo ? "動画ファイルをコピーしました" : "注釈付きの画像をコピーしました"; }
        catch (Exception ex) { StatusLabel.Text = "コピーできませんでした · " + ex.Message; }
    }
    private void ExportImage(object sender, RoutedEventArgs e)
    {
        if (current?.IsVideo == true) { SaveVideo(current); return; }
        if (original is null || current is null) return;
        var name = string.Concat(current.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG 画像|*.png", FileName = name + ".png", AddExtension = true };
        if (dialog.ShowDialog(this) == true) Run(() => { CaptureStore.WriteImage(dialog.FileName, AnnotationRenderer.Render(original, current.Marks, current.Crop)); StatusLabel.Text = "保存しました · " + dialog.FileName; });
    }
    private void DeleteCapture(object sender, RoutedEventArgs e)
    {
        if (current is null) return;
        if (MessageBox.Show(this, "このキャプチャを履歴から削除しますか？", "キャプチャを削除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Run(() => { var entry = current; CloseVideo(entry); store.Delete(entry); entries.Remove(entry); if (entries.Count > 0) History.SelectedIndex = 0; CountLabel.Text = $"{entries.Count} 枚"; });
    }
    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
        if (Keyboard.FocusedElement is System.Windows.Controls.Slider or System.Windows.Controls.ComboBox && e.Key is Key.Left or Key.Right or Key.Up or Key.Down) return;
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
                case Key.A: SelectAllAnnotations(); break;
                case Key.D: DuplicateSelected(this, e); break;
                case Key.PageUp: BringForward(this, e); break;
                case Key.PageDown: SendBackward(this, e); break;
                case Key.Home: BringToFront(this, e); break;
                case Key.End: SendToBack(this, e); break;
                case Key.D0: ZoomFit(this, e); break;
                case Key.D1: ZoomActual(this, e); break;
                case Key.Add: case Key.OemPlus: ZoomIn(this, e); break;
                case Key.Subtract: case Key.OemMinus: ZoomOut(this, e); break;
                default: return;
            }
            e.Handled = true;
        }
        else if (Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            if (selectedIndices.Count > 0 && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
            {
                double step = Keyboard.Modifiers == ModifierKeys.Shift ? 10 : 1;
                NudgeSelection(new Vector(e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0, e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0)); e.Handled = true; return;
            }
            if (Keyboard.Modifiers != ModifierKeys.None) return;
            if (e.Key == Key.Escape) { CancelGesture(); selectedIndex = -1; if (tool == "Crop") SetTool("Select"); UpdateSelectionControls(); Redraw(); }
            if (e.Key == Key.Enter && tool == "Crop") { ApplyCropClick(this, e); e.Handled = true; }
            if (e.Key == Key.Delete) { DeleteSelected(this, e); e.Handled = true; }
            var value = e.Key switch { Key.V => "Select", Key.A => "Arrow", Key.R => "Rectangle", Key.E => "Ellipse", Key.P => "Pen", Key.H => "Highlight", Key.T => "Text", Key.N => "Number", Key.C => "Crop", _ => null };
            if (value is not null) SetTool(value);
        }
    }
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Prtsc;

internal static class SelfTest
{
    public static void Run()
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var rect = RegionWindow.PixelRect(new System.Windows.Point(150, 80), new System.Windows.Point(20, 10), 200, 100, 400, 200);
        Check(rect == new Int32Rect(40, 20, 260, 140), "Reverse drag must preserve pixel coordinates at 200% DPI");
        rect = RegionWindow.PixelRect(new System.Windows.Point(-40, -10), new System.Windows.Point(250, 110), 200, 100, 400, 200);
        Check(rect == new Int32Rect(0, 0, 400, 200), "Selection must clamp to image bounds");
        var desktopBounds = new System.Drawing.Rectangle(-1920, -200, 3840, 1280);
        var front = new WindowTarget(new IntPtr(1), "前面", new System.Drawing.Rectangle(-2000, -300, 800, 600));
        var back = new WindowTarget(new IntPtr(2), "背面", new System.Drawing.Rectangle(-1800, -100, 1200, 800));
        Check(WindowCapture.At([front, back], -1700, 0) == front, "Overlapping windows must select the frontmost candidate");
        Check(WindowCapture.At([front, back], -1000, 500) == back, "Exposed area of a background window must remain selectable");
        Check(WindowCapture.At([front, back], 1500, 500) is null, "Desktop background must not select a window");
        rect = WindowCapture.CropBounds(front.Bounds, desktopBounds);
        Check(rect == new Int32Rect(0, 0, 720, 500), "Window crop must clip negative desktop origins and off-screen borders");
        Check(WindowCapture.CropBounds(new System.Drawing.Rectangle(100, 50, 640, 480), desktopBounds) == new Int32Rect(2020, 250, 640, 480), "Window crop must offset screen pixels into the virtual desktop bitmap");
        Check(WindowCapture.CropBounds(new System.Drawing.Rectangle(5000, 0, 640, 480), desktopBounds).IsEmpty, "Off-screen windows must not produce an invalid crop");
        var image = BitmapSource.Create(640, 360, 96, 96, PixelFormats.Bgr32, null, Enumerable.Repeat((byte)240, 640 * 360 * 4).ToArray(), 640 * 4);
        ArrowStyleTests.Run(image);
        List<Mark> marks = [];
        string[] tools = ["Arrow", "Rectangle", "Ellipse", "Pen", "Highlight", "Text", "Number"];
        for (int i = 0; i < tools.Length; i++) marks.Add(new Mark { Tool = tools[i], X = 30 + i * 75, Y = 70, X2 = 90 + i * 75, Y2 = 180, Text = tools[i] == "Number" ? "1" : "テスト", Points = [[250, 250], [300, 230], [350, 260]] });
        var rendered = AnnotationRenderer.Render(image, marks);
        Check(rendered.PixelWidth == 640 && rendered.PixelHeight == 360, "Export dimensions changed");
        byte[] before = new byte[640 * 360 * 4], after = new byte[before.Length];
        new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0).CopyPixels(before, 640 * 4, 0); rendered.CopyPixels(after, 640 * 4, 0);
        Check(!before.SequenceEqual(after), "Annotations must modify exported pixels");
        var root = Path.Combine(Environment.CurrentDirectory, "prtsc-test-" + Guid.NewGuid().ToString("N"));
        var store = new CaptureStore(root); var entry = new CaptureEntry { Title = "テスト", Marks = marks };
        try
        {
            store.Create(entry, image); store.Save(entry, rendered);
            var loaded = store.Load(); Check(loaded.Count == 1 && loaded[0].Marks.Count == 7, "Annotation persistence failed");
            Check(loaded[0].Marks[3].Points.Count == 3, "Pen points not restored");
            Check(store.Original(entry).PixelWidth == 640, "Original lost");
            BitmapSource? clipboardImage = null;
            int clipboardCopies = 0;
            var window = new MainWindow(store, bitmap => { clipboardImage = bitmap; clipboardCopies++; return Task.CompletedTask; }, false);
            var surface = (System.Windows.FrameworkElement)window.Content;
            surface.Measure(new System.Windows.Size(1280, 800));
            surface.Arrange(new Rect(0, 0, 1280, 800)); surface.UpdateLayout();
            Check(((System.Windows.Controls.TextBlock)window.FindName("HotkeyLabel")).Text.Contains("PrtSc"), "Main window must display the current capture hotkey");
            static IEnumerable<System.Windows.Controls.Button> Buttons(DependencyObject node)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                {
                    var child = VisualTreeHelper.GetChild(node, i);
                    if (child is System.Windows.Controls.Button button) yield return button;
                    foreach (var nested in Buttons(child)) yield return nested;
                }
            }
            void Click(string label) => Buttons(surface).Single(b => b.Content as string == label).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(Buttons(surface).Any(b => b.Content as string == "▣  ウィンドウを選択して撮影"), "Window capture must be accessible from the sidebar");
            window.SelectAnnotation(0);
            Buttons(surface).Single(b => b.Tag as string == "#0EA5E9").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(store.Load()[0].Marks[0].Color == "#0EA5E9", "Changing a selected annotation must persist its new color");
            Click("↶"); Check(store.Load()[0].Marks[0].Color == "#F43F5E", "Undo must persist the previous color");
            Click("↷"); Check(store.Load()[0].Marks[0].Color == "#0EA5E9", "Redo must persist an existing annotation edit");
            window.SelectAnnotation(0);
            var arrowStyleBox = (System.Windows.Controls.ComboBox)window.FindName("ArrowStyleBox");
            arrowStyleBox.SelectedValue = "Tapered";
            Check(store.Load()[0].Marks[0].ArrowStyle == "Tapered", "Arrow style selection must modify and persist the existing arrow");
            Click("↶"); Check(store.Load()[0].Marks[0].ArrowStyle == "Open", "Undo must restore the original arrow style");
            Click("↷"); Check(store.Load()[0].Marks[0].ArrowStyle == "Tapered", "Redo must restore the chosen arrow style");
            window.SelectAnnotation(5);
            Check(!arrowStyleBox.IsEnabled, "Arrow styles must not modify selected text annotations");
            Check(((System.Windows.Controls.Button)window.FindName("EditTextButton")).IsEnabled, "Text loaded from history must be editable");
            Click("注釈を削除"); Check(store.Load()[0].Marks.Count == 6, "Deleting the selected annotation must persist");
            Click("↶"); Check(store.Load()[0].Marks.Count == 7, "Undo must restore a deleted annotation");
            var edited = (CaptureEntry)((System.Windows.Controls.ListBox)window.FindName("History")).SelectedItem;
            window.CopyEntryAsync(edited).GetAwaiter().GetResult();
            Check(clipboardImage is { PixelWidth: 640, PixelHeight: 360 } && edited.Marks.Count == 7, "Clipboard copy must produce an image and retain editable marks");
            byte[] expectedCopy = new byte[after.Length], actualCopy = new byte[after.Length];
            AnnotationRenderer.Render(store.Original(edited), edited.Marks).CopyPixels(expectedCopy, 640 * 4, 0);
            clipboardImage!.CopyPixels(actualCopy, 640 * 4, 0);
            Check(expectedCopy.SequenceEqual(actualCopy), "Clipboard image must contain all saved edits and exclude selection handles");
            window.SelectAnnotation(0); surface.UpdateLayout();
            var editorCanvas = (System.Windows.Controls.Canvas)window.FindName("Editor");
            Check(AnnotationSelection.HitTest(editorCanvas, edited.Marks, new System.Windows.Point(45, 97.5)) == 0, "Arrow shaft must be selectable");
            Check(!AnnotationSelection.Bounds(editorCanvas, 5).IsEmpty, "Text selection bounds must be measurable");
            Directory.CreateDirectory("artifacts");
            static void Preview(FrameworkElement element, double width, double height, string path)
            {
                element.Measure(new System.Windows.Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
                if (element.Parent is Window parent)
                {
                    var backdrop = new DrawingVisual(); using (var drawing = backdrop.RenderOpen()) drawing.DrawRectangle(parent.Background, null, new Rect(0, 0, width, height));
                    bitmap.Render(backdrop);
                }
                bitmap.Render(element);
                CaptureStore.WriteImage(Path.Combine("artifacts", path), bitmap);
            }
            Preview(surface, 1280, 800, "editor-preview.png");
            var dialog = new TextDialog("複数行のテキスト\n再編集できます。");
            var dialogContent = (FrameworkElement)dialog.Content;
            dialogContent.Measure(new System.Windows.Size(396, double.PositiveInfinity));
            double dialogHeight = Math.Ceiling(dialogContent.DesiredSize.Height);
            dialogContent.Arrange(new Rect(0, 0, 396, dialogHeight)); dialogContent.UpdateLayout();
            var buttonBounds = dialog.AcceptButton.TransformToAncestor(dialogContent).TransformBounds(new Rect(dialog.AcceptButton.RenderSize));
            Check(buttonBounds.Bottom <= dialogContent.ActualHeight && buttonBounds.Right <= dialogContent.ActualWidth, "Text dialog action must fit inside its auto-sized content");
            Check(dialog.Input.Text.Contains("再編集") && dialog.AcceptButton.Content as string == "更新", "Edit dialog must preserve existing text");
            Preview(dialogContent, 396, dialogHeight, "text-dialog-preview.png"); dialog.Close();
            var gallery = new GalleryWindow(new System.Collections.ObjectModel.ObservableCollection<CaptureEntry>([edited]), window.CopyEntryAsync, _ => { }, () => { });
            var galleryContent = (FrameworkElement)gallery.Content;
            Preview(galleryContent, 780, 550, "gallery-preview.png");
            Buttons(galleryContent).Single(b => b.Content as string == "コピー").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(clipboardCopies == 2, "Gallery copy button must copy a past capture without changing the editor"); gallery.Close();
            int opened = 0; bool toastDismissed = false;
            var toast = new CaptureToast(edited, true, () =>
            {
                Check(!toastDismissed, "Editor activation must run before dismissing the clicked notification"); opened++;
            });
            toast.Closed += (_, _) => toastDismissed = true;
            Preview((FrameworkElement)toast.Content, 326, 240, "toast-preview.png");
            toast.OpenEditor(); toast.OpenEditor(); Check(opened == 1 && CaptureToast.Lifetime == TimeSpan.FromSeconds(3), "Capture notification must open editing only once and fade over three seconds");
            static void Pump(int milliseconds)
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start();
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
            var fading = new CaptureToast(edited, true, () => { }); bool expired = false;
            fading.Closed += (_, _) => expired = true; fading.StartFade(); Pump(1500);
            Check(fading.Opacity > 0.2 && fading.Opacity < 0.8, "Capture preview opacity must decrease gradually halfway through the fade");
            Pump(1800); Check(expired, "Capture preview must close after the three-second fade");
            var doc = new AnnotationDocument(edited);
            var previous = edited.Marks[3]; var moved = AnnotationDocument.Move(previous, new Vector(20, -10));
            doc.Change(list => list[3] = moved);
            Check(edited.Marks[3].Points[0][0] == previous.Points[0][0] + 20 && previous.Points[0][0] == 250, "Moving a pen must preserve original point data");
            doc.Change(list => list[5] = list[5] with { Text = "変更したテキスト" });
            doc.Change(list => list[0] = list[0] with { X2 = 200, Y2 = 220 });
            store.Save(edited, AnnotationRenderer.Render(image, edited.Marks));
            var reopened = store.Load()[0];
            Check(reopened.Marks[5].Text == "変更したテキスト" && reopened.Marks[0].X2 == 200 && reopened.Marks[3].Points[0][0] == 270, "Text, arrow endpoints and pen positions must remain editable after reload");
            Check(doc.Undo() && edited.Marks[0].X2 == 90 && doc.Redo() && edited.Marks[0].X2 == 200, "Resize edits must support undo and redo");
            using var trayIcon = AppIcon.CreateTrayIcon(); Check(trayIcon.Width == 32, "Tray icon resource must contain a 32px icon");
            Check(surface.ActualWidth == 1280, "Main window layout failed");
            foreach (var state in new[] { WindowState.Normal, WindowState.Maximized })
            {
                window.WindowState = state; window.HideToTray();
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    window.WindowState = WindowState.Minimized; window.HideToTray();
                    Check(!window.IsVisible && !window.ShowInTaskbar && window.WindowState == state,
                        "Repeated minimize-to-tray must retain a restored state for gallery editing, including maximized editors");
                }
            }
            window.WindowState = WindowState.Normal;
            int closedCount = 0; window.Closed += (_, _) => closedCount++;
            window.Close();
            Check(closedCount == 0 && !window.IsVisible && !window.ShowInTaskbar, "Editor close must hide to tray instead of disposing the resident window");
            window.SelectAnnotation(0);
            Check(((System.Windows.Controls.Button)window.FindName("DeleteMarkButton")).IsEnabled, "Editor state must remain usable after closing to tray");
            int exitRequests = 0;
            using (var menu = TrayController.CreateMenu(window.Dispatcher, () => { }, () => { }, () => { }, () => { }, () => { exitRequests++; window.RequestExit(); }))
            {
                var exitItems = menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().Where(item => item.Text == "終了").ToArray();
                Check(exitItems.Length == 1, "Tray menu must contain one explicit Exit command");
                exitItems[0].PerformClick(); Pump(20);
                Check(exitRequests == 1 && closedCount == 1, "Tray Exit must bypass close-to-tray and actually close the editor");
            }
            window.RequestExit(); Check(closedCount == 1, "Repeated exit requests must be harmless");
            // The files must be unlocked after loading so deletion works on Windows.
            store.Delete(entry); Check(store.Load().Count == 0, "Deletion failed");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        File.WriteAllText("self-test-result.txt", "PASS: repeated minimize-to-tray state restoration (normal/maximized), editor close-to-tray and retained edit state, tray Exit, activation-before-toast-dismissal, 9 arrow styles and directional opacity, style persistence/Undo/Redo, capture geometry, annotation re-editing, flattened clipboard output, gallery copy, dialog bounds, notification fade, icon resources, UI layouts, unlocked deletion.");
    }
}

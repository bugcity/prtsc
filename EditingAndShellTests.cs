using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Button = System.Windows.Controls.Button;

namespace Prtsc;

internal static class EditingAndShellTests
{
    internal static void Run(string root, BitmapSource image)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static IEnumerable<Button> Buttons(DependencyObject node)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is Button b) yield return b;
                foreach (var nested in Buttons(child)) yield return nested;
            }
        }
        var store = new CaptureStore(root); var prefs = new CaptureSettingsStore(Path.Combine(root, "settings.json"));
        BitmapSource? copied = null;
        var window = new MainWindow(store, b => { copied = b; return Task.CompletedTask; }, false);
        try
        {
            var entry = window.AddCapture(image, "multi selection and typography");
            entry.Marks.AddRange([
                new Mark { Tool = "Text", X = 40, Y = 30, Text = "編集できる文字", Width = 4, Color = "#FF000000" },
                new Mark { Tool = "Arrow", X = 120, Y = 140, X2 = 260, Y2 = 220 },
                new Mark { Tool = "Pen", X = 10, Y = 10, X2 = 80, Y2 = 60, Points = [[10, 10], [30, 50], [80, 60]] }
            ]);
            void Layout() { var surface = (FrameworkElement)window.Content; surface.Measure(new System.Windows.Size(1280, 800)); surface.Arrange(new Rect(0, 0, 1280, 800)); surface.UpdateLayout(); }
            void Click(string label) => Buttons((DependencyObject)window.Content).Single(b => b.Content as string == label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.SelectAnnotation(0); Layout();
            var canvas = (Canvas)window.FindName("Editor"); var legacy = (TextBlock)canvas.Children[1];
            Check(legacy.FontSize == 24, "Legacy annotations must retain width-derived font size until formatted");
            ((Slider)window.FindName("StrokeSlider")).Value = 9;
            Check(((TextBlock)canvas.Children[1]).FontSize == 24 && entry.Marks[0].Width == 4, "Line thickness must never resize legacy text");
            var style = new TextStyle { Size = 38, FontFamily = "Consolas", Background = "#FFFFFFFF", BorderColor = "#FF00FF00", BorderWidth = 3 };
            window.ApplyTextStyle(style); Layout();
            var textFrame = (Border)canvas.Children[1]; var text = (TextBlock)textFrame.Child;
            Check(text.FontSize == 38 && text.FontFamily.Source == "Consolas" && textFrame.BorderThickness.Left == 3, "Font size, family and border must render independently");
            Check(((SolidColorBrush)textFrame.Background).Color == Colors.White && ((SolidColorBrush)textFrame.BorderBrush).Color == Colors.Lime, "Text backgrounds and borders must use requested colors");
            ((Slider)window.FindName("StrokeSlider")).Value = 6;
            Check(entry.Marks[0].TextStyle == style && entry.Marks[0].Width == 4 && prefs.Load().TextStyle == style, "Text styling must survive thickness changes and persist as drawing defaults");
            window.ToggleSelection(2); window.NudgeSelection(new Vector(-1, 10));
            Check(entry.Marks[0].X == 39 && entry.Marks[1].X == 120 && entry.Marks[2].Points[0].SequenceEqual(new double[] { 9, 20 }), "Multiple selection nudge must move only selected annotations and all pen points");
            Click("↶"); Check(entry.Marks[0].X == 40 && entry.Marks[2].X == 10, "Group edits must undo as one operation");
            Click("↷"); Check(entry.Marks[0].X == 39 && entry.Marks[2].X == 9, "Group edits must redo together");
            window.SelectAnnotation(0); window.ToggleSelection(2);
            var menu = Buttons((DependencyObject)window.Content).Single(b => b.Content as string == "編集操作 ▾").ContextMenu;
            var duplicate = menu.Items.OfType<MenuItem>().Single(m => m.Header as string == "複製");
            duplicate.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(entry.Marks.Count == 5 && entry.Marks[3].Tool == "Text" && entry.Marks[4].Tool == "Pen" && entry.Marks[3].X == 51, "Duplication must preserve selected order and offset the copies");
            Check(!ReferenceEquals(entry.Marks[2].Points, entry.Marks[4].Points), "Duplicating freehand marks must clone point data");
            window.ReorderSelection(false);
            Check(entry.Marks[0].X == 51 && entry.Marks[1].Tool == "Pen" && entry.Marks[2].X == 39, "Send to back must preserve group order");
            window.ReorderSelection(true); Check(entry.Marks[3].X == 51 && entry.Marks[4].Tool == "Pen", "Bring to front must preserve group order and selection");
            var beforeStep = entry.Marks.ToArray(); window.SelectAnnotation(0); window.ToggleSelection(2); window.StepSelection(true);
            Check(entry.Marks[1] == beforeStep[0] && entry.Marks[3] == beforeStep[2], "Bring forward must move each selected annotation one layer while retaining group order");
            window.StepSelection(false); Check(entry.Marks.SequenceEqual(beforeStep), "Send backward must move selected annotations one layer and keep their selection");
            window.SelectAnnotation(3); window.ToggleSelection(4);
            Click("注釈を削除"); Check(entry.Marks.Count == 3, "Delete must remove all selected annotations");
            Click("↶"); Check(entry.Marks.Count == 5, "Undo must restore a deleted group");
            window.SelectAllAnnotations(); window.NudgeSelection(new Vector(1, 0)); Check(entry.Marks.All(m => m.X != 120), "Select all must include every annotation");
            Buttons((DependencyObject)window.Content).Single(b => b.Tag as string == "#10B981").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(entry.Marks.All(m => m.Color == "#10B981") && prefs.Load().Color == "#10B981", "Color changes must apply to the group and save defaults");
            var arrow = (ComboBox)window.FindName("ArrowStyleBox"); arrow.SelectedValue = "Dashed";
            Check(entry.Marks.Single(m => m.Tool == "Arrow").ArrowStyle == "Dashed" && prefs.Load().ArrowStyle == "Dashed", "Arrow preference must persist while only changing arrows in a mixed selection");
            var savedStyle = prefs.Load().TextStyle;
            window.CopyEntryAsync(entry).GetAwaiter().GetResult(); Check(copied?.PixelWidth == image.PixelWidth && entry.Marks.Count == 5, "Copy must flatten multi-selection and styled text without deleting vectors");
            window.RequestExit(); window = new MainWindow(store, _ => Task.CompletedTask, false); Layout();
            Check(prefs.Load().TextStyle == savedStyle && prefs.Load().Color == "#10B981" && ((ComboBox)window.FindName("ArrowStyleBox")).SelectedValue as string == "Dashed", "Drawing preferences must survive reopening the editor");
            Check(store.Load()[0].Marks[0].TextStyle == style, "Text annotations must retain their own styles after reopening");
            var loaded = store.Load()[0]; var full = AnnotationRenderer.Render(image, loaded.Marks);
            var crop = new CropRegion(50, 20, 300, 220);
            var cropped = AnnotationRenderer.Render(image, loaded.Marks, crop);
            var expectedCrop = new CroppedBitmap(full, new Int32Rect(crop.X, crop.Y, crop.Width, crop.Height));
            byte[] actualPixels = new byte[crop.Width * crop.Height * 4], expectedPixels = new byte[actualPixels.Length];
            cropped.CopyPixels(actualPixels, crop.Width * 4, 0); expectedCrop.CopyPixels(expectedPixels, crop.Width * 4, 0);
            Check(actualPixels.SequenceEqual(expectedPixels), "Styled text, background and border must preserve their layout and pixels when cropped");
            var format = new TextStyleDialog(style); var panel = (FrameworkElement)format.Content;
            Check(format.TextFormat.Size == style.Size && format.SizeBox.Text == "28.5", "Opening point-size controls must preserve the existing pixel size");
            format.SizeBox.SelectedItem = "12";
            Check(format.TextFormat.Size == 16, "Selecting 12pt must convert to 16 image pixels at 96 DPI");
            format.SizeBox.SelectedItem = "18";
            Check(format.TextFormat.Size == 24, "Selecting 18pt must convert to 24 image pixels");
            panel.Measure(new System.Windows.Size(470, 900)); panel.Arrange(new Rect(0, 0, 470, panel.DesiredSize.Height)); panel.UpdateLayout();
            Check(Buttons(panel).All(b => b.TransformToAncestor(panel).TransformBounds(new Rect(b.RenderSize)).Bottom <= panel.RenderSize.Height), "Text format dialog buttons must remain within its content");
            void Snapshot(FrameworkElement element, string name)
            {
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    var area = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
                    drawing.DrawRectangle(Brushes.White, null, area); drawing.DrawRectangle(new VisualBrush(element), null, area);
                }
                bitmap.Render(visual); CaptureStore.WriteImage(Path.Combine("artifacts", name), bitmap);
            }
            Snapshot(panel, "text-format-dialog.png"); Snapshot((FrameworkElement)window.Content, "editing-actions-preview.png");
            format.Close();
            Directory.CreateDirectory("artifacts"); CaptureStore.WriteImage(Path.Combine("artifacts", "text-format-selection-preview.png"), AnnotationRenderer.Render(image, store.Load()[0].Marks));
        }
        finally { window.RequestExit(); }
        var custom = prefs.Load() with { HotkeyModifiers = 6, HotkeyKey = 0x35, StartWithWindows = true }; prefs.Save(custom);
        Check(prefs.Load() == custom && HotkeyBinding.Label(6, 0x35) == "Ctrl+Shift+D5", "Hotkey and startup settings must persist");
        Check(HotkeyBinding.IsValid(0, 0x2C) && !HotkeyBinding.IsValid(0, 0x41) && !HotkeyBinding.IsValid(8, 0x41), "Hotkey validation must allow PrtSc and reject plain typing keys and unsupported modifiers");
        Check(StartupRegistration.Command(@"C:\Program Files\Prtsc\Prtsc.exe") == "\"C:\\Program Files\\Prtsc\\Prtsc.exe\" --startup", "Startup command must quote paths with spaces and start hidden");
        File.WriteAllText(Path.Combine(root, "settings.json"), "{\"Color\":\"invalid\",\"ArrowStyle\":\"invalid\",\"TextStyle\":null,\"HotkeyKey\":0}");
        var fallback = prefs.Load(); Check(fallback.Color == "#F43F5E" && fallback.ArrowStyle == "Tapered" && fallback.TextStyle.Size == 24 && fallback.HotkeyKey == 0x2C, "Malformed/old preferences must restore safe defaults");
        Check(CaptureLoupe.SampleRect(0, 0, 640, 360) == new Int32Rect(0, 0, 25, 25) && CaptureLoupe.SampleRect(639, 359, 640, 360) == new Int32Rect(615, 335, 25, 25), "Magnifier must clamp correctly at screen corners");
        Check(CaptureLoupe.SampleRect(0, 0, 1, 1) == new Int32Rect(0, 0, 1, 1), "Magnifier must support small source images");
        var loupe = new CaptureLoupe(); loupe.Update(image, new(100, 60), 320, 180, new Int32Rect(20, 20, 80, 90));
        var stack = (StackPanel)loupe.Child; Check(((TextBlock)stack.Children[1]).Text == "80 × 90 px", "Capture size must display image pixels rather than scaled display units");
        var grid = (Grid)stack.Children[0]; var source = (CroppedBitmap)((System.Windows.Controls.Image)grid.Children[0]).Source;
        Check(source.SourceRect.X == 188 && source.SourceRect.Y == 108, "Magnifier cursor mapping must account for display scaling");
        var settingsDialog = new SettingsDialog(100, 20, custom);
        Check(settingsDialog.HotkeyKey == custom.HotkeyKey && settingsDialog.HotkeyModifiers == custom.HotkeyModifiers && settingsDialog.StartWithWindows, "Settings dialog must reflect saved hotkey and startup preferences");
        settingsDialog.Close();
        var gateName = @"Local\Prtsc.Test." + Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceGate(gateName); using var received = new ManualResetEventSlim(); first.Listen(received.Set);
        using (var second = new SingleInstanceGate(gateName)) Check(first.IsFirst && !second.IsFirst && received.Wait(2000), "Second launch must signal the first instance instead of owning another instance");
        first.Dispose(); // Release before reopening the same named instance.
        using var reopened = new SingleInstanceGate(gateName); Check(reopened.IsFirst, "Exit must release single-instance ownership for a later launch");
    }
}

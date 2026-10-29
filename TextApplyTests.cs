using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Prtsc;

internal static class TextApplyTests
{
    internal static void Run(string root, BitmapSource image)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var store = new CaptureStore(root);
        var window = new MainWindow(store, _ => Task.CompletedTask, false) { ShowActivated = false, Opacity = 0 };
        try
        {
            var entry = window.AddCapture(image, "Apply text without closing");
            window.Show();
            var canvas = (Canvas)window.FindName("Editor");
            var source = new Mark { Tool = "Text", X = 30, Y = 40, TextStyle = new TextStyle(), Color = "#F43F5E" };
            void Apply(TextDialog dialog) => dialog.ApplyButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            void Modal(TextDialog dialog, Action action)
            {
                Exception? error = null; dialog.Opacity = 0;
                dialog.Loaded += (_, _) =>
                {
                    try { action(); }
                    catch (Exception ex) { error = ex; }
                    finally { if (dialog.IsVisible) dialog.Close(); }
                };
                dialog.ShowDialog();
                if (error is not null) throw new InvalidOperationException("Text apply dialog test failed", error);
            }
            var adding = window.CreateTextDialog(source);
            Modal(adding, () =>
            {
                Check(!adding.ApplyButton.IsEnabled, "Blank text must disable Apply");
                adding.Input.Text = "最初の適用";
                adding.FormatEditor!.ColorBox.SelectedItem = "White";
                adding.FormatEditor.BackgroundBox.SelectedItem = "Black";
                Apply(adding);
                Check(adding.IsVisible && entry.Marks.Count == 1 && entry.Marks[0].Text == "最初の適用" && adding.AcceptButton.Content as string == "更新", "Apply must create one annotation, keep the dialog open and change Add to Update");
                Check(((System.Windows.Controls.Button)window.FindName("EditTextButton")).IsEnabled, "Newly applied text must remain selected for later editing");
                var frame = (Border)canvas.Children[1];
                Check(((TextBlock)frame.Child).Text == "最初の適用" && ((TextBlock)frame.Child).Foreground.ToString() == Brushes.White.ToString() && frame.Background.ToString() == Brushes.Black.ToString(), "Apply must redraw the actual editor with text and colors");
                adding.Input.Text = "2回目の適用"; adding.FormatEditor.SizeBox.SelectedItem = "36";
                Apply(adding);
                Check(adding.IsVisible && entry.Marks.Count == 1 && store.Load()[0].Marks[0].Text == "2回目の適用" && ((TextBlock)((Border)canvas.Children[1]).Child).FontSize == 48, "Repeated Apply must update and persist the same annotation with its new size");
                adding.Input.Text = "未適用の変更";
                Check(entry.Marks[0].Text == "2回目の適用", "Typing after Apply must not change the image until the next Apply or Update");
            });
            Check(entry.Marks.Count == 1 && entry.Marks[0].Text == "2回目の適用", "Cancel must keep applied changes and discard unsubmitted input");
            var settings = new CaptureSettingsStore(Path.Combine(root, "settings.json")).Load();
            Check(settings.Color == "White" && settings.TextStyle.Size == 48, "Apply must persist drawing defaults immediately");
            var editing = window.CreateTextDialog(entry.Marks[0], 0);
            Modal(editing, () =>
            {
                editing.Input.Text = "更新して閉じる";
                Apply(editing);
                Check(editing.IsVisible && entry.Marks.Count == 1, "Existing text must support Apply without closing or duplication");
                editing.AcceptButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Check(!editing.IsVisible && editing.DialogResult == true, "Update must apply and close the modal dialog");
            });
            Check(store.Load()[0].Marks[0].Text == "更新して閉じる", "Update must preserve the last applied text");
            var undo = ((Panel)window.FindName("Tools")).Children.OfType<System.Windows.Controls.Button>().Single(b => b.Content as string == "↶");
            undo.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(entry.Marks[0].Text == "2回目の適用", "Update after an unchanged Apply must not add a duplicate Undo step");
            undo.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(entry.Marks[0].Text == "最初の適用", "Each changed Apply must be individually undoable");
            var cancelled = window.CreateTextDialog(source);
            cancelled.Input.Text = "追加しない"; cancelled.Close();
            Check(entry.Marks.Count == 1, "Cancelling before any Apply must not create an annotation");
        }
        finally { window.RequestExit(); }
    }
}

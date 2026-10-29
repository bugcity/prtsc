using System.Windows;

namespace Prtsc;

public partial class MainWindow
{
    private void OpenEditMenu(object sender, RoutedEventArgs e)
    {
        var button = (System.Windows.Controls.Button)sender; button.ContextMenu.PlacementTarget = button; button.ContextMenu.IsOpen = true;
    }
    private void SelectAllClick(object sender, RoutedEventArgs e) => SelectAllAnnotations();
    private void SavePreferences(CaptureSettings next) => Run(() => { settingsStore.Save(next); settings = next; });
    private void UpdateHotkeyLabel() => HotkeyLabel.Text = HotkeyBinding.Label(settings.HotkeyModifiers, settings.HotkeyKey) + "  範囲 / ウィンドウ撮影";
    internal void ShowExistingInstance() => ShowGallery();
    private void ApplyApplicationSettings(CaptureSettings next)
    {
        bool changedKey = next.HotkeyKey != settings.HotkeyKey || next.HotkeyModifiers != settings.HotkeyModifiers;
        int candidateId = activeHotkeyId == HotkeyId ? HotkeyId + 1 : HotkeyId;
        bool registered = false, startupChanged = false;
        try
        {
            if ((changedKey || !hotkeyRegistered) && enableShell && hwnd != IntPtr.Zero)
            {
                if (!HotkeyBinding.Register(hwnd, candidateId, next.HotkeyModifiers, next.HotkeyKey))
                    throw new InvalidOperationException("そのホットキーは登録できません。他のアプリや Windows との競合を確認してください。以前の設定を維持します。");
                registered = true;
            }
            if (next.StartWithWindows != settings.StartWithWindows && enableShell)
            { StartupRegistration.Apply(next.StartWithWindows); startupChanged = true; }
            settingsStore.Save(next);
        }
        catch
        {
            if (registered) UnregisterHotKey(hwnd, candidateId);
            if (startupChanged) StartupRegistration.Apply(settings.StartWithWindows);
            throw;
        }
        if (registered) { UnregisterHotKey(hwnd, activeHotkeyId); activeHotkeyId = candidateId; hotkeyRegistered = true; }
        bool changedRendering = settings.RenderingMethod != next.RenderingMethod;
        settings = next; UpdateHotkeyLabel(); EnforceCaptureLimit();
        StatusLabel.Text = changedRendering ? "設定を保存しました · 描画方法は再起動後に反映されます" : "設定を保存しました";
    }
    private Mark PreviewMark(int index, Mark mark)
    {
        if (!moving || selectionDraft is null) return mark;
        if (dragPart == "Move" && dragSelection?.TryGetValue(index, out var source) == true)
            return AnnotationDocument.Move(source, new Vector(selectionDraft.X - dragOriginal!.X, selectionDraft.Y - dragOriginal.Y));
        return index == selectedIndex ? selectionDraft : mark;
    }
    internal void ToggleSelection(int index)
    {
        if (current is null || index < 0 || index >= current.Marks.Count) return;
        CancelGesture();
        if (!selectedIndices.Add(index)) selectedIndices.Remove(index);
        primaryIndex = selectedIndices.Count == 0 ? -1 : selectedIndices.Max();
        UpdateSelectionControls(); Redraw();
    }
    internal void SelectAllAnnotations()
    {
        if (tool != "Select") SetTool("Select"); selectedIndices.Clear();
        if (current is not null) foreach (int index in Enumerable.Range(0, current.Marks.Count)) selectedIndices.Add(index);
        primaryIndex = selectedIndices.Count == 0 ? -1 : selectedIndices.Max(); UpdateSelectionControls(); Redraw();
    }
    private void ChangeSelected(Func<Mark, Mark> change)
    {
        if (current is null || document is null || selectedIndices.Count == 0) return;
        CancelGesture();
        var updates = selectedIndices.Select(i => (Index: i, Mark: change(current.Marks[i]))).Where(p => p.Mark != current.Marks[p.Index]).ToList();
        if (updates.Count == 0) return;
        document.Change(marks => { foreach (var p in updates) marks[p.Index] = p.Mark; }); Commit();
    }
    internal void NudgeSelection(Vector delta) => ChangeSelected(mark => AnnotationDocument.Move(mark, delta));
    private void DuplicateSelected(object sender, RoutedEventArgs e)
    {
        if (current is null || document is null || selectedIndices.Count == 0) return;
        CancelGesture();
        var copies = selectedIndices.OrderBy(i => i).Select(i => AnnotationDocument.Move(current.Marks[i], new Vector(12, 12))).ToList();
        int first = current.Marks.Count; document.Change(marks => marks.AddRange(copies));
        selectedIndices.Clear(); foreach (int i in Enumerable.Range(first, copies.Count)) selectedIndices.Add(i);
        primaryIndex = first; UpdateSelectionControls(); Commit();
    }
    private void BringToFront(object sender, RoutedEventArgs e) => ReorderSelection(true);
    private void SendToBack(object sender, RoutedEventArgs e) => ReorderSelection(false);
    private void BringForward(object sender, RoutedEventArgs e) => StepSelection(true);
    private void SendBackward(object sender, RoutedEventArgs e) => StepSelection(false);
    internal void StepSelection(bool front)
    {
        if (current is null || document is null || selectedIndices.Count == 0) return;
        CancelGesture(); var flags = Enumerable.Range(0, current.Marks.Count).Select(selectedIndices.Contains).ToArray();
        var result = current.Marks.ToList(); bool changed = false;
        void Swap(int i, int j)
        {
            if (!flags[i] || flags[j]) return;
            (result[i], result[j]) = (result[j], result[i]); (flags[i], flags[j]) = (flags[j], flags[i]); changed = true;
        }
        if (front) { for (int i = result.Count - 2; i >= 0; i--) Swap(i, i + 1); }
        else { for (int i = 1; i < result.Count; i++) Swap(i, i - 1); }
        if (!changed) return;
        document.Change(marks => { marks.Clear(); marks.AddRange(result); });
        selectedIndices.Clear(); for (int i = 0; i < flags.Length; i++) if (flags[i]) selectedIndices.Add(i);
        primaryIndex = selectedIndices.Min(); UpdateSelectionControls(); Commit();
    }
    internal void ReorderSelection(bool front)
    {
        if (current is null || document is null || selectedIndices.Count == 0) return;
        CancelGesture();
        var selected = current.Marks.Where((_, i) => selectedIndices.Contains(i)).ToList();
        var others = current.Marks.Where((_, i) => !selectedIndices.Contains(i)).ToList();
        int start = front ? others.Count : 0;
        document.Change(marks => { marks.Clear(); marks.AddRange(front ? others.Concat(selected) : selected.Concat(others)); });
        selectedIndices.Clear(); foreach (int i in Enumerable.Range(start, selected.Count)) selectedIndices.Add(i);
        primaryIndex = start; UpdateSelectionControls(); Commit();
    }
    private void EditTextStyle(object sender, RoutedEventArgs e)
    {
        var initial = SelectedMark is { Tool: "Text" } mark ? mark.TextStyle ?? new TextStyle { Size = Math.Max(18, mark.Width * 6) } : settings.TextStyle;
        var dialog = new TextStyleDialog(initial, SelectedMark is { Tool: "Text" } text ? text.Color : color) { Owner = this };
        if (dialog.ShowDialog() == true) ApplyTextStyle(dialog.TextFormat, dialog.TextColor);
    }
    internal void ApplyTextStyle(TextStyle style, string? textColor = null)
    {
        if (textColor is not null && !TextStyle.IsColor(textColor)) throw new ArgumentException("文字色を確認してください。", nameof(textColor));
        style = style.Normalize(); SavePreferences(settings with { TextStyle = style, Color = textColor ?? settings.Color });
        if (textColor is not null)
        {
            color = textColor; CurrentColor.Fill = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(color)!;
        }
        ChangeSelected(mark => mark.Tool == "Text" ? mark with { TextStyle = style, Color = textColor ?? mark.Color } : mark);
    }
}

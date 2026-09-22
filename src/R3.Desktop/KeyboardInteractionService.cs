using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace R3.Desktop;

/// <summary>ERP genelindeki klavye davranışlarını tek noktadan yönetir. Dialoglar arasında
/// farklı Enter/Escape uygulamaları oluşmasını ve her tuşta pahalı liste sorgusu çalışmasını önler.</summary>
internal static class KeyboardInteractionService
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return; _initialized = true;
        EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyDownEvent, new KeyEventHandler(WindowPreviewKeyDown), true);
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(TextBoxGotKeyboardFocus), true);
        EventManager.RegisterClassHandler(typeof(PasswordBox), UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(PasswordGotKeyboardFocus), true);
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(TextBoxPreviewMouseDown), true);
    }

    public static void AttachListShortcuts(FrameworkElement view, TextBox? search, Action? create, Action? edit, Action refresh)
    {
        view.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F2 && create != null) { create(); e.Handled = true; }
            else if (e.Key == Key.F3 && edit != null) { edit(); e.Handled = true; }
            else if (e.Key == Key.F5) { refresh(); e.Handled = true; }
            else if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && search != null) { search.Focus(); search.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.Escape && search is { Text.Length: > 0 } && search.IsKeyboardFocusWithin) { search.Clear(); e.Handled = true; }
        };
    }

    public static void AttachDebouncedSearch(TextBox search, Action refresh, int delayMilliseconds = 240)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(delayMilliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); refresh(); };
        search.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
        search.Unloaded += (_, _) => timer.Stop();
    }

    private static void WindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Window window || window is MainWindow || e.Handled) return;
        var focused = Keyboard.FocusedElement as DependencyObject;
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (FindVisualChildren<Button>(window).FirstOrDefault(x => x.IsDefault && x.IsEnabled && x.Visibility == Visibility.Visible) is { } save)
            { save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
            return;
        }
        if (e.Key == Key.Escape)
        {
            if (FindVisualChildren<Button>(window).FirstOrDefault(x => x.IsCancel && x.IsEnabled && x.Visibility == Visibility.Visible) is { } cancel) cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else window.Close();
            e.Handled = true; return;
        }
        if (e.Key == Key.F4 && FindParent<ComboBox>(focused) is { } combo)
        { combo.IsDropDownOpen = !combo.IsDropDownOpen; e.Handled = true; return; }
        if (e.Key == Key.Decimal && FindParent<TextBox>(focused) is { Tag: "Numeric" } numeric)
        {
            var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            numeric.SelectedText = separator; numeric.SelectionStart += separator.Length; e.Handled = true; return;
        }
        if (e.Key != Key.Enter || Keyboard.Modifiers is not (ModifierKeys.None or ModifierKeys.Shift)) return;
        if (FindParent<DataGrid>(focused) != null) return;
        if (FindParent<TextBox>(focused) is { AcceptsReturn: true }) return;
        if (FindParent<TextBox>(focused) == null && FindParent<PasswordBox>(focused) == null && FindParent<ComboBox>(focused) == null && FindParent<DatePicker>(focused) == null) return;
        if (FindParent<ComboBox>(focused) is { IsDropDownOpen: true } open) { open.IsDropDownOpen = false; }
        if (focused is not UIElement element) return;
        element.MoveFocus(new TraversalRequest(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next));
        e.Handled = true;
    }

    private static void TextBoxGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) { if (sender is TextBox box) box.SelectAll(); }
    private static void PasswordGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) { if (sender is PasswordBox box) box.SelectAll(); }
    private static void TextBoxPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox box || box.IsKeyboardFocusWithin) return;
        e.Handled = true; box.Focus();
    }
    private static T? FindParent<T>(DependencyObject? value) where T : DependencyObject
    {
        while (value != null) { if (value is T match) return match; value = value is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(value) : LogicalTreeHelper.GetParent(value); }
        return null;
    }
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match; foreach (var nested in FindVisualChildren<T>(child)) yield return nested; }
    }
}

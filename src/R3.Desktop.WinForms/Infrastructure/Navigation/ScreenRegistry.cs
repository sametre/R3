using R3.Desktop.WinForms.Infrastructure.Session;

namespace R3.Desktop.WinForms.Infrastructure.Navigation;

/// <summary>Opens screens as workspace tabs. Views depend on this, not on MainForm.</summary>
public interface IWorkspaceNavigator
{
    /// <summary>Opens (or activates, if already open under <paramref name="key"/>) a screen tab.</summary>
    void Open(string key, string title, Func<Control> create);
    /// <summary>Opens a registered screen by key, checking its permission.</summary>
    void OpenScreen(string key);
    /// <summary>Closes the tab hosting <paramref name="view"/>.</summary>
    void Close(Control view);
    /// <summary>Renames the tab hosting <paramref name="view"/> (e.g. "Yeni Stok Kartı" → the saved code).</summary>
    void SetTitle(Control view, string title);
}

/// <param name="Module">Top menu group ("Stok").</param>
/// <param name="Permission">Required permission code; null = any signed-in user.</param>
public sealed record ScreenDefinition(string Key, string Module, string Title, string? Permission, Keys Shortcut, Func<AppSession, IWorkspaceNavigator, Control> Create);

/// <summary>
/// The screen catalog: menus are built from it, so a menu item exists only for a screen that is implemented and
/// permitted - no dead "coming soon" entries. Migrated screens are added here module by module.
/// </summary>
public sealed class ScreenRegistry
{
    private readonly List<ScreenDefinition> _screens = [];

    public IReadOnlyList<ScreenDefinition> Screens => _screens;

    public ScreenRegistry Register(ScreenDefinition screen)
    {
        if (_screens.Any(s => s.Key == screen.Key)) throw new InvalidOperationException($"Ekran anahtarı iki kez tanımlandı: {screen.Key}");
        _screens.Add(screen);
        return this;
    }

    public ScreenDefinition? Find(string key) => _screens.FirstOrDefault(s => s.Key == key);

    public IEnumerable<ScreenDefinition> VisibleTo(AppSession session) =>
        _screens.Where(s => s.Permission == null || session.Can(s.Permission));
}

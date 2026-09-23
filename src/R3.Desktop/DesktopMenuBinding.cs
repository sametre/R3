namespace R3.Desktop;

internal static class DesktopMenuBinding
{
    // Bind by full path: report templates and operational screens can share a title.
    internal static DesktopMenuEntry[] Bind(
        DesktopMenuEntry[] entries,
        IReadOnlyDictionary<string, (Action Click, string Permission)> actions,
        Func<string, bool> hasPermission,
        string parentPath = "") => entries.Select(entry =>
        {
            var path = parentPath.Length == 0 ? entry.Text : $"{parentPath} > {entry.Text}";
            var click = actions.TryGetValue(path, out var action) && hasPermission(action.Permission)
                ? action.Click : null;
            return entry with
            {
                Click = click,
                Children = Bind(entry.Children, actions, hasPermission, path)
            };
        }).ToArray();
}

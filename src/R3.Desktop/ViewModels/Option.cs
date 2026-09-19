namespace R3.Desktop.ViewModels;

/// <summary>Value/label pair for ComboBoxes backed by a fixed set (not a database lookup table).</summary>
public sealed record Option(string Value, string Label);

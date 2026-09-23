using Krypton.Toolkit;

namespace R3.Desktop.WinForms.Components.Dialogs;

/// <summary>All message boxes go through here (Krypton-styled, Turkish captions) so their look and wording stay uniform.</summary>
public static class R3Dialogs
{
    public static void Error(IWin32Window? owner, string title, string message) =>
        Show(owner, message, title, KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Error);

    public static void Warning(IWin32Window? owner, string title, string message) =>
        Show(owner, message, title, KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Warning);

    public static void Info(IWin32Window? owner, string title, string message) =>
        Show(owner, message, title, KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);

    /// <summary>Yes/No confirmation; "Hayır" is the default button for destructive questions.</summary>
    public static bool Confirm(IWin32Window? owner, string title, string message, bool destructive = false) =>
        Show(owner, message, title, KryptonMessageBoxButtons.YesNo, destructive ? KryptonMessageBoxIcon.Warning : KryptonMessageBoxIcon.Question,
            destructive ? KryptonMessageBoxDefaultButton.Button2 : KryptonMessageBoxDefaultButton.Button1) == DialogResult.Yes;

    /// <summary>Unsaved changes: Yes = save, No = discard, Cancel = stay.</summary>
    public static DialogResult SaveChanges(IWin32Window? owner, string title) =>
        Show(owner, "Kaydedilmemiş değişiklikler var. Kaydedilsin mi?", title, KryptonMessageBoxButtons.YesNoCancel, KryptonMessageBoxIcon.Warning);

    private static DialogResult Show(IWin32Window? owner, string text, string caption, KryptonMessageBoxButtons buttons, KryptonMessageBoxIcon icon,
        KryptonMessageBoxDefaultButton defaultButton = KryptonMessageBoxDefaultButton.Button1) =>
        owner == null
            ? KryptonMessageBox.Show(text, caption, buttons, icon, defaultButton)
            : KryptonMessageBox.Show(owner, text, caption, buttons, icon, defaultButton);
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using R3.Desktop.ViewModels;
using R3.Infrastructure;
using static R3.Desktop.Views.ElectronicDocumentUiKit;

namespace R3.Desktop.Views;

// Phase 10 (§50-53): E-Belge Ayarları. No credential fields here at all - there is nothing to mask
// with "********" yet, because no real provider (and therefore no real credential) has been
// selected; see docs/architecture/ELECTRONIC-DOCUMENT-PROVIDER.md's Blocker. When one is, its
// Username/ApiKey/etc. fields belong on this screen, write-only, through
// IElectronicDocumentSecretProvider - never echoing the stored value back.
internal static class ElectronicDocumentProviderSettingsView
{
    public static UIElement Create(StoreDatabase database, string companyId)
    {
        var vm = new ElectronicDocumentProviderSettingsViewModel(database, companyId);
        var root = new StackPanel { Margin = new Thickness(18), MaxWidth = 560 };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(new TextBlock { Text = "E-Belge Ayarları", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brush("263746") });
        var testBadge = Badge("TEST ORTAMI", "#C0832B"); testBadge.Margin = new Thickness(10, 4, 0, 0); testBadge.Visibility = Visibility.Collapsed;
        header.Children.Add(testBadge);
        root.Children.Add(header);
        root.Children.Add(new TextBlock { Text = "Firma vergi kimliği ve sağlayıcı seçimi. Gerçek entegratör henüz seçilmedi - bkz. docs/architecture/ELECTRONIC-DOCUMENT-PROVIDER.md.",
            Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });

        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        root.Children.Add(status);

        var card = new Border { Background = Brushes.White, BorderBrush = PanelBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(16) };
        var form = new StackPanel();
        var taxNumber = LabeledReadOnly(form, "Firma VKN/TCKN", vm.TaxNumber);
        var legalTitle = LabeledReadOnly(form, "Ünvan", vm.LegalTitle);

        form.Children.Add(new TextBlock { Text = "Sağlayıcı", Foreground = Muted, FontSize = 11, Margin = new Thickness(0, 10, 0, 4) });
        var providerCombo = new ComboBox
        {
            ItemsSource = new[] { ("Development", "Development (test/dev sağlayıcı)"), ("Http", "Gerçek Entegratör (henüz yapılandırılmamış)") },
            DisplayMemberPath = "Item2", SelectedValuePath = "Item1", SelectedValue = vm.ProviderKind.ToString(), Height = 30
        };
        form.Children.Add(providerCombo);

        form.Children.Add(new TextBlock { Text = "Ortam", Foreground = Muted, FontSize = 11, Margin = new Thickness(0, 10, 0, 4) });
        var environmentCombo = new ComboBox
        {
            ItemsSource = new[] { ("Test", "Test"), ("Production", "Production") },
            DisplayMemberPath = "Item2", SelectedValuePath = "Item1", SelectedValue = vm.ProviderEnvironment.ToString(), Height = 30
        };
        form.Children.Add(environmentCombo);
        card.Child = form; root.Children.Add(card);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var saveButton = ActionButton(buttons, "Kaydet", () => { });
        var testButton = ActionButton(buttons, "Bağlantıyı Test Et", () => { });
        root.Children.Add(buttons);

        var healthPanel = new Border { Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(12), CornerRadius = new CornerRadius(6), Visibility = Visibility.Collapsed };
        var healthText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        healthPanel.Child = healthText; root.Children.Add(healthPanel);

        void Refresh()
        {
            status.Text = vm.ErrorMessage ?? vm.StatusMessage ?? "";
            status.Foreground = vm.ErrorMessage != null ? Brush("#C4514B") : Brush("#2A8F7B");
            testBadge.Visibility = vm.IsTestEnvironment ? Visibility.Visible : Visibility.Collapsed;
            var editable = vm.CanEdit && !vm.IsBusy;
            providerCombo.IsEnabled = editable; environmentCombo.IsEnabled = editable;
            saveButton.IsEnabled = editable; testButton.IsEnabled = editable;
            if (vm.HealthMessage != null)
            {
                healthPanel.Visibility = Visibility.Visible;
                healthText.Text = (vm.LastHealthy == true ? "● Bağlantı başarılı. " : vm.LastHealthy == false ? "● Bağlantı başarısız. " : "● ") + vm.HealthMessage;
                healthPanel.Background = vm.LastHealthy == true ? Brush("#EAF5F0") : vm.LastHealthy == false ? Brush("#FBEEEE") : Brush("#F5F5F5");
                healthText.Foreground = vm.LastHealthy == true ? Brush("#1F6B54") : vm.LastHealthy == false ? Brush("#8A3A3A") : Brush("#626262");
            }
            else healthPanel.Visibility = Visibility.Collapsed;
        }

        providerCombo.SelectionChanged += (_, _) => { if (providerCombo.SelectedValue is string s && Enum.TryParse<ElectronicDocumentProviderKind>(s, out var kind)) vm.ProviderKind = kind; };
        environmentCombo.SelectionChanged += (_, _) => { if (environmentCombo.SelectedValue is string s && Enum.TryParse<ElectronicDocumentProviderEnvironment>(s, out var env)) { vm.ProviderEnvironment = env; Refresh(); } };
        saveButton.Click += (_, _) => { vm.SaveCommand.Execute(null); Refresh(); };
        testButton.Click += async (_, _) => { await vm.TestConnectionCommand.ExecuteAsync(null); Refresh(); };

        Refresh();
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static TextBox LabeledReadOnly(Panel parent, string label, string value)
    {
        parent.Children.Add(new TextBlock { Text = label, Foreground = Muted, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
        var box = new TextBox { Text = value, IsReadOnly = true, Height = 30, Background = Brush("#F5F5F5") };
        parent.Children.Add(box); return box;
    }
}

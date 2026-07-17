using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SessionDeck.Interop;
using SessionDeck.Services;
using SessionDeck.ViewModels;

namespace SessionDeck;

/// <summary>
/// Full tile editing in the UI (SPEC §F2/§F6 — stage B): title (manual/auto),
/// description, border color and blink settings. Applies to the view-model on OK;
/// the caller persists.
/// </summary>
public partial class EditTileDialog : Window
{
    private readonly TileViewModel _tile;

    public EditTileDialog(TileViewModel tile)
    {
        _tile = tile;
        InitializeComponent();

        AutoTitleCheck.IsChecked = !tile.ManualTitle;
        TitleBox.Text = tile.Title;
        DescBox.Text = tile.Description;
        ColorBox.Text = tile.ColorName;
        BlinkCheck.IsChecked = tile.AltColorName != null;
        AltColorBox.Text = tile.AltColorName ?? "black";
        IntervalBox.Text = tile.BlinkIntervalMs.ToString();

        UpdateEnabledStates();
        UpdatePreviews();
    }

    private void AutoTitle_Changed(object sender, RoutedEventArgs e) => UpdateEnabledStates();
    private void Blink_Changed(object sender, RoutedEventArgs e) => UpdateEnabledStates();
    private void Color_Changed(object sender, TextChangedEventArgs e) => UpdatePreviews();

    private void UpdateEnabledStates()
    {
        if (TitleBox == null || BlinkPanel == null) return;   // during InitializeComponent
        TitleBox.IsEnabled = AutoTitleCheck.IsChecked != true;
        BlinkPanel.IsEnabled = BlinkCheck.IsChecked == true;
        BlinkPanel.Opacity = BlinkPanel.IsEnabled ? 1.0 : 0.4;
    }

    private void UpdatePreviews()
    {
        if (ColorPreview == null || AltColorPreview == null) return;
        ColorPreview.Background = ColorUtil.TryParse(ColorBox.Text, out var c)
            ? new SolidColorBrush(c) : Brushes.Transparent;
        AltColorPreview.Background = ColorUtil.TryParse(AltColorBox.Text, out var a)
            ? new SolidColorBrush(a) : Brushes.Transparent;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!ColorUtil.TryParse(ColorBox.Text, out _))
        {
            MessageBox.Show(this, $"צבע לא חוקי: \"{ColorBox.Text}\"", "עריכת אריח",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool blink = BlinkCheck.IsChecked == true;
        int interval = _tile.BlinkIntervalMs;
        if (blink)
        {
            if (!ColorUtil.TryParse(AltColorBox.Text, out _))
            {
                MessageBox.Show(this, $"צבע משני לא חוקי: \"{AltColorBox.Text}\"", "עריכת אריח",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!int.TryParse(IntervalBox.Text, out interval) || interval < 100 || interval > 10000)
            {
                MessageBox.Show(this, "קצב הבהוב חייב להיות 100–10000 ms", "עריכת אריח",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        bool autoTitle = AutoTitleCheck.IsChecked == true;
        if (autoTitle)
        {
            _tile.ManualTitle = false;
            // Snap back to the live window title immediately when possible.
            if (_tile.Hwnd != IntPtr.Zero && NativeMethods.IsWindow(_tile.Hwnd))
            {
                string current = NativeMethods.GetWindowTextSafe(_tile.Hwnd);
                if (current.Length > 0) _tile.Title = current;
            }
        }
        else
        {
            _tile.ManualTitle = true;
            _tile.Title = TitleBox.Text;
        }

        _tile.Description = DescBox.Text;
        _tile.ColorName = ColorBox.Text.Trim();
        _tile.AltColorName = blink ? AltColorBox.Text.Trim() : null;
        _tile.BlinkIntervalMs = interval;

        DialogResult = true;
    }
}

using System.Windows;

namespace SalsaNOWGames.Views
{
    public partial class ShortcutDialog : Window
    {
        public enum DialogType
        {
            Info,
            Success,
            Error,
            Confirm,
            RestartSteam
        }

        public bool DialogResultValue { get; private set; }

        public ShortcutDialog()
        {
            InitializeComponent();
        }

        public ShortcutDialog(string title, string message, DialogType type = DialogType.Info, string secondaryMessage = null)
            : this()
        {
            TitleText.Text = title;
            MessageText.Text = message;

            // Set icon and styling based on type
            switch (type)
            {
                case DialogType.Success:
                    IconText.Text = "✓";
                    IconText.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4CAF50"));
                    break;
                case DialogType.Error:
                    IconText.Text = "⚠";
                    IconText.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF6B6B"));
                    break;
                case DialogType.Confirm:
                    IconText.Text = "❓";
                    ShowSecondaryButton("No");
                    PrimaryButton.Content = "Yes";
                    break;
                case DialogType.RestartSteam:
                    IconText.Text = "🔄";
                    ShowSecondaryButton("Later");
                    PrimaryButton.Content = "Restart Steam";
                    break;
                default:
                    IconText.Text = "🔗";
                    break;
            }

            if (!string.IsNullOrEmpty(secondaryMessage))
            {
                SecondaryText.Text = secondaryMessage;
                SecondaryText.Visibility = Visibility.Visible;
            }
        }

        private void ShowSecondaryButton(string text)
        {
            SecondaryButton.Content = text;
            SecondaryButton.Visibility = Visibility.Visible;
        }

        private void PrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResultValue = true;
            DialogResult = true;
            Close();
        }

        private void SecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResultValue = false;
            DialogResult = false;
            Close();
        }

        // Static helper methods
        public static void ShowInfo(Window owner, string title, string message, string secondaryMessage = null)
        {
            var dialog = new ShortcutDialog(title, message, DialogType.Info, secondaryMessage);
            dialog.Owner = owner;
            dialog.ShowDialog();
        }

        public static void ShowSuccess(Window owner, string title, string message, string secondaryMessage = null)
        {
            var dialog = new ShortcutDialog(title, message, DialogType.Success, secondaryMessage);
            dialog.Owner = owner;
            dialog.ShowDialog();
        }

        public static void ShowError(Window owner, string title, string message, string secondaryMessage = null)
        {
            var dialog = new ShortcutDialog(title, message, DialogType.Error, secondaryMessage);
            dialog.Owner = owner;
            dialog.ShowDialog();
        }

        public static bool ShowConfirm(Window owner, string title, string message, string secondaryMessage = null)
        {
            var dialog = new ShortcutDialog(title, message, DialogType.Confirm, secondaryMessage);
            dialog.Owner = owner;
            dialog.ShowDialog();
            return dialog.DialogResultValue;
        }

        public static bool ShowRestartSteam(Window owner, string gameName)
        {
            var dialog = new ShortcutDialog(
                "Restart Steam?",
                $"Shortcut created for {gameName}!\n\nRestart Steam now to see the game in your library?",
                DialogType.RestartSteam,
                "Steam will close and reopen automatically.");
            dialog.Owner = owner;
            dialog.ShowDialog();
            return dialog.DialogResultValue;
        }
    }
}

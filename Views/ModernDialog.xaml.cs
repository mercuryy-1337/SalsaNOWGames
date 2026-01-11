using System.Windows;
using System.Windows.Media;

namespace SalsaNOWGames.Views
{
    /// <summary>
    /// A modern, reusable dialog window that matches the app's dark theme.
    /// Use the static helper methods for easy dialog creation.
    /// </summary>
    public partial class ModernDialog : Window
    {
        /// <summary>
        /// Dialog types that control the icon and button layout
        /// </summary>
        public enum DialogType
        {
            Info,           // ℹ - Information, single OK button
            Success,        // ✓ - Success confirmation, single OK button
            Warning,        // ⚠ - Warning message, single OK button
            Error,          // ✖ - Error message, single OK button
            Confirm,        // ❓ - Yes/No confirmation
            Update,         // 🔄 - Update available, Update Now/Later buttons
            RestartSteam,   // 🔄 - Restart Steam prompt
            Download        // ⬇ - Downloading indicator
        }

        /// <summary>
        /// Gets the result of the dialog (true = primary button, false = secondary/close)
        /// </summary>
        public bool DialogResultValue { get; private set; }

        public ModernDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates a dialog with the specified title, message, and type
        /// </summary>
        public ModernDialog(string title, string message, DialogType type = DialogType.Info, 
            string secondaryMessage = null, string primaryButtonText = null, string secondaryButtonText = null)
            : this()
        {
            TitleText.Text = title;
            MessageText.Text = message;

            // Set icon, colors, and buttons based on type
            ConfigureDialogType(type, primaryButtonText, secondaryButtonText);

            if (!string.IsNullOrEmpty(secondaryMessage))
            {
                SecondaryText.Text = secondaryMessage;
                SecondaryText.Visibility = Visibility.Visible;
            }
        }

        private void ConfigureDialogType(DialogType type, string primaryText, string secondaryText)
        {
            // Default icon color
            var defaultColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#66C0F4"));
            var successColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
            var warningColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA726"));
            var errorColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF6B6B"));

            switch (type)
            {
                case DialogType.Info:
                    IconText.Text = "ℹ";
                    IconText.Foreground = defaultColor;
                    PrimaryButton.Content = primaryText ?? "OK";
                    break;

                case DialogType.Success:
                    IconText.Text = "✓";
                    IconText.Foreground = successColor;
                    PrimaryButton.Content = primaryText ?? "OK";
                    break;

                case DialogType.Warning:
                    IconText.Text = "⚠";
                    IconText.Foreground = warningColor;
                    PrimaryButton.Content = primaryText ?? "OK";
                    break;

                case DialogType.Error:
                    IconText.Text = "✖";
                    IconText.Foreground = errorColor;
                    PrimaryButton.Content = primaryText ?? "OK";
                    break;

                case DialogType.Confirm:
                    IconText.Text = "❓";
                    IconText.Foreground = defaultColor;
                    PrimaryButton.Content = primaryText ?? "Yes";
                    ShowSecondaryButton(secondaryText ?? "No");
                    break;

                case DialogType.Update:
                    IconText.Text = "🔄";
                    IconText.Foreground = defaultColor;
                    PrimaryButton.Content = primaryText ?? "Update Now";
                    ShowSecondaryButton(secondaryText ?? "Later");
                    break;

                case DialogType.RestartSteam:
                    IconText.Text = "🔄";
                    IconText.Foreground = defaultColor;
                    PrimaryButton.Content = primaryText ?? "Restart Steam";
                    ShowSecondaryButton(secondaryText ?? "Later");
                    break;

                case DialogType.Download:
                    IconText.Text = "⬇";
                    IconText.Foreground = defaultColor;
                    PrimaryButton.Content = primaryText ?? "OK";
                    break;
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

        #region Static Helper Methods

        /// <summary>
        /// Shows an information dialog with a single OK button
        /// </summary>
        public static void ShowInfo(Window owner, string title, string message, string secondaryMessage = null)
        {
            var dialog = new ModernDialog(title, message, DialogType.Info, secondaryMessage);
            if (owner != null) dialog.Owner = owner;
            dialog.ShowDialog();
        }

        /// <summary>
        /// Shows a success dialog with a single OK button
        /// </summary>
        public static void ShowSuccess(Window owner, string title, string message, string secondaryMessage = null)
        {
            var dialog = new ModernDialog(title, message, DialogType.Success, secondaryMessage);
            if (owner != null) dialog.Owner = owner;
            dialog.ShowDialog();
        }

        /// <summary>
        /// Shows a warning dialog with a single OK button
        /// </summary>
        public static void ShowWarning(Window owner, string title, string message, string secondaryMessage = null)
        {
            var dialog = new ModernDialog(title, message, DialogType.Warning, secondaryMessage);
            if (owner != null) dialog.Owner = owner;
            dialog.ShowDialog();
        }

        /// <summary>
        /// Shows an error dialog with a single OK button
        /// </summary>
        public static void ShowError(Window owner, string title, string message, string secondaryMessage = null)
        {
            var dialog = new ModernDialog(title, message, DialogType.Error, secondaryMessage);
            if (owner != null) dialog.Owner = owner;
            dialog.ShowDialog();
        }

        /// <summary>
        /// Shows a confirmation dialog with Yes/No buttons
        /// </summary>
        /// <returns>True if user clicked Yes, false otherwise</returns>
        public static bool ShowConfirm(Window owner, string title, string message, string secondaryMessage = null)
        {
            var dialog = new ModernDialog(title, message, DialogType.Confirm, secondaryMessage);
            if (owner != null) dialog.Owner = owner;
            dialog.ShowDialog();
            return dialog.DialogResultValue;
        }

        /// <summary>
        /// Shows an update available dialog with Update Now/Later buttons
        /// </summary>
        /// <returns>True if user clicked Update Now, false otherwise</returns>
        public static bool ShowUpdate(Window owner, string version, string message = null)
        {
            string defaultMessage = $"A new version ({version}) is available.\n\nWould you like to update now?";
            var dialog = new ModernDialog(
                "Update Available",
                message ?? defaultMessage,
                DialogType.Update,
                "The app will restart automatically after updating.");
            if (owner != null) dialog.Owner = owner;
            dialog.ShowDialog();
            return dialog.DialogResultValue;
        }

        /// <summary>
        /// Shows a dialog asking to restart Steam
        /// </summary>
        /// <returns>True if user clicked Restart Steam, false otherwise</returns>
        public static bool ShowRestartSteam(Window owner, string gameName)
        {
            var dialog = new ModernDialog(
                "Restart Steam?",
                $"Shortcut created for {gameName}!\n\nRestart Steam now to see the game in your library?",
                DialogType.RestartSteam,
                "Steam will close and reopen automatically.");
            if (owner != null) dialog.Owner = owner;
            dialog.ShowDialog();
            return dialog.DialogResultValue;
        }

        /// <summary>
        /// Shows a custom dialog with full control over buttons
        /// </summary>
        public static bool ShowCustom(Window owner, string title, string message, DialogType type,
            string secondaryMessage = null, string primaryButtonText = null, string secondaryButtonText = null)
        {
            var dialog = new ModernDialog(title, message, type, secondaryMessage, primaryButtonText, secondaryButtonText);
            if (owner != null) dialog.Owner = owner;
            dialog.ShowDialog();
            return dialog.DialogResultValue;
        }

        #endregion
    }
}

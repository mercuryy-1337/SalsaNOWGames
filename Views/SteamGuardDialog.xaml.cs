using System.Windows;

namespace SalsaNOWGames.Views
{
    public partial class SteamGuardDialog : Window
    {
        public string Code { get; private set; }

        public SteamGuardDialog(string message = null)
        {
            InitializeComponent();
            
            if (!string.IsNullOrEmpty(message))
            {
                MessageText.Text = message;
            }
            
            CodeInput.Focus();
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            Code = CodeInput.Text?.Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(Code))
            {
                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

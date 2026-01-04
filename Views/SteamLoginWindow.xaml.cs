using System;
using System.Windows;
using SalsaNOWGames.Models;

namespace SalsaNOWGames.Views
{
    public partial class SteamLoginWindow : Window
    {
        public SteamSession Session { get; private set; }
        public string Username { get; private set; }
        public string Password { get; private set; }
        public bool RememberCredentials { get; private set; }

        public SteamLoginWindow()
        {
            InitializeComponent();
            UsernameBox.Focus();
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;

            string username = UsernameBox.Text?.Trim();
            string password = PasswordBox.Password;

            if (string.IsNullOrEmpty(username))
            {
                ShowError("Please enter your Steam username.");
                UsernameBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                ShowError("Please enter your Steam password.");
                PasswordBox.Focus();
                return;
            }

            // Create a session with the credentials
            Username = username;
            Password = password;
            RememberCredentials = RememberCheckBox.IsChecked ?? true;

            // Create session object
            Session = new SteamSession
            {
                Username = username,
                ExpiresAt = DateTime.UtcNow.AddDays(30), // Assume valid for 30 days
            };

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}

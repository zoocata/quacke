using System.Windows;

namespace QuakeServerManager.Views
{
    public partial class InputDialog : Window
    {
        public string ResponseText
        {
            get { return ResponseTextBox.Text; }
            set { ResponseTextBox.Text = value; }
        }

        public InputDialog(string title, string message)
        {
            InitializeComponent();
            Title = title;
            DataContext = this;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Mqtt.App
{
    /// <summary>
    /// Interaktionslogik für SendMessageInput.xaml
    /// </summary>
    public partial class SendMessageInput : UserControl
    {
        private bool isEmpty = false;
        private TextBox? inputField;
        public event RoutedEventHandler Click;

        public SendMessageInput()
        {
            InitializeComponent();
        }

        // Placeholder DependencyProperty
        public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
            "SendMessagePlaceholder", // Der Name des Propertys
            typeof(string), // Der Typ des Propertys
            typeof(SendMessageInput), // Der Typ, in dem das Property registriert wird
            new PropertyMetadata("Add message here...") // Standardwert und Property-Metadata
        );

        // CLR-Wrapper für das DependencyProperty
        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            "SendMessageText", // Der Name des Propertys
            typeof(string), // Der Typ des Propertys
            typeof(SendMessageInput), // Der Typ, in dem das Property registriert wird
            new PropertyMetadata("") // Standardwert und Property-Metadata
        );

        // CLR-Wrapper für das DependencyProperty
        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public static new readonly DependencyProperty FontSizeProperty =
            DependencyProperty.Register(
                "SendMessageInputFontSize", // Der Name des Propertys
                typeof(string), // Der Typ des Propertys
                typeof(SendMessageInput), // Der Typ, in dem das Property registriert wird
                new PropertyMetadata("20") // Standardwert und Property-Metadata
            );

        public void Clear()
        {
            DeleteButton_Click(null, null);
        }

        // CLR-Wrapper für das DependencyProperty
        public new string FontSize
        {
            get => (string)GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        private void Image_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { }

        private void InputField_GotFocus(object sender, RoutedEventArgs e)
        {
            CheckInputContent((TextBox)sender, true);
        }

        private void InputField_LostFocus(object sender, RoutedEventArgs e)
        {
            CheckInputContent((TextBox)sender, false);
        }

        private void InputField_Loaded(object sender, RoutedEventArgs e)
        {
            inputField = (TextBox)sender;
            CheckInputContent((TextBox)sender, false);
        }

        private void CheckInputContent(TextBox textBox, bool gotFocus)
        {
            if (gotFocus && isEmpty)
            {
                isEmpty = false;
                textBox.Text = "";
                textBox.Foreground = new SolidColorBrush(Colors.Black);
            }
            else if (textBox.Text == "")
            {
                isEmpty = true;
                textBox.Text = (string)GetValue(PlaceholderProperty);
                textBox.Foreground = new SolidColorBrush(Colors.Gray);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            inputField!.Text = "";
            CheckInputContent(inputField, false);
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            Click?.Invoke(this, e);
        }
    }
}

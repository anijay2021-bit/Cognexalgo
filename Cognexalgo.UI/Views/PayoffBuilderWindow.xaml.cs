using System.Windows;

namespace Cognexalgo.UI.Views
{
    public partial class PayoffBuilderWindow : Window
    {
        // Static arrays for DataGrid ComboBox columns
        public static readonly string[] ActionOptions    = { "BUY", "SELL" };
        public static readonly string[] OptionTypeOptions = { "CE", "PE" };

        public PayoffBuilderWindow()
        {
            InitializeComponent();
        }
    }
}

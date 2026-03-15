using System.Windows;
using System.Windows.Controls;
using Cognexalgo.UI.ViewModels;

namespace Cognexalgo.UI.Views
{
    public partial class PayoffBuilderWindow : Window
    {
        public static readonly string[] ActionOptions     = { "BUY", "SELL" };
        public static readonly string[] OptionTypeOptions = { "CE", "PE" };

        public PayoffBuilderWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Shared handler for NeutralList, BullishList, BearishList SelectionChanged.
        /// Fires SelectTemplateCommand and immediately clears selection so the same
        /// strategy can be re-clicked (e.g. after editing Spot or Lots).
        /// </summary>
        private void OnStrategySelected(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not PayoffBuilderViewModel vm) return;
            if (sender is not ListBox lb) return;
            if (lb.SelectedItem is not string name) return;

            vm.SelectTemplateCommand.Execute(name);
            lb.SelectedItem = null;
        }
    }
}

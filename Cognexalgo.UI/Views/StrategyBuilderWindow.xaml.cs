using System.Collections.Generic;
using System.Windows;

namespace Cognexalgo.UI.Views
{
    public partial class StrategyBuilderWindow : Window
    {
        // F8: Adjustment trigger options for the per-leg DataGrid ComboBox
        public static readonly List<string> AdjustmentTriggers = new() { "None", "ParentLegPnL", "UnderlyingMove" };

        public StrategyBuilderWindow()
        {
            InitializeComponent();
        }
    }
}

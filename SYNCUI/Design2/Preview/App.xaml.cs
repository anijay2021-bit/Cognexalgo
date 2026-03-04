using System;
using System.Windows;

namespace Cognexalgo.UI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(
                "Ngo9BigBOggjHTQxAR8/V1JGaF1cXmhNYVBpR2NbeU51flBCalhSVAciSV9jS3hTdUVnWXpbdXFQRWJVVU91XQ==");
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
                MessageBox.Show(ex.ExceptionObject?.ToString() ?? "Unknown error",
                    "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            base.OnStartup(e);
        }
    }
}

using System.Windows;

namespace OptiGames;

public partial class App : Application
{
    private bool _reportingFault;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            // Showing a dialog pumps the dispatcher, which re-runs the layout pass that
            // threw — without this guard a single bad binding recurses until the stack
            // overflows instead of surfacing one readable message.
            if (_reportingFault)
            {
                args.Handled = true;
                return;
            }

            _reportingFault = true;
            try
            {
                MessageBox.Show(args.Exception.ToString(), "OptiGames hit a problem",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _reportingFault = false;
            }

            args.Handled = true;
        };

        base.OnStartup(e);
    }
}

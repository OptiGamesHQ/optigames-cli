using System.Windows;
using System.Windows.Controls;
using OptiGames.ViewModels;

namespace OptiGames.Views;

public partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();

    /// <summary>
    /// Hands the sparkline strip's real width to the view model so it can lay the trace out in
    /// device pixels. Done here rather than through a binding because a view model property
    /// cannot be a binding target, and the OneWayToSource variants of this all end up pushing
    /// from a property the Border does not own.
    /// </summary>
    private void Spark_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is HomeViewModel vm) vm.SparkWidth = e.NewSize.Width;
    }
}

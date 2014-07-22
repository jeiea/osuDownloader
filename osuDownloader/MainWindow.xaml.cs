using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace OsuDownloader
{
/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
	public MainWindow()
	{
		InitializeComponent();

		CommandBinding closeCommand = new CommandBinding(ApplicationCommands.Close, CloseCommandHandler);
		CommandBindings.Add(closeCommand);

		MascotBtn.IsChecked = OsuHooker.IsHooked;
	}

	private void CloseCommandHandler(object sender, ExecutedRoutedEventArgs e)
	{
		Application.Current.Shutdown();
	}

	private void Mascot_Click(object sender, RoutedEventArgs e)
	{
		OsuHooker.Hook();

		MascotBtn.IsChecked = OsuHooker.IsHooked;
	}
}
}

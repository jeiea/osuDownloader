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
	System.Windows.Forms.NotifyIcon Tray;

	public MainWindow()
	{
		InitializeComponent();

		CommandBinding closeCommand = new CommandBinding(ApplicationCommands.Close, CloseCommandHandler);
		CommandBindings.Add(closeCommand);

		MascotBtn.IsChecked = OsuHooker.IsHooked;

		// 트레이 아이콘 생성과 등록
		Tray = new System.Windows.Forms.NotifyIcon();
		Tray.Icon = Properties.Resources.osuIcon;
		Tray.Visible = true;
		Tray.DoubleClick += (s, e) =>
		{
			this.Show();
			this.WindowState = WindowState.Normal;
		};
		Tray.MouseDown += new System.Windows.Forms.MouseEventHandler(notifier_MouseDown);
	}

	void notifier_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
	{
		if (e.Button == System.Windows.Forms.MouseButtons.Right)
		{
			ContextMenu menu = (ContextMenu)this.FindResource("TrayContextMenu");
			menu.IsOpen = true;
		}
	}

	protected override void OnStateChanged(EventArgs e)
	{
		if (WindowState == WindowState.Minimized)
		{
			this.Hide();
		}
		base.OnStateChanged(e);
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

	private void BlCatBtn_Click(object sender, RoutedEventArgs e)
	{
	}

	private void MenuItem_Click(object sender, RoutedEventArgs e)
	{

	}
}
}

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

		// Generate and register notifier icon.
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
			menu.CommandBindings.AddRange(CommandBindings);
			Mouse.Capture(menu);
			menu.LostFocus += menu_LostFocus;
			menu.LostMouseCapture += menu_LostFocus;
			menu.IsOpen = true;
		}
	}

	private void menu_LostFocus(object sender, RoutedEventArgs e)
	{
		ContextMenu menu = (ContextMenu)this.FindResource("TrayContextMenu");
		menu.IsOpen = false;
	}

	class CloseCommand : ICommand
	{
		public bool CanExecute(object parameter)
		{
			return true;
		}

		public event EventHandler CanExecuteChanged;

		public void Execute(object parameter)
		{
			throw new NotImplementedException();
		}
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Replace minimize action to hiding window. </summary>
	///
	/// <param name="e">    . </param>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	protected override void OnStateChanged(EventArgs e)
	{
		if (WindowState == WindowState.Minimized)
		{
			this.Hide();
		}
		base.OnStateChanged(e);
	}

	protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
	{
		base.OnClosed(e);
		//when user [Alt + F4] , this app will not work window close command.
		//instead, this app window change to minimized state.
		WindowState = System.Windows.WindowState.Minimized;
		e.Cancel = true;
	}

	private void CloseCommandHandler(object sender, ExecutedRoutedEventArgs e)
	{
		Tray.Dispose();
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

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Toggle hooking status. </summary>
	///
	/// <param name="sender">   Source of the event. </param>
	/// <param name="e">        Routed event information. </param>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	private void MenuHookToggle_Click(object sender, RoutedEventArgs e)
	{

	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		this.Hide();
	}
}
}

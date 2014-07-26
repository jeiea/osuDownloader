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
	/// <summary>   The timer which check hooking is enabled. </summary>
	System.Windows.Threading.DispatcherTimer PingTimer;

	public MainWindow()
	{
		InitializeComponent();

		MascotBtn.IsChecked = OsuHooker.IsInjected;

		PingTimer = new System.Windows.Threading.DispatcherTimer();
		PingTimer.Interval = TimeSpan.FromSeconds(1);
		PingTimer.Tick += CheckHooking;
		PingTimer.Start();
	}

	private void CheckHooking(object sender, EventArgs e)
	{
		if (MascotBtn.IsChecked != OsuHooker.IsHooking)
		{
			MascotBtn.IsChecked = OsuHooker.IsHooking;
		}
	}

	protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
	{
		DragMove();
		base.OnMouseLeftButtonDown(e);
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
			DismissWindow();
		}
		base.OnStateChanged(e);
	}

	private void DismissWindow()
	{
		PingTimer.Stop();
		this.Hide();
	}

	private void PresentWindow()
	{
		PingTimer.Start();
		this.Show();
	}

	protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
	{
		base.OnClosed(e);
		//when user [Alt + F4] , this app will not work window close command.
		//instead, this app window change to minimized state.
		WindowState = System.Windows.WindowState.Minimized;
		e.Cancel = true;
	}

	private void MenuExit_Click(object sender, RoutedEventArgs e)
	{
		Tray.Dispose();
		Application.Current.Shutdown();
	}

	private void BlCatBtn_Click(object sender, RoutedEventArgs e)
	{
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			DismissWindow();
		}
		base.OnKeyDown(e);
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Toggle hooking status. </summary>
	///
	/// <param name="sender">   Source of the event. </param>
	/// <param name="e">        Routed event information. </param>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	private void TryHook(object sender, RoutedEventArgs e)
	{
		OsuHooker.Hook();

		MascotBtn.IsChecked = OsuHooker.IsInjected;
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		DismissWindow();
	}

	private void FirstPage_Executed(object sender, ExecutedRoutedEventArgs e)
	{
		PresentWindow();
	}

	private void MenuWindow_Click(object sender, RoutedEventArgs e)
	{
		PresentWindow();
	}
}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Hardcodet.Wpf.TaskbarNotification;

namespace OsuDownloader
{
/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
	/// <summary>   The timer which check hooking is enabled. </summary>

	Hardcodet.Wpf.TaskbarNotification.TaskbarIcon Tray;
	MenuItem ToggleHookItem;

	OsuHooker Hooker = new OsuHooker();

	public MainWindow()
	{
		Initialized += WindowSettings_Loaded;

		Hide();
		InitializeComponent();

		#region Tray registration routine

		// Xaml seems to have problem with project settings.
		Tray = new TaskbarIcon()
		{
			IconSource = new BitmapImage(new Uri("pack://application:,,,/Pic/osuIcon.ico")),
			ToolTipText = "Osu Beatmap Downloader v1.0",
		};
		Tray.TrayMouseDoubleClick += ShowWindow_Handler;

		var contextMenu = new ContextMenu();
		contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
		contextMenu.IsVisibleChanged += ContextMenu_IsVisibleChanged;

		var headers = new[] { "창 띄우기", "켜기", "종료" };
		var clicks = new RoutedEventHandler[] { ShowWindow_Handler, ToggleHooking, MenuExit_Click };
		for (int i = 0; i < headers.Length; i++)
		{
			var item = new MenuItem();
			item.Header = headers[i];
			item.Click += clicks[i];
			contextMenu.Items.Add(item);
		}

		Tray.ContextMenu = contextMenu;
		ToggleHookItem = (MenuItem)contextMenu.Items[1];

		#endregion
	}

	void WindowSettings_Loaded(object sender, EventArgs e)
	{
		var setting = Properties.Settings.Default;

		this.DataContext = Hooker;
		AutoStart.IsChecked = setting.AutoStart;
		StartAsTray.IsChecked = setting.StartAsTray;
		AutoTerminate.IsChecked = setting.AutoTerminate;

		if (setting.StartAsTray == false)
		{
			Show();
			Activate();
		}
		if (setting.AutoStart)
		{
			Hooker.ToggleHook();
		}
		if (setting.AutoTerminate)
		{

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
			Hide();
		}
		base.OnKeyDown(e);
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Toggle hooking status. </summary>
	///
	/// <param name="sender">   Source of the event. </param>
	/// <param name="e">        Routed event information. </param>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	private void ToggleHooking(object sender, RoutedEventArgs e)
	{
		Hooker.ToggleHook();
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		Hide();
	}

	private void ShowWindow_Handler(object sender, RoutedEventArgs e)
	{
		Show();
		Activate();
	}

	private void ContextMenu_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
	{
		ToggleHookItem.Header = Hooker.IsHooking ? "끄기" : "켜기";
	}

	private void AutoStart_Click(object sender, RoutedEventArgs e)
	{
		Properties.Settings.Default["AutoStart"] = AutoStart.IsChecked ?? false;
		Properties.Settings.Default.Save();
	}

	private void StartAsTray_Click(object sender, RoutedEventArgs e)
	{
		Properties.Settings.Default["StartAsTray"] = StartAsTray.IsChecked ?? false;
		Properties.Settings.Default.Save();
	}
}
}

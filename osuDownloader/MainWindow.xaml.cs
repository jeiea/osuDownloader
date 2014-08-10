using Hardcodet.Wpf.TaskbarNotification;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace OsuDownloader
{
/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
	/// <summary>   The timer which check hooking is enabled. </summary>
	public static MainWindow InUseWindow;

	TaskbarIcon Tray;
	MenuItem ToggleHookItem;

	MainWindowViewModel Hooker = new MainWindowViewModel();

	public MainWindow()
	{
		InUseWindow = this;
		Initialized += Window_Loaded;

		Hide();

		DataContext = Hooker;
		InitializeComponent();

		#region Tray registration routine

		// Xaml seems to have problem with project settings.
		Tray = new TaskbarIcon()
		{
			IconSource = new BitmapImage(new Uri("pack://application:,,,/icon2.ico")),
			ToolTipText = "Osu Beatmap Downloader v2",
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

	void Window_Loaded(object sender, EventArgs e)
	{
		if (StartAsTray.IsChecked == false)
		{
			Show();
			Activate();
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
		var resName = (bool)BlCatBtn.IsChecked ? "Appearing" : "Disappearing";
		BloodcatPopup.BeginStoryboard(BloodcatPopup.Resources[resName] as Storyboard);
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
		Hooker.IsHooking = !Hooker.IsHooking;
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

	private void OsuBtn_Click(object sender, RoutedEventArgs e)
	{
	}

	private void ColorBrush_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		var dlg = new System.Windows.Forms.ColorDialog();
		dlg.AllowFullOpen = true;
		var color = Hooker.BloodcatOption.BackgroundColor;
		dlg.Color = System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);

		if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
		{
			var col = dlg.Color;
			color = System.Windows.Media.Color.FromArgb(col.A, col.R, col.G, col.B);
			Hooker.BloodcatOption.BackgroundColor = color;
			// Invoke changed event
			Hooker.BloodcatOption = Hooker.BloodcatOption;
		}
	}
}

public class BloodcatDownOptionConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter,
						  System.Globalization.CultureInfo culture)
	{
		BloodcatDownloadOption option = value as BloodcatDownloadOption;
		if (parameter == null || option == null)
		{
			return false;
		}

		switch (parameter as string)
		{
		case "NoTouch":
			return option.Background == BloodcatWallpaperOption.NoTouch;
		case "SolidColor":
			return option.Background == BloodcatWallpaperOption.SolidColor;
		case "RemoveBackground":
			return option.Background == BloodcatWallpaperOption.RemoveBackground;
		case "ColorBrush":
			return new SolidColorBrush(option.BackgroundColor);
		case "RemoveSkin":
			return option.RemoveSkin;
		case "RemoveVideoAndStoryboard":
			return option.RemoveVideoAndStoryboard;
		}

		throw new Exception("Not expected download option.");
	}

	public object ConvertBack(object value, Type targetType, object parameter,
							  System.Globalization.CultureInfo culture)
	{
		BloodcatDownloadOption option = new BloodcatDownloadOption();

		var window = MainWindow.InUseWindow;
		if (window.NoTouch.IsChecked ?? false)
		{ }
		else if (window.SolidColor.IsChecked ?? false)
		{
			var brush = window.ColorBrush.Fill as SolidColorBrush;
			option.BackgroundColor = brush.Color;
		}
		else if (window.RemoveBackground.IsChecked ?? false)
		{
			option.Background = BloodcatWallpaperOption.RemoveBackground;
		}

		option.RemoveVideoAndStoryboard = window.RemoveVideoAndStoryboard.IsChecked ?? false;
		option.RemoveSkin = window.RemoveSkin.IsChecked ?? false;

		switch (parameter as string)
		{
		case "NoTouch":
			option.Background = BloodcatWallpaperOption.NoTouch;
			break;
		case "SolidColor":
			option.Background = BloodcatWallpaperOption.SolidColor;
			break;
		case "RemoveBackground":
			option.Background = BloodcatWallpaperOption.RemoveBackground;
			break;
		case "RemoveSkin":
			option.RemoveSkin = (bool)value;
			break;
		case "RemoveVideoAndStoryboard":
			option.RemoveVideoAndStoryboard = (bool)value;
			break;
		}

		return option;
	}
}
}

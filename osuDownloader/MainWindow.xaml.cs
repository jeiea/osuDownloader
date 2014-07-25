using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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

		var tray = new System.Windows.Forms.NotifyIcon();
		tray.Icon = new System.Drawing.Icon("pack://application:,,,/pic/osuLogo.png");
		tray.Visible = true;
		tray.DoubleClick += (s, e) =>
		{
			this.Show();
			this.WindowState = WindowState.Normal;
		};
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

	[StructLayout(LayoutKind.Sequential)]
	public struct SHELLEXECUTEINFO
	{
		public int cbSize;
		public uint fMask;
		public IntPtr hwnd;
		[MarshalAs(UnmanagedType.LPTStr)]
		public string lpVerb;
		[MarshalAs(UnmanagedType.LPTStr)]
		public string lpFile;
		[MarshalAs(UnmanagedType.LPTStr)]
		public string lpParameters;
		[MarshalAs(UnmanagedType.LPTStr)]
		public string lpDirectory;
		public int nShow;
		public IntPtr hInstApp;
		public IntPtr lpIDList;
		[MarshalAs(UnmanagedType.LPTStr)]
		public string lpClass;
		public IntPtr hkeyClass;
		public uint dwHotKey;
		public IntPtr hIcon;
		public IntPtr hProcess;
	}

	[DllImport("shell32.dll", CharSet = CharSet.Auto)]
	static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

	private void BlCatBtn_Click(object sender, RoutedEventArgs e)
	{
	}
}
}

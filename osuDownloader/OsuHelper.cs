using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OsuDownloader
{
static class OsuHelper
{
	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Gets osu path from registry and suspected location. </summary>
	///
	/// <returns>   The osu!.exe path. </returns>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	public static string GetOsuPath()
	{
		// Find from existing processes.
		var osuProcs = Process.GetProcessesByName("osu!");
		if (osuProcs.Count() >= 1)
		{
			return osuProcs[0].MainModule.FileName;
		}

		// Find from settings.
		string savedPath = Properties.Settings.Default.OsuPath;
		if (savedPath != null && File.Exists(savedPath))
		{
			return savedPath;
		}

		// Find from registry.
		var valuesContainOsuPath = new string[]
		{
			"HKEY_LOCAL_MACHINE\\SOFTWARE\\Classes\\osu!\\shell\\open\\command",
			"HKEY_LOCAL_MACHINE\\SOFTWARE\\Classes\\osu\\shell\\open\\command",
			"HKEY_LOCAL_MACHINE\\SOFTWARE\\Classes\\osu!\\DefaultIcon",
			"HKEY_LOCAL_MACHINE\\SOFTWARE\\Classes\\osu\\DefaultIcon",
		};
		foreach (var regPath in valuesContainOsuPath)
		{
			var beatmapOpenCommand = (string)Registry.GetValue(regPath, "", null);
			if (beatmapOpenCommand != null)
			{
				string executablePath = beatmapOpenCommand.Split('"')[1];
				if (File.Exists(executablePath))
				{
					return executablePath;
				}
			}
		}

		// Find suspecting directory.

		// Constant program files.
		var suspectingPaths = new List<string>
		{
			"C:\\Program Files",
			"C:\\Program Files (x86)",
		};

		// Environmental program files.
		suspectingPaths.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

#if NET20 || NET35
		suspectingPaths.Add(Environment.GetEnvironmentVariable("ProgramFiles(x86)"));
#else
		suspectingPaths.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
#endif

		// root directories.
		foreach (var drive in DriveInfo.GetDrives())
		{
			suspectingPaths.Add(drive.Name);
		}

		// This executable's parent paths.
		string executingDir = System.Reflection.Assembly.GetExecutingAssembly().Location;
		while (executingDir != Path.GetPathRoot(executingDir))
		{
			executingDir = Path.GetDirectoryName(executingDir);
			suspectingPaths.Add(executingDir);
		}

		// Query all.
		foreach (string path in suspectingPaths.Distinct())
		{
			if (File.Exists(Path.Combine(path, "osu!.exe")))
			{
				return Path.Combine(path, "osu!.exe");
			}
			if (File.Exists(Path.Combine(path, "osu!\\osu!.exe")))
			{
				return Path.Combine(path, "osu!\\osu!.exe");
			}
		}

		// Not found. Ask to user.
		Window tempWindow = new Window()
		{
			Width = 0,
			Height = 0,
			ShowInTaskbar = false,
			WindowStyle = WindowStyle.None,
			WindowStartupLocation = WindowStartupLocation.Manual,
			Left = -10000,
			Top = -10000,
		};
		tempWindow.Show();

		try
		{
			var ofd = new Microsoft.Win32.OpenFileDialog();
			ofd.Title = "오스 실행파일 위치를 입력하세요";
			ofd.Filter = "오스 실행파일|osu!.exe|모든 파일|*.*";
			ofd.Multiselect = false;
			ofd.CheckFileExists = true;
			if (ofd.ShowDialog() ?? false)
			{
				Properties.Settings.Default.OsuPath = ofd.FileName;
				Properties.Settings.Default.Save();
				return ofd.FileName;
			}
		}
		catch (Exception e)
		{
			MainWindowViewModel.LogException(e);
		}

		return null;
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Query beatmap which has 'sid' already had downloaded. </summary>
	///
	/// <param name="sid">  The beatmap id. </param>
	///
	/// <returns>   true if existing beatmap, false if not. </returns>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	public static bool IsTakenBeatmap(int sid)
	{
		try
		{
			string osuPath = Path.GetDirectoryName(GetOsuPath());
			string songsPath = Path.Combine(osuPath, "Songs");

#if NET20 || NET35
			int count = Directory.GetDirectories(songsPath)
#else
			int count = Directory.EnumerateDirectories(songsPath)
#endif
						.Where((s) => Path.GetFileName(s).StartsWith(sid + " "))
						.Count();
			return count > 0;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static Rect GetOsuWindowClient()
	{
		var osuProcs = Process.GetProcessesByName("osu!");

		IntPtr mainHwnd = IntPtr.Zero;
		foreach (var proc in osuProcs)
		{
			if (proc.MainWindowHandle != IntPtr.Zero)
			{
				mainHwnd = proc.MainWindowHandle;
				break;
			}
		}

		if (mainHwnd == IntPtr.Zero)
			return Rect.Empty;

		//WINDOWPLACEMENT placement = new WINDOWPLACEMENT();
		//placement.length = Marshal.SizeOf(placement);
		//GetWindowPlacement(mainHwnd, ref placement);

		//var rect = placement.rcNormalPosition;

		var rt = new System.Drawing.Rectangle();
		GetClientRect(mainHwnd, out rt);

		if (rt.IsEmpty)
		{
			return Rect.Empty;
		}

		var lt = new System.Drawing.Point(rt.Left, rt.Top);
		ClientToScreen(mainHwnd, ref lt);
		var rb = new System.Drawing.Point(rt.Right, rt.Bottom);
		ClientToScreen(mainHwnd, ref rb);

		return new Rect(lt.X, lt.Y, rb.X - lt.X, rb.Y - lt.Y);
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

	private struct WINDOWPLACEMENT
	{
		public int length;
		public int flags;
		public Injectee.ShowWindowCommands showCmd;
		public System.Drawing.Point ptMinPosition;
		public System.Drawing.Point ptMaxPosition;
		public System.Drawing.Rectangle rcNormalPosition;
	}

	[DllImport("user32.dll", SetLastError = true)]
	static extern bool GetClientRect(IntPtr hwnd, out System.Drawing.Rectangle lpRect);

	[DllImport("user32.dll")]
	static extern bool ClientToScreen(IntPtr hWnd, ref System.Drawing.Point lpPoint);
}
}

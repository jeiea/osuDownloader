using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace OsuDownloader
{
static class OsuHelper
{
	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Gets osu path from registry. </summary>
	///
	/// <returns>   The osu!.exe path. </returns>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	public static string GetOsuPath()
	{
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
		suspectingPaths.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

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
			OsuHooker.LogException(e);
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
			int count = Directory.EnumerateDirectories(songsPath)
						.Where((s) => s.StartsWith(sid + " "))
						.Count();
			return count > 0;
		}
		catch (Exception)
		{
			return false;
		}
	}
}
}

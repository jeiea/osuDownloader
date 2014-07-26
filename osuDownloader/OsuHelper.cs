using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Win32;

namespace OsuDownloader
{
static class OsuHelper
{
	const string BeatmapHandlerKey = "HKEY_LOCAL_MACHINE\\SOFTWARE\\Classes\\osu!\\shell\\open\\command";

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Gets osu path from registry. </summary>
	///
	/// <returns>   The osu!.exe path. </returns>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	public static string GetOsuPath()
	{
		var beatmapOpenCommand = (string)Registry.GetValue(BeatmapHandlerKey, "", null);
		if (beatmapOpenCommand != null)
		{
			string executablePath = beatmapOpenCommand.Split('"')[1];
			return executablePath;
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

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace OsuDownloader.Injectee
{

class OszDownloader
{
	public static IOverlayer Overlayer;

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>
	/// The requested beatmap list to determine whether or not clicked before. It's used at force
	/// download.
	/// </summary>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	static Dictionary<int, RequestHistory> RequestedBeatmaps = new Dictionary<int, RequestHistory>();

	/// <summary>   Full pathname of the downloading file. </summary>
	static Dictionary<WebClient, string> DownloadPath = new Dictionary<WebClient, string>();

	// TODO: Consider asynchoronous and case of download file name exists.
	#region Download routine

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Download from bloodcat mirror. </summary>
	///
	/// <param name="request"> Official beatmap thread url or bloodcat.com beatmap url. </param>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	public static void AutomaticDownload(string request)
	{
		WebClient client = new WebClient() { Encoding = Encoding.UTF8 };
		var query = client.QueryString;
		query.Add("mod", "json");

		Uri uri = new Uri(request);
		if (uri.Host == "osu.ppy.sh" && "b/d/s/".Contains(uri.Segments[1]))
		{
			// b = each diffibulty id, s = beatmap id, d = beatmap id as download link.
			query.Add("m", uri.Segments[1][0] == 'b' ? "b" : "s");
			query.Add("q", uri.Segments[2]);
		}
		else if (uri.Host == "bloodcat.com" && uri.AbsolutePath.StartsWith("/osu/m/"))
		{
			// Id extracted from bloodcat.com link
			query.Add("m", "s");
			query.Add("q", uri.Segments[3]);
		}

		// Query to bloodcat.com whether the beatmap exists.

		string json = client.DownloadString("http://bloodcat.com/osu/");
		var jsonStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
		var jsonParser = new DataContractJsonSerializer(typeof(BloodcatContainer));
		var container = (BloodcatContainer)jsonParser.ReadObject(jsonStream);

		// If we use CsQuery, then below is possible.
		//dynamic result = JSON.ParseJSON(json);

		string downloadLink = null;
		int beatmapId = -1;
		if (container.resultCount != 1)
		{
			// If not found or undecidable, open official page if not logged in.
			if (string.IsNullOrEmpty(Properties.Settings.Default.OfficialSession))
			{
				ContinueOpen(request);
				return;
			}

			// If we only have difficulty ID, we should convert it to beatmap ID.
			if (query["m"] == "b")
			{
				string officialBeatmapPage = client.DownloadString(request);
				string[] beatmapIdPatterns = new string[]
				{
					@"osu.ppy.sh/[ds]/(\d+)",
					@"/pages/include/beatmap-rating-graph.php?s=(\d+)",
					@"b.ppy.sh/thumb/(\d+)",
					"s\\s*:\\s*\"(\\d+)\"\\s*,",
				};
				foreach (string beatmapIdPattern in beatmapIdPatterns)
				{
					Match match = Regex.Match(officialBeatmapPage, beatmapIdPattern);
					if (match.Success)
					{
						beatmapId = int.Parse(match.Groups[1].Value);
						break;
					}
				}
				// Cannot find beatmap id.
				if (beatmapId == -1)
				{
					ContinueOpen(request);
					return;
				}
			}
			else
			{
				beatmapId = int.Parse(query["q"]);
			}
			const string OfficialDownloadUrl = "http://osu.ppy.sh/d/";
			downloadLink = OfficialDownloadUrl + beatmapId;
		}
		else
		{
			const string BloodcatDownloadUrl = "http://bloodcat.com/osu/m/";
			beatmapId = container.results[0].id;
			downloadLink = BloodcatDownloadUrl + beatmapId;
		}

		// Copy URL for sharing to clipboard.
		Clipboard.SetText(downloadLink);

		// Add history if not exists.
		if (RequestedBeatmaps.ContainsKey(beatmapId) == false)
		{
			RequestedBeatmaps[beatmapId] = new RequestHistory()
			{
				LastRequested = DateTime.MinValue,
				Status = RequestResult.Operating,
			};
		}
		RequestHistory history = RequestedBeatmaps[beatmapId];

		// Download if not exists or double clicked.
		if (OsuHelper.IsTakenBeatmap(beatmapId) == false ||
			(DateTime.Now - history.LastRequested).TotalSeconds < 1)
		{
			DownloadAndExecuteOsz(downloadLink);
		}
		else
		{
			Overlayer.AddMessage(new object(), new NoticeEntry()
			{
				Begin = DateTime.Now,
				Duration = TimeSpan.FromSeconds(2),
				Message = "이미 있는 비트맵입니다. URL이 클립보드로 복사되었습니다. 받으시려면 더블클릭 해 주세요."
			});
		}

		history.LastRequested = DateTime.Now;

	}

	private static void ContinueOpen(string request)
	{
		Process.Start(new ProcessStartInfo()
		{
			UseShellExecute = true,
			Verb = "open",
			FileName = request,
		});
	}

	static void DownloadAndExecuteOsz(string url)
	{
		WebClient client = new WebClient() { Encoding = Encoding.UTF8 };

		var setting = Properties.Settings.Default;
		var uri = new Uri(url);
		string cookie = null;
		switch (uri.Host)
		{
		case "bloodcat.com":
			cookie = setting.BloodcatOption.ToCookie().ToString();
			break;
		case "osu.ppy.sh":
			client.Headers["User-Agent"] = "Mozilla/5.0 (Windows NT 6.3; WOW64; Trident/7.0; rv:11.0) like Gecko";
			cookie = setting.OfficialSession;
			break;
		}
		client.Headers.Add(HttpRequestHeader.Cookie, cookie);

		client.DownloadProgressChanged += new DownloadProgressChangedEventHandler(client_DownloadProgressChanged);
		client.DownloadFileCompleted += new AsyncCompletedEventHandler(client_DownloadFileCompleted);

		string tempFile = Path.GetTempFileName();
		DownloadPath.Add(client, tempFile);

		client.DownloadFileAsync(uri, tempFile);

		Overlayer.AddMessage(client, new ProgressEntry()
		{
			Begin = DateTime.Now,
			// TODO: Accurate name via shared information struct.
			Title = Path.GetFileNameWithoutExtension(url),
			Total = int.MaxValue,
		});
	}

	static void client_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
	{
		try
		{
			if (Overlayer == null)
				return;

			ProgressEntry entry = (ProgressEntry)Overlayer.GetMessageQueue()[sender];

			if (entry.Total != e.TotalBytesToReceive)
			{
				var client = (WebClient)sender;

				if (client.ResponseHeaders["Content-Type"].Contains("application/") == false)
				{
					Overlayer.AddMessage(new object(), new NoticeEntry()
					{
						Begin = DateTime.Now,
						Duration = TimeSpan.FromSeconds(2),
						Message = "다운로드에 실패했습니다. 파일을 찾지 못했습니다."
					});
					client.CancelAsync();
					return;
				}

				var fileName = GetFileNameFromDisposition(client.ResponseHeaders["Content-Disposition"]);
				entry.Title = Path.GetFileNameWithoutExtension(fileName);

				entry.Total = e.TotalBytesToReceive;
			}

			entry.Downloaded = e.BytesReceived;
		}
		catch (Exception ex)
		{
			MainWindowViewModel.LogException(ex);
		}
	}

	static string GetFileNameFromDisposition(string disposition)
	{
		return Regex.Match(disposition, "filename\\s*=\\s*\"(.*?)\"").Groups[1].ToString();
	}

	static void client_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
	{
		try
		{
			if (e.Cancelled)
			{
				MainWindowViewModel.LogException(e.Error);
				return;
			}

			WebClient client = (WebClient)sender;

			if (Overlayer != null)
			{
				Overlayer.GetMessageQueue().Remove(client);
			}

			var tempFile = DownloadPath[client];
			var destName = GetFileNameFromDisposition(client.ResponseHeaders["Content-Disposition"]);
			var destFile = Path.Combine(Path.GetDirectoryName(tempFile), destName);

			File.Move(tempFile, destFile);

			string osuExePath = Process.GetCurrentProcess().MainModule.FileName;

			try
			{
				Process.Start(osuExePath, destFile);
			}
			// TODO: IDropTarget으로 강제 갱신하는 방법 있음
			catch (Win32Exception)
			{
				string[] pathElements = new string[]
				{
					Path.GetDirectoryName(osuExePath), "Songs", destName,
				};
				// For .NET 3.5
				string destPath = pathElements.Aggregate(Path.Combine);
				File.Move(destName, destPath);
			}
		}
		catch (Exception ex)
		{
			MainWindowViewModel.LogException(ex);
		}
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Filter character which cannot be file name. </summary>
	///
	/// <param name="name"> The name. </param>
	///
	/// <returns>   The string removed invalid characters. </returns>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	private static string GetValidFileName(string name)
	{
		return string.Concat(name.Except(System.IO.Path.GetInvalidFileNameChars()));
	}

	#endregion

	enum RequestResult
	{
		Queued,
		Operating,
		Downloaded,
		/// <summary>   Represents this beatmap is not downloaded since exists. </summary>
		PendedExisting,
		BloodcatRejected,
		OfficialLoginRequired,
	}

	class RequestHistory
	{
		public DateTime LastRequested;
		public RequestResult Status;
	}

}

}

using SHDocVw;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace OsuDownloader.Injectee
{
////////////////////////////////////////////////////////////////////////////////////////////////////
/// <summary>
/// bloodcat.com JSON result format, also represents beatmap info. For deserialization, member
/// name casing is treated as exception.
/// </summary>
////////////////////////////////////////////////////////////////////////////////////////////////////
[DataContract]
internal class BloodcatResult
{
	[DataMember]
	public int id;
	[DataMember]
	public string artist = string.Empty;
	[DataMember]
	public string title = string.Empty;
	[DataMember]
	public string creator = string.Empty;
}

[DataContract]
internal class BloodcatContainer
{
	[DataMember]
	public int resultCount;

	[DataMember]
	public BloodcatResult[] results;
}

class OszDownloader
{
	public static IOverlayer Overlayer;

	const string FakeUserAgent = "Mozilla/5.0 (Windows NT 6.3; WOW64; Trident/7.0; rv:11.0) like Gecko";

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>
	/// The requested beatmap list to determine whether or not clicked before. It's used at force
	/// download.
	/// </summary>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	static Dictionary<int, RequestHistory> RequestedBeatmaps = new Dictionary<int, RequestHistory>();

	string Request;

	BloodcatResult BeatmapInfo = new BloodcatResult();

	/// <summary>   Full pathname of the temporary downloading file. </summary>
	string DownloadPath;

	WebClient GetWebClient(string url)
	{
		WebClient client = new WebClient() { Encoding = Encoding.UTF8 };

		client.Headers[HttpRequestHeader.UserAgent] = FakeUserAgent;

		client.DownloadProgressChanged += client_DownloadProgressChanged;
		client.DownloadFileCompleted += client_DownloadFileCompleted;

		return client;
	}

	// TODO: Consider asynchoronous and case of download file name exists.
	#region Download routine

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Download from bloodcat mirror. </summary>
	///
	/// <param name="request"> Official beatmap thread url or bloodcat.com beatmap url. </param>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	public void AutomaticDownload(string request)
	{
		Request = request;

		var client = new WebClient();
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
						BeatmapInfo.id = int.Parse(match.Groups[1].Value);
						break;
					}
				}
				// Cannot find beatmap id.
				if (BeatmapInfo.id == -1)
				{
					ContinueOpen(request);
					return;
				}
			}
			else
			{
				BeatmapInfo.id = int.Parse(query["q"]);
			}
			const string OfficialDownloadUrl = "http://osu.ppy.sh/d/";
			downloadLink = OfficialDownloadUrl + BeatmapInfo.id;

			// Official download page has javascript redirection.
			var ie = new InternetExplorer();
			ie.Visible = false;
			ie.Silent = true;
			ie.BeforeNavigate2 += ie_BeforeNavigate2;
			ie.FileDownload += ie_FileDownload;
			object cookie = "Cookie: " + Properties.Settings.Default.OfficialSession;
			ie.Navigate2(downloadLink, Headers: ref cookie);
		}
		else
		{
			const string BloodcatDownloadUrl = "http://bloodcat.com/osu/m/";
			BeatmapInfo = container.results[0];
			BeatmapInfo.id = container.results[0].id;
			downloadLink = BloodcatDownloadUrl + BeatmapInfo.id;
		}

		// Copy URL for sharing to clipboard.
		Clipboard.SetText(downloadLink);

		// Add history if not exists.
		if (RequestedBeatmaps.ContainsKey(BeatmapInfo.id) == false)
		{
			RequestedBeatmaps[BeatmapInfo.id] = new RequestHistory()
			{
				LastRequested = DateTime.MinValue,
				Status = RequestResult.Operating,
			};
		}
		RequestHistory history = RequestedBeatmaps[BeatmapInfo.id];

		// Download if not exists or double clicked.
		if (OsuHelper.IsTakenBeatmap(BeatmapInfo.id) == false ||
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

	void ie_FileDownload(bool ActiveDocument, ref bool Cancel)
	{
		Cancel = true;
	}

	void ie_BeforeNavigate2(object pDisp, ref object URL, ref object Flags, ref object TargetFrameName,
							ref object PostData, ref object Headers, ref bool Cancel)
	{
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

	void DownloadAndExecuteOsz(string url)
	{
		var setting = Properties.Settings.Default;
		var uri = new Uri(url);
		string cookie = null;

		var client = GetWebClient(url);
		switch (uri.Host)
		{
		case "bloodcat.com":
			cookie = setting.BloodcatOption.ToCookie().ToString();
			break;
		case "osu.ppy.sh":
			cookie = setting.OfficialSession;
			break;
		}
		client.Headers.Add(HttpRequestHeader.Cookie, cookie);

		DownloadPath = Path.GetTempFileName();

		client.DownloadFileAsync(uri, DownloadPath);

		string title = string.IsNullOrEmpty(BeatmapInfo.title)
					   ? Path.GetFileNameWithoutExtension(url)
					   : BeatmapInfo.artist + " - " + BeatmapInfo.title;

		Overlayer.AddMessage(client, new ProgressEntry()
		{
			Begin = DateTime.Now,
			// TODO: Accurate name via shared information struct.
			Title = title,
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
				string disposition = ValidateAndGetFileName(sender as WebClient);
				if (disposition == null)
				{
					(sender as WebClient).CancelAsync();
					return;
				}

				entry.Total = e.TotalBytesToReceive;
				entry.Title = Path.GetFileNameWithoutExtension(disposition);
			}

			entry.Downloaded = e.BytesReceived;
		}
		catch (Exception ex)
		{
			MainWindowViewModel.LogException(ex);
		}
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Check whether or not downloaded data is binary stream. </summary>
	///
	/// <param name="client" type="WebClient">  . </param>
	///
	/// <returns>   If not binary file, null, else Content-Disposition. </returns>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	private static string ValidateAndGetFileName(WebClient client)
	{
		if (client.ResponseHeaders["Content-Type"].Contains("application/") == false)
		{
			Overlayer.AddMessage(new object(), new NoticeEntry()
			{
				Begin = DateTime.Now,
				Duration = TimeSpan.FromSeconds(2),
				Message = "다운로드에 실패했습니다. 파일을 찾지 못했습니다."
			});
			return null;
		}

		return GetFileNameFromDisposition(client.ResponseHeaders["Content-Disposition"]);
	}

	static string GetFileNameFromDisposition(string disposition)
	{
		return Regex.Match(disposition, "filename\\s*=\\s*\"(.*?)\"").Groups[1].ToString();
	}

	void client_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
	{
		try
		{
			if (e.Cancelled)
			{
				// TODO: case of not logged in, retry other mirrors or just retry.
				Process.Start("iexplore", Request);
				MainWindowViewModel.LogException(e.Error);
				return;
			}

			WebClient client = (WebClient)sender;

			if (Overlayer != null)
			{
				Overlayer.GetMessageQueue().Remove(client);
			}

			var destName = ValidateAndGetFileName(client);
			if (destName == null)
			{
				return;
			}
			var destFile = Path.Combine(Path.GetDirectoryName(DownloadPath), destName);

			File.Move(DownloadPath, destFile);

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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using EasyHook;
using System.Runtime.InteropServices;
using System.Net;
using System.Diagnostics;
using System.IO;
using System.ServiceModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;


namespace OsuDownloader.Injectee
{
////////////////////////////////////////////////////////////////////////////////////////////////////
/// <summary>
/// Hooker which change beatmap background and video to black and empty storyboard by CreateFile.
/// </summary>
////////////////////////////////////////////////////////////////////////////////////////////////////
class FileNameHooker : IHookerBase, IDisposable
{
	public bool IsHooking
	{
		get
		{
			return CreateFileHook != null;
		}
	}

	/// <summary>   The alternative background image path for boss mode. </summary>
	static string AlternativeImage;
	/// <summary>   The alternative storyboard file path for boss mode. </summary>
	static string AlternativeStoryboard;
	/// <summary>   The alternative black screen video path for boss mode. </summary>
	static string AlternativeVideo;

	/// <summary>   Filter of reserved names to use as skin. </summary>
	static Regex SkinNames;

	/// <summary>   true if disposed. </summary>
	private bool Disposed;

	/// <summary>   CreateFile function hook for boss mode. </summary>
	LocalHook CreateFileHook;

	static FileNameHooker()
	{
		var sb = new StringBuilder();
		sb.Append(@"^(");
		sb.Append(@"approachcircle|button-|comboburst|count\d|cursor|default-\d|followpoint|");
		sb.Append(@"fruit-|go|hit|inputoverlay|mania-|particle|pause-|pippidon|ranking-|");
		sb.Append(@"ready|reversearrow|score-|scorebar-|section-|slider|spinner-|star|taiko-|");
		sb.Append(@"taikobigcircle|taikohitciecle");
		sb.Append(@").*$");

		SkinNames = new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
	}

	private static void AssignAlternativeImage()
	{
		if (AlternativeImage != null)
			return;

		#region Bitmap creation

		DrawingVisual drawingvisual = new DrawingVisual();
		using(DrawingContext context = drawingvisual.RenderOpen())
		{
			var bdOpt = Properties.Settings.Default.BloodcatOption;
			context.DrawRectangle(new SolidColorBrush(bdOpt.BackgroundColor),
								  null, new System.Windows.Rect(0, 0, 2, 2));
			context.Close();
		}

		RenderTargetBitmap result = new RenderTargetBitmap(2, 2, 96, 96, PixelFormats.Pbgra32);
		result.Render(drawingvisual);

		var encoder = new PngBitmapEncoder();
		encoder.Frames.Add(BitmapFrame.Create(result));

		#endregion

		AlternativeImage = Path.GetTempFileName();
		using(var tempImage = File.OpenWrite(AlternativeImage))
		{
			encoder.Save(tempImage);
		}
	}

	/// <summary>   Prepare alternative background, video, storyboard. </summary>
	private void PrepareAlternatives()
	{
		AssignAlternativeImage();

		if (AlternativeStoryboard == null)
		{
			AlternativeStoryboard = Path.GetTempFileName();
			File.AppendAllText(AlternativeStoryboard, "[Events]");
		}

		if (AlternativeVideo == null)
		{
			AlternativeVideo = Path.GetTempFileName();
			File.WriteAllBytes(AlternativeVideo, Properties.Resources.Black2x2);
		}
	}

	public void SetHookState(bool request)
	{
		if (request)
		{
			if (CreateFileHook != null)
				SetHookState(false);

			PrepareAlternatives();

			CreateFileHook = LocalHook.Create(
								 LocalHook.GetProcAddress("kernel32.dll", "CreateFileW"),
								 new DCreateFile(CreateFile_Hooked),
								 this);

			ResetHookAcl(HookManager.HookingThreadIds.ToArray());
		}
		else if (CreateFileHook != null)
		{
			CreateFileHook.Dispose();
			CreateFileHook = null;
		}
	}

	public void ResetHookAcl(int[] hookThreadIds)
	{
		if (CreateFileHook != null)
		{
			// BG reading is not thread specific. Newly created thread also included.
			CreateFileHook.ThreadACL.SetExclusiveACL(hookThreadIds);
		}
	}

	#region IDisposable and finalizer

	void Dispose(bool disposing)
	{
		if (Disposed)
			return;

		Disposed = true;

		SetHookState(false);

		// Files must be deleted.
		if (AlternativeImage != null)
		{
			File.Delete(AlternativeImage);
			AlternativeImage = null;
		}
		if (AlternativeStoryboard != null)
		{
			File.Delete(AlternativeStoryboard);
			AlternativeStoryboard = null;
		}
		if (AlternativeVideo != null)
		{
			File.Delete(AlternativeVideo);
			AlternativeVideo = null;
		}
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	~FileNameHooker()
	{
		Dispose(false);
	}

	#endregion

	#region CreateFile pinvoke

	[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
	delegate IntPtr DCreateFile(
		[MarshalAs(UnmanagedType.LPWStr)] string filename,
		[MarshalAs(UnmanagedType.U4)] FileAccess access,
		[MarshalAs(UnmanagedType.U4)] FileShare share,
		IntPtr securityAttributes, // optional SECURITY_ATTRIBUTES struct or IntPtr.Zero
		[MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
		[MarshalAs(UnmanagedType.U4)] FileAttributes flagsAndAttributes,
		IntPtr templateFile);

	// just use a P-Invoke implementation to get native API access from C# (this step is not necessary for C++.NET)
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, CallingConvention = CallingConvention.StdCall)]
	public static extern IntPtr CreateFile(
		[MarshalAs(UnmanagedType.LPWStr)] string filename,
		[MarshalAs(UnmanagedType.U4)] FileAccess access,
		[MarshalAs(UnmanagedType.U4)] FileShare share,
		IntPtr securityAttributes, // optional SECURITY_ATTRIBUTES struct or IntPtr.Zero
		[MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
		[MarshalAs(UnmanagedType.U4)] FileAttributes flagsAndAttributes,
		IntPtr templateFile);

	// this is where we are intercepting all file accesses!
	static IntPtr CreateFile_Hooked(
		[MarshalAs(UnmanagedType.LPWStr)] string filename,
		[MarshalAs(UnmanagedType.U4)] FileAccess access,
		[MarshalAs(UnmanagedType.U4)] FileShare share,
		IntPtr securityAttributes, // optional SECURITY_ATTRIBUTES struct or IntPtr.Zero
		[MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
		[MarshalAs(UnmanagedType.U4)] FileAttributes flagsAndAttributes,
		IntPtr templateFile)
	{
		try
		{
			filename = filename.ToLower();
			// Frequency order.
			if (!filename.EndsWith(".exe") &&
				filename.IndexOf("\\osu!\\data\\") == -1 &&
				!filename.EndsWith(".osu") &&
				!filename.EndsWith(".mp3") &&
				!filename.EndsWith(".wav"))
			{
				if (filename.EndsWith(".osb"))
				{
					filename = AlternativeStoryboard;
				}
				else if (filename.EndsWith(".avi") ||
						 filename.EndsWith(".mkv") ||
						 filename.EndsWith(".mp4") ||
						 filename.EndsWith(".flv"))
				{
					filename = AlternativeVideo;
				}
				else if (SkinNames.IsMatch(Path.GetFileName(filename)) == false)
				{
					filename = AlternativeImage;
				}
			}
		}
		catch (Exception e)
		{
			MainWindowViewModel.LogException(e);
		}

		// call original API...
		return CreateFile(filename, access, share, securityAttributes,
						  creationDisposition, flagsAndAttributes, templateFile);
	}

	#endregion

}
}

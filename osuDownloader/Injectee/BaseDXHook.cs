using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using EasyHook;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Threading;

namespace Capture.Hook
{
internal interface IDXHook
{
	void Hook();

	void Cleanup();
}

internal abstract class BaseDXHook: IDXHook
{
	~BaseDXHook()
	{
		Dispose(false);
	}

	protected IntPtr[] GetVTblAddresses(IntPtr pointer, int numberOfMethods)
	{
		return GetVTblAddresses(pointer, 0, numberOfMethods);
	}

	protected IntPtr[] GetVTblAddresses(IntPtr pointer, int startIndex, int numberOfMethods)
	{
		List<IntPtr> vtblAddresses = new List<IntPtr>();

		IntPtr vTable = Marshal.ReadIntPtr(pointer);
		for (int i = startIndex; i < startIndex + numberOfMethods; i++)
			// using IntPtr.Size allows us to support both 32 and 64-bit processes
			vtblAddresses.Add(Marshal.ReadIntPtr(vTable, i * IntPtr.Size));

		return vtblAddresses.ToArray();
	}

	protected static void CopyStream(Stream input, Stream output)
	{
		int bufferSize = 32768;
		byte[] buffer = new byte[bufferSize];
		while (true)
		{
			int read = input.Read(buffer, 0, buffer.Length);
			if (read <= 0)
			{
				return;
			}
			output.Write(buffer, 0, read);
		}
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>
	/// Reads data from a stream until the end is reached. The data is returned as a byte array. An
	/// IOException is thrown if any of the underlying IO calls fail.
	/// </summary>
	///
	/// <param name="stream">   The stream to read data from. </param>
	///
	/// <returns>   An array of byte. </returns>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	protected static byte[] ReadFullStream(Stream stream)
	{
		if (stream is MemoryStream)
		{
			return ((MemoryStream)stream).ToArray();
		}
		else
		{
			byte[] buffer = new byte[32768];
			using(MemoryStream ms = new MemoryStream())
			{
				while (true)
				{
					int read = stream.Read(buffer, 0, buffer.Length);
					if (read > 0)
						ms.Write(buffer, 0, read);
					if (read < buffer.Length)
					{
						return ms.ToArray();
					}
				}
			}
		}
	}

	private ImageCodecInfo GetEncoder(ImageFormat format)
	{

		ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();

		foreach (ImageCodecInfo codec in codecs)
		{
			if (codec.FormatID == format.Guid)
			{
				return codec;
			}
		}
		return null;
	}

	private Bitmap BitmapFromBytes(byte[] bitmapData)
	{
		using(MemoryStream ms = new MemoryStream(bitmapData))
		{
			return (Bitmap)Image.FromStream(ms);
		}
	}
	#region IDXHook Members

	protected List<LocalHook> Hooks = new List<LocalHook>();
	public abstract void Hook();

	public abstract void Cleanup();

	#endregion

	#region IDispose Implementation

	public void Dispose()
	{
		Dispose(true);
	}

	protected virtual void Dispose(bool disposing)
	{
		// Only clean up managed objects if disposing (i.e. not called from destructor)
		if (disposing)
		{
			try
			{
				// Uninstall Hooks
				if (Hooks.Count > 0)
				{
					// First disable the hook (by excluding all threads) and wait long enough to ensure that all hooks are not active
					foreach (var hook in Hooks)
					{
						// Lets ensure that no threads will be intercepted again
						hook.ThreadACL.SetInclusiveACL(new int[] { 0 });
					}

					System.Threading.Thread.Sleep(100);

					// Now we can dispose of the hooks (which triggers the removal of the hook)
					foreach (var hook in Hooks)
					{
						hook.Dispose();
					}

					Hooks.Clear();
				}
			}
			catch
			{
			}
		}
	}

	#endregion
}
}

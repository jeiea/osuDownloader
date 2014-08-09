using EasyHook;
using SharpDX;
using SharpDX.Direct3D9;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace OsuDownloader.Injectee
{

internal class D3D9Hooker: BaseDXHook, IOverlayer, IHookerBase
{
	public Dictionary<object, EntryBase> MessageQueue = new Dictionary<object, EntryBase>();

	Font MainFont;
	Line BackLine;
	int ScreenWidth = 1024;
	int ScreenHeight = 768;
	ColorBGRA background = new ColorBGRA(255, 255, 255, 192);
	ColorBGRA foreground = new ColorBGRA(128, 255, 128, 192);

	//LocalHook Direct3DDevice_EndSceneHook    = null;
	LocalHook Direct3DDevice_ResetHook         = null;
	LocalHook Direct3DDevice_PresentHook       = null;
	LocalHook Direct3DDeviceEx_PresentExHook   = null;

	List<IntPtr> id3dDeviceFunctionAddresses     = new List<IntPtr>();
	//List<IntPtr> id3dDeviceExFunctionAddresses = new List<IntPtr>();
	const int D3D9_DEVICE_METHOD_COUNT           = 119;
	const int D3D9Ex_DEVICE_METHOD_COUNT         = 15;
	bool _supportsDirect3D9Ex                    = false;

	public bool IsHooking
	{
		get
		{
			return Direct3DDevice_ResetHook != null;
		}
	}

	public void SetHookState(bool request)
	{
		try
		{
			if (request)
			{
				Hook();
			}
			else
			{
				Dispose();
				Direct3DDevice_PresentHook = null;
				Direct3DDevice_ResetHook = null;
				Direct3DDeviceEx_PresentExHook = null;
			}
		}
		catch (Exception e)
		{
			MainWindowViewModel.LogException(e);
		}
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Add message. It also enables hook if it was disabled. </summary>
	///
	/// <param name="key">      The key. </param>
	/// <param name="entry">    The entry. </param>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	public void AddMessage(object key, EntryBase entry)
	{
		if (IsHooking == false)
		{
			// If remove it, progress event of first download request won't firing.
			ThreadPool.QueueUserWorkItem(state =>
			{
				if (IsHooking == false)
				{
					SetHookState(true);
				}
			});
		}
		MessageQueue.Add(key, entry);
	}

	public Dictionary<object, EntryBase> GetMessageQueue()
	{
		return MessageQueue;
	}

	public override void Hook()
	{
		// First we need to determine the function address for IDirect3DDevice9
		Device device;
		id3dDeviceFunctionAddresses = new List<IntPtr>();
		//id3dDeviceExFunctionAddresses = new List<IntPtr>();
		using(Direct3D d3d = new Direct3D())
		{
			using(var renderForm = new System.Windows.Forms.Form())
			{
				using(device = new Device(d3d, 0, DeviceType.NullReference, IntPtr.Zero, CreateFlags.HardwareVertexProcessing, new PresentParameters() { BackBufferWidth = 1, BackBufferHeight = 1, DeviceWindowHandle = renderForm.Handle }))
				{
					id3dDeviceFunctionAddresses.AddRange(GetVTblAddresses(device.NativePointer, D3D9_DEVICE_METHOD_COUNT));
				}
			}
		}

		try
		{
			using(Direct3DEx d3dEx = new Direct3DEx())
			{
				using(var renderForm = new System.Windows.Forms.Form())
				{
					using(var deviceEx = new DeviceEx(
						d3dEx, 0, DeviceType.NullReference, IntPtr.Zero, CreateFlags.HardwareVertexProcessing,
						new PresentParameters()
					{
						BackBufferWidth = 1, BackBufferHeight = 1, DeviceWindowHandle = renderForm.Handle
					}, new DisplayModeEx() { Width = 800, Height = 600 }))
					{
						id3dDeviceFunctionAddresses.AddRange(
							GetVTblAddresses(deviceEx.NativePointer, D3D9_DEVICE_METHOD_COUNT, D3D9Ex_DEVICE_METHOD_COUNT));
						_supportsDirect3D9Ex = true;
					}
				}
			}
		}
		catch (Exception)
		{
			_supportsDirect3D9Ex = false;
		}

		// We want to hook each method of the IDirect3DDevice9 interface that we are interested in

		// 42 - EndScene (we will retrieve the back buffer here)
		/// It's not used.
		//Direct3DDevice_EndSceneHook = LocalHook.Create(
		//                                  id3dDeviceFunctionAddresses[(int)Direct3DDevice9FunctionOrdinals.EndScene],
		//                                  // On Windows 7 64-bit w/ 32-bit app and d3d9 dll version 6.1.7600.16385, the address is equiv to:
		//                                  // (IntPtr)(GetModuleHandle("d3d9").ToInt32() + 0x1ce09),
		//                                  // A 64-bit app would use 0xff18
		//                                  // Note: GetD3D9DeviceFunctionAddress will output these addresses to a log file
		//                                  new Direct3D9Device_EndSceneDelegate(EndSceneHook),
		//                                  this);

		unsafe
		{
			// If Direct3D9Ex is available - hook the PresentEx
			if (_supportsDirect3D9Ex)
			{
				Direct3DDeviceEx_PresentExHook = LocalHook.Create(
					id3dDeviceFunctionAddresses[(int)Direct3DDevice9ExFunctionOrdinals.PresentEx],
					new Direct3D9DeviceEx_PresentExDelegate(PresentExHook),
					this);
			}

			// Always hook Present also (device will only call Present or PresentEx not both)
			Direct3DDevice_PresentHook = LocalHook.Create(
				id3dDeviceFunctionAddresses[(int)Direct3DDevice9FunctionOrdinals.Present],
				new Direct3D9Device_PresentDelegate(PresentHook),
				this);
		}

		// 16 - Reset (called on resolution change or windowed/fullscreen change - we will reset some things as well)
		Direct3DDevice_ResetHook = LocalHook.Create(
									   id3dDeviceFunctionAddresses[(int)Direct3DDevice9FunctionOrdinals.Reset],
									   // On Windows 7 64-bit w/ 32-bit app and d3d9 dll version 6.1.7600.16385,
									   // the address is equiv to: (IntPtr)(GetModuleHandle("d3d9").ToInt32() + 0x58dda),
									   // A 64-bit app would use 0x3b3a0
									   // Note: GetD3D9DeviceFunctionAddress will output these addresses to a log file
									   new Direct3D9Device_ResetDelegate(ResetHook),
									   this);

		/*
		 * Don't forget that all hooks will start deactivated...
		 * The following ensures that all threads are intercepted:
		 * Note: you must do this for each hook.
		 */
		//Direct3DDevice_EndSceneHook.ThreadACL.SetExclusiveACL(new Int32[1]);
		//Direct3DDevice_EndSceneHook.ThreadACL.SetInclusiveACL(new int[] {});
		//Hooks.Add(Direct3DDevice_EndSceneHook);

		Direct3DDevice_PresentHook.ThreadACL.SetExclusiveACL(new Int32[1]);
		Hooks.Add(Direct3DDevice_PresentHook);

		if (_supportsDirect3D9Ex)
		{
			Direct3DDeviceEx_PresentExHook.ThreadACL.SetExclusiveACL(new Int32[1]);
			Hooks.Add(Direct3DDeviceEx_PresentExHook);
		}

		Direct3DDevice_ResetHook.ThreadACL.SetExclusiveACL(new Int32[1]);
		Hooks.Add(Direct3DDevice_ResetHook);
	}

	/// <summary>   Just ensures that the surface we created is cleaned up. </summary>
	public override void Cleanup()
	{
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
	}

	[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
	delegate int Direct3D9Device_EndSceneDelegate(IntPtr device);

	[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
	delegate int Direct3D9Device_ResetDelegate(IntPtr device, ref PresentParameters presentParameters);

	[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
	unsafe delegate int Direct3D9Device_PresentDelegate(IntPtr devicePtr, SharpDX.Rectangle* pSourceRect, SharpDX.Rectangle* pDestRect, IntPtr hDestWindowOverride, IntPtr pDirtyRegion);

	[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
	unsafe delegate int Direct3D9DeviceEx_PresentExDelegate(IntPtr devicePtr, SharpDX.Rectangle* pSourceRect, SharpDX.Rectangle* pDestRect, IntPtr hDestWindowOverride, IntPtr pDirtyRegion, Present dwFlags);

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>
	/// Reset the _renderTarget so that we are sure it will have the correct presentation parameters
	/// (required to support working across changes to windowed/fullscreen or resolution changes)
	/// </summary>
	///
	/// <param name="devicePtr">            . </param>
	/// <param name="presentParameters">    [in,out]. </param>
	///
	/// <returns>   An int. </returns>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	int ResetHook(IntPtr devicePtr, ref PresentParameters presentParameters)
	{
		Device device = (Device)devicePtr;
		try
		{
			ScreenWidth = presentParameters.BackBufferWidth;
			ScreenHeight = presentParameters.BackBufferHeight;

			// EasyHook has already repatched the original Reset so calling it here will
			// not cause an endless recursion to this function
			device.Reset(presentParameters);
			return SharpDX.Result.Ok.Code;
		}
		catch (SharpDX.SharpDXException sde)
		{
			return sde.ResultCode.Code;
		}
		catch (Exception e)
		{
			return SharpDX.Result.Ok.Code;
		}
	}

	bool _isUsingPresent = false;

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Used in the overlay. </summary>
	///
	/// <param name="devicePtr">            . </param>
	/// <param name="pSourceRect">          [in,out] If non-null, source rectangle. </param>
	/// <param name="pDestRect">            [in,out] If non-null, destination rectangle. </param>
	/// <param name="hDestWindowOverride">  Destination window override. </param>
	/// <param name="pDirtyRegion">         The dirty region. </param>
	/// <param name="dwFlags">              The flags. </param>
	///
	/// <returns>   An int. </returns>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	unsafe int PresentExHook(IntPtr devicePtr, SharpDX.Rectangle* pSourceRect, SharpDX.Rectangle* pDestRect,
							 IntPtr hDestWindowOverride, IntPtr pDirtyRegion, Present dwFlags)
	{
		_isUsingPresent = true;
		DeviceEx device = (DeviceEx)devicePtr;

		DoCaptureRenderTarget(device, "PresentEx");

		//    Region region = new Region(pDirtyRegion);
		if (pSourceRect == null || *pSourceRect == SharpDX.Rectangle.Empty)
			device.PresentEx(dwFlags);
		else
		{
			if (hDestWindowOverride != IntPtr.Zero)
				device.PresentEx(dwFlags, *pSourceRect, *pDestRect, hDestWindowOverride);
			else
				device.PresentEx(dwFlags, *pSourceRect, *pDestRect);
		}
		return SharpDX.Result.Ok.Code;
	}

	unsafe int PresentHook(IntPtr devicePtr, SharpDX.Rectangle* pSourceRect, SharpDX.Rectangle* pDestRect,
						   IntPtr hDestWindowOverride, IntPtr pDirtyRegion)
	{
		_isUsingPresent = true;

		Device device = (Device)devicePtr;

		DoCaptureRenderTarget(device, "PresentHook");

		if (pSourceRect == null || *pSourceRect == SharpDX.Rectangle.Empty)
			device.Present();
		else
		{
			if (hDestWindowOverride != IntPtr.Zero)
				device.Present(*pSourceRect, *pDestRect, hDestWindowOverride);
			else
				device.Present(*pSourceRect, *pDestRect);
		}
		return SharpDX.Result.Ok.Code;
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Hook for IDirect3DDevice9.EndScene. </summary>
	///
	/// <remarks>
	/// Remember that this is called many times a second by the Direct3D application - be mindful of
	/// memory and performance!
	/// </remarks>
	///
	/// <param name="devicePtr">    Pointer to the IDirect3DDevice9 instance. Note: object member
	///                             functions always pass "this" as the first parameter. </param>
	///
	/// <returns>   The HRESULT of the original EndScene. </returns>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	int EndSceneHook(IntPtr devicePtr)
	{
		Device device = (Device)devicePtr;

		if (!_isUsingPresent)
			DoCaptureRenderTarget(device, "EndSceneHook");

		device.EndScene();
		return SharpDX.Result.Ok.Code;
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>
	/// Implementation of capturing from the render target of the Direct3D9 Device (or DeviceEx)
	/// </summary>
	///
	/// <param name="device">   . </param>
	/// <param name="hook">     The hook. </param>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	void DoCaptureRenderTarget(Device device, string hook)
	{
		// Auto hook disable.
		if (MessageQueue.Count == 0)
		{
			SetHookState(false);
			return;
		}

		try
		{
			if (MainFont != null && MainFont.Device.NativePointer != device.NativePointer)
			{
				MainFont.Dispose();
				MainFont = null;
			}
			if (MainFont == null)
			{
				// Measure screen size after device creation.
				using(var renderTarget = device.GetRenderTarget(0))
				{
					ScreenWidth  = renderTarget.Description.Width;
					ScreenHeight = renderTarget.Description.Height;
				}

				MainFont = new Font(device, new FontDescription()
				{
					Height = (int)(Math.Min(ScreenWidth, ScreenHeight) * 0.03),
					FaceName = "맑은 고딕",
					CharacterSet = FontCharacterSet.Default,
					OutputPrecision = FontPrecision.ScreenOutline,
					Quality = FontQuality.ClearTypeNatural,
					PitchAndFamily = FontPitchAndFamily.Default,
					Weight = FontWeight.Regular
				});
			}
			if (BackLine != null && BackLine.Device.NativePointer != device.NativePointer)
			{
				BackLine.Dispose();
				BackLine = null;
			}
			if (BackLine == null)
			{
				BackLine = new Line(device) { Width = MainFont.Description.Height + 2 };
			}

			var fontColor  = new ColorBGRA(0, 0, 0, 255);

			int i = 0;
			var items = from pair in MessageQueue
						where pair.Value.Begin < DateTime.Now
						orderby pair.Value.Begin
						select pair;

			float screenCenter = ScreenWidth * 0.5F;
			float topMargin = ScreenHeight * 0.725F;
			float spaceBetweenLines = MainFont.Description.Height * 1.4F;
			int fontMarginX = (int)(MainFont.Description.Height * 0.2F);
			int fontCorrectionY = (int)(MainFont.Description.Height * 0.5F);

			BackLine.Begin();
			foreach (var pair in items.ToArray())
			{
				var entry = pair.Value;

				if (entry is ProgressEntry)
				{
					ProgressEntry item = (ProgressEntry)entry;
					Rectangle rect = MainFont.MeasureText(null, item.Message, FontDrawFlags.SingleLine);

					float lineLength = rect.Right + fontMarginX * 2;
					Vector2 lineStart = new Vector2()
					{
						X = screenCenter - lineLength * 0.5F,
						Y = i * spaceBetweenLines + topMargin
					};
					float midPoint = lineStart.X + lineLength * item.Downloaded / item.Total;
					Vector2[] line = new Vector2[]
					{
						lineStart,
						new Vector2(midPoint, lineStart.Y)
					};
					new Vector2(lineStart.X + lineLength, lineStart.Y);
					BackLine.Draw(line, foreground);

					// Reduce length by multiplying download rate.
					line[0] = line[1];
					line[1] = new Vector2(lineStart.X + lineLength, lineStart.Y);
					BackLine.Draw(line, background);

					MainFont.DrawText(null, item.Message,
									  (int)lineStart.X + fontMarginX,
									  (int)lineStart.Y - fontCorrectionY,
									  fontColor);
					i++;
				}
				else if (entry is NoticeEntry)
				{
					NoticeEntry item = (NoticeEntry)entry;
					Rectangle rect = MainFont.MeasureText(null, item.Message, FontDrawFlags.SingleLine);

					var endTime = item.Begin + item.Duration;
					var remainingTime = endTime - DateTime.Now;
					byte alpha;
					if (remainingTime.TotalMilliseconds < 0)
					{
						MessageQueue.Remove(pair.Key);
						alpha = 0;
					}
					else
					{
						alpha = (byte)Math.Min(remainingTime.TotalMilliseconds * 0.5, 255);
					}
					var noticeColor = new ColorBGRA(255, 255, 32, alpha);
					var noticeFontColor = new ColorBGRA(0, 0, 0, alpha);

					float lineLength = rect.Right + 2 * fontMarginX;
					Vector2 lineStart = new Vector2()
					{
						X = screenCenter - lineLength * 0.5F,
						Y = i * spaceBetweenLines + topMargin
					};
					BackLine.Draw(new Vector2[]
					{
						lineStart,
						new Vector2(lineStart.X + lineLength, lineStart.Y)
					}, noticeColor);

					MainFont.DrawText(null, item.Message,
									  (int)lineStart.X + fontMarginX,
									  (int)lineStart.Y - fontCorrectionY,
									  noticeFontColor);
					i++;
				}
			}
			BackLine.End();
		}
		catch (Exception e)
		{
			MainWindowViewModel.LogException(e);
		}
	}
}
}

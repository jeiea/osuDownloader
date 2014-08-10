using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Input;

namespace OsuDownloader.Injectee
{
// http://www.liensberger.it/web/blog/?p=207
public sealed class KeyboardHook : IDisposable
{

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool UnregisterHotKey(IntPtr hWnd, int id);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool PostMessage(HandleRef hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	const int WM_USER = 0x0400;
	const int WM_REGISTER_HOTKEY = WM_USER + 10;
	const int WM_UNREGISTER_HOTKEY = WM_USER + 11;

	public HandleWindow Window;

	/// <summary>   Represents the window that is used internally to get the messages. </summary>
	public class HandleWindow : NativeWindow, IDisposable
	{
		const int WM_HOTKEY = 0x0312;

		List<int> HotkeyIds = new List<int>();

		int CurrentId;

		public HandleWindow()
		{
			// create the handle for the window.
			this.CreateHandle(new CreateParams());
		}

		////////////////////////////////////////////////////////////////////////////////////////////////////
		/// <summary>   Overridden to get the notifications. </summary>
		///
		/// <param name="m">    [in,out]. </param>
		////////////////////////////////////////////////////////////////////////////////////////////////////
		protected override void WndProc(ref Message m)
		{
			base.WndProc(ref m);

			// check if we got a hot key pressed.
			switch (m.Msg)
			{
			case WM_HOTKEY:
				// get the keys.
				Keys key = (Keys)(((int)m.LParam >> 16) & 0xFFFF);
				ModifierKeys modifier = (ModifierKeys)((int)m.LParam & 0xFFFF);

				// invoke the event to notify the parent.
				if (KeyPressed != null)
					KeyPressed(this, new KeyPressedEventArgs(modifier, key));
				break;
			case WM_REGISTER_HOTKEY:
				if (RegisterHotKey(Handle, CurrentId, (uint)m.WParam, (uint)m.LParam))
				{
					HotkeyIds.Add(CurrentId++);
				}
				break;
			case WM_UNREGISTER_HOTKEY:
				foreach (var id in HotkeyIds)
				{
					UnregisterHotKey(Handle, id);
				}
				break;
			}
		}

		public event EventHandler<KeyPressedEventArgs> KeyPressed;

		#region IDisposable Members

		public void Dispose()
		{
			this.DestroyHandle();
		}

		#endregion
	}

	public KeyboardHook(bool createThread)
	{
		if (createThread)
		{
			var mre = new ManualResetEvent(false);

			new Thread(() =>
			{
				// Failed...
				// SynchronizationContext.SetSynchronizationContext(Context);

				AssignWindow();

				mre.Set();

				Application.Run();
			}).Start();

			mre.WaitOne();
		}
		else
		{
			AssignWindow();
		}
	}

	/// <summary>   register the event of the inner window. </summary>
	private void AssignWindow()
	{
		Window = new HandleWindow();

		Window.KeyPressed += delegate(object sender, KeyPressedEventArgs args)
		{
			if (KeyPressed != null)
				KeyPressed(this, args);
		};
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Registers a hot key in the system. </summary>
	///
	/// <exception cref="InvalidOperationException">    Thrown when the requested operation is
	///                                                 invalid. </exception>
	///
	/// <param name="modifier"> The modifiers that are associated with the hot key. </param>
	/// <param name="key">      The key itself that is associated with the hot key. </param>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	public void RegisterHotKey(ModifierKeys modifier, Keys key, bool noRepeat = false)
	{
		// register the hot key.
		PostMessage(new HandleRef(Window, Window.Handle), (uint)WM_REGISTER_HOTKEY,
					(IntPtr)((int)modifier | (noRepeat ? 0x4000 : 0)), (IntPtr)key);
	}

	/// <summary>   A hot key has been pressed. </summary>
	public event EventHandler<KeyPressedEventArgs> KeyPressed;

	#region IDisposable Members

	public void Dispose()
	{
		// unregister all the registered hot keys.
		SendMessage(Window.Handle, (uint)WM_UNREGISTER_HOTKEY, IntPtr.Zero, IntPtr.Zero);

		// dispose the inner native window.
		Window.Dispose();
	}

	#endregion
}

/// <summary>   Event Args for the event that is fired after the hot key has been pressed. </summary>
public class KeyPressedEventArgs : EventArgs
{
	internal KeyPressedEventArgs(ModifierKeys modifier, Keys key)
	{
		Modifier = modifier;
		Key = key;
	}

	public ModifierKeys Modifier
	{
		get;
		private set;
	}

	public Keys Key
	{
		get;
		private set;
	}
}
}

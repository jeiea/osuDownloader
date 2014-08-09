using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;

namespace OsuDownloader.Injectee
{
class WPFOverlayer : IOverlayer
{
	public Dictionary<object, EntryBase> MessageQueue = new Dictionary<object, EntryBase>();

	Window Overlay;

	public Dictionary<object, EntryBase> GetMessageQueue()
	{
		return MessageQueue;
	}

	public void AddMessage(object key, EntryBase entry)
	{
		MessageQueue.Add(key, entry);
		Overlay.Dispatcher.BeginInvoke(new Action(() =>
		{
			Overlay.Show();
		}));
	}

	public WPFOverlayer()
	{
		var mre = new ManualResetEvent(false);

		var uiThread = new Thread(() =>
		{
			var app = new Application();
			Overlay = new ProgressWindow(this);

			// For window preparation.
			mre.Set();

			// ShowDialog isn't enough. It can't serve BeginInvoke request.
			app.Run(Overlay);
		});
		uiThread.SetApartmentState(ApartmentState.STA);
		uiThread.Start();

		mre.WaitOne();
	}
}

internal class ProgressWindow : Window
{
	public IOverlayer Overlayer;

	StackPanel MainPanel;
	Style TextBlockStyle;
	DispatcherTimer Refresher;
	readonly SolidColorBrush NoticeBackground = Brushes.Yellow;

	bool IsDragMoved;

	Dictionary<object, TextBlock> Messages = new Dictionary<object, TextBlock>();
	Dictionary<object, ProgressBar> Progresses = new Dictionary<object, ProgressBar>();

	public ProgressWindow(IOverlayer overlayer) : base()
	{
		Overlayer = overlayer;

		IsVisibleChanged += ProgressWindow_IsVisibleChanged;

		AllowsTransparency = true;
		WindowStyle = WindowStyle.None;
		ShowInTaskbar = false;
		ShowActivated = false;
		SnapsToDevicePixels = true;
		Topmost = true;
		Background = Brushes.Transparent;

		MainPanel = new StackPanel();
		MainPanel.Orientation = Orientation.Vertical;
		AddChild(MainPanel);

		TextBlockStyle = new Style();

		foreach (var setter in new Setter[]
	{
		new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center),
			new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center),
			new Setter(TextBlock.ForegroundProperty, Brushes.Black),
			new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center),
			new Setter(TextBlock.FontWeightProperty, FontWeights.DemiBold),
			new Setter(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 2)),
		})
		{
			TextBlockStyle.Setters.Add(setter);
		}

		Hide();
	}

	void ProgressWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
	{
		if (IsVisible)
		{
			var queue = Overlayer.GetMessageQueue();
			if (IsDragMoved == false)
			{
				var area = OsuHelper.GetOsuWindowClient();
				if (area.IsEmpty)
				{
					Left = 0;
					Top = 0;
					Width = 400;
					Height = 150;
				}
				else
				{
					Left = area.X;
					Top = area.Y + area.Height * 0.725;
					Width = area.Width;
					Height = area.Height * 0.2;
					FontSize = Math.Min(area.Width, area.Height) * 0.025D;
				}
			}

			Refresher = new DispatcherTimer();
			Refresher.Interval = TimeSpan.FromMilliseconds(100);
			Refresher.IsEnabled = true;
			Refresher.Tick += Refresher_Tick;
			Refresher.Start();
		}
	}

	protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
	{
		DragMove();
		IsDragMoved = true;
		base.OnMouseLeftButtonDown(e);
	}

	void Refresher_Tick(object sender, EventArgs e)
	{
		var queue = Overlayer.GetMessageQueue();
		if (queue.Count == 0)
		{
			Refresher.Stop();
			Refresher = null;
			Hide();
		}

		// Remove timeout items from queue
		foreach (var item in queue.ToArray())
		{
			if (item.Value is NoticeEntry)
			{
				var entry = (NoticeEntry)item.Value;
				if (entry.Begin + entry.Duration < DateTime.Now)
				{
					queue.Remove(item.Key);
				}
			}
		}

		// Remove removed visual fro window
		foreach (var pair in Messages.ToArray())
		{
			if (queue.ContainsKey(pair.Key) == false)
			{
				MainPanel.Children.Remove(Messages[pair.Key]);
				Messages.Remove(pair.Key);
				Progresses.Remove(pair.Key);
			}
		}

		// TODO: caching state.
		foreach (var pair in queue)
		{
			var entry = pair.Value;

			// TextBlock preparation.
			TextBlock tb;
			if (Messages.ContainsKey(pair.Key))
			{
				tb = Messages[pair.Key];
			}
			else
			{
				tb = new TextBlock();
				tb.Style = TextBlockStyle;
				tb.FontSize = FontSize;
				Messages[pair.Key] = tb;
				MainPanel.Children.Add(tb);
			}
			tb.Text = entry.Message;

			// Background progressbar preparation.
			if (entry is ProgressEntry)
			{
				ProgressEntry item = (ProgressEntry)entry;

				ProgressBar pg;
				if (Progresses.ContainsKey(pair.Key))
				{
					pg = Progresses[pair.Key];
				}
				else
				{
					pg = new ProgressBar();
					pg.Minimum = 0;
					pg.Maximum = item.Total;
					pg.Margin = new Thickness(0, 2, 0, 2);
				}
				pg.Value = item.Downloaded;

				tb.Background = new VisualBrush(pg);
			}
			else if (entry is NoticeEntry)
			{
				if (Progresses.ContainsKey(pair.Key))
				{
					MainPanel.Children.Remove(Progresses[pair.Key]);
					Progresses.Remove(pair.Key);
				}

				tb.Background = NoticeBackground;
			}
		}
	}

	static object CloneControl(Control control)
	{
		string xamled = XamlWriter.Save(control);
		StringReader stringReader = new StringReader(xamled);
		XmlReader xmlReader = XmlReader.Create(stringReader);
		return XamlReader.Load(xmlReader);
	}
}
}

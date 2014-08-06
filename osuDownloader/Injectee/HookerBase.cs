using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OsuDownloader.Injectee
{
interface IHookerBase
{
	bool IsHooking { get; }
	void SetHookState(bool request);
}
}

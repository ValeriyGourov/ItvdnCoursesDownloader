using System;

namespace Downloader.Models
{
	public sealed class Lesson
	{
		internal Lesson()
		{
		}

		public int Number { get; internal set; }
		public string Title { get; internal set; }
		public Uri Uri { get; internal set; }
		public DownloadFile Video { get; internal set; }
	}
}

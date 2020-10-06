using System;
using System.Collections.Generic;

namespace Downloader.Models
{
	/// <summary>
	/// Определение списка видео, извлекаемое из конфигурации на странице курса.
	/// </summary>
	internal sealed class VideoListDefinition
	{
		private VideoListDefinition()
		{
		}

		public VideoRequest Request { get; set; }
	}

	internal sealed class VideoRequest
	{
		private VideoRequest()
		{
		}

		public VideoFile Files { get; set; }
	}

	internal sealed class VideoFile
	{
		private VideoFile()
		{
		}

		public IEnumerable<ProgressiveItem> Progressive { get; set; }
	}

	internal sealed class ProgressiveItem
	{
		private ProgressiveItem()
		{
		}

		public int Profile { get; set; }
		public Uri Url { get; set; }
		public string Quality { get; set; }
	}
}

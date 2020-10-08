namespace Downloader.Models
{
	/// <summary>
	/// Описание переменной settings в сценарии JavaScript на странице отдельного урока курса.
	/// </summary>
	internal sealed class LessonSettings
	{
		private LessonSettings()
		{
		}

		public string LessonUrl { get; set; }
		public string VideosetUrl { get; set; }
	}
}

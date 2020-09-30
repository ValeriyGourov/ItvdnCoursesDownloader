using System.ComponentModel.DataAnnotations;

using Downloader;

namespace ItvdnCoursesDownloaderConsole
{
	/// <summary>
	/// Настройки приложения.
	/// </summary>
	internal sealed class AppSettings
	{
		/// <summary>
		/// Полный адрес курса на сайте.
		/// </summary>
		[Url]
		public string CourseAddress { get; set; }

		/// <summary>
		/// Секция настроек движка загрузки файлов.
		/// </summary>
		[Required]
		public EngineSettings Engine { get; set; }
	}
}

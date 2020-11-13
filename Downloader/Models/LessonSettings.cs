namespace Downloader.Models
{
	/// <summary>
	/// Описание переменной settings в сценарии JavaScript на странице отдельного урока курса.
	/// </summary>
	internal sealed record LessonSettings(string LessonUrl, string VideosetUrl);
}

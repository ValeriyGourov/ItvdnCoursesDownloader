namespace Downloader.Models.Responses;

/// <summary>
/// Описывает ответ сервера при запросе идентификатора видеофайла.
/// </summary>
internal sealed class VideoIdResponse
{
	/// <summary>
	/// Значение статуса, определяющего успешно выполнение операции.
	/// </summary>
	private const string _videoIdStatusOk = "OK";

	/// <summary>
	/// Статус выполнения операции.
	/// </summary>
	public string Status { get; set; }

	/// <summary>
	/// Идентификатор видеофайла.
	/// </summary>
	public int Id { get; set; }

	/// <summary>
	/// Признак того, что запрос выполнен успешно.
	/// </summary>
	public bool IsStatusSuccess => Status.Equals(_videoIdStatusOk, System.StringComparison.OrdinalIgnoreCase);
}

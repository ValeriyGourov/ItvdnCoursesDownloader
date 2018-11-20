namespace Downloader.Models.Responses
{
    /// <summary>
    /// Описывает ответ сервера при запросе идентификатора видеофайла.
    /// </summary>
    internal sealed class VideoIdResponse
    {
        /// <summary>
        /// Значение статуса, определяющего успешно выполнение операции.
        /// </summary>
        private const string videoIdStatusOk = "OK";

        /// <summary>
        /// Статус выполнения операции.
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Идентификатор видеофайла.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Признак того, что запрос выполнен успешно.
        /// </summary>
        public bool IsStatusSuccess => Status.Equals(videoIdStatusOk, System.StringComparison.OrdinalIgnoreCase);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

using Downloader.Infrastructure;
using Downloader.Models;
using Downloader.Models.Responses;

using Jint.Native;
using Jint.Parser;
using Jint.Parser.Ast;

namespace Downloader
{
	/// <summary>
	/// Механизм извлечения данных о конкретном курсе из веб-страницы этого курса.
	/// </summary>
	internal sealed class CourseDataExtractor
	{
		/// <summary>
		/// Полный адрес курса.
		/// </summary>
		private readonly Uri _courseUri;

		/// <summary>
		/// Настройки движка загрузки данных.
		/// </summary>
		private readonly EngineSettings _settings;

		/// <summary>
		/// Контейнер для хранения куки сайта курсов между запросами.
		/// </summary>
		private readonly CookieContainer _cookies;

		/// <summary>
		/// Маркер отмены выполняемой операции.
		/// </summary>
		private readonly CancellationToken _cancellationToken;

		/// <summary>
		/// Массив, содержащий символы, которые не разрешены в именах файлов.
		/// </summary>
		private static readonly char[] _invalidFileNameChars = Path.GetInvalidFileNameChars();

		/// <summary>
		/// Параметры сериализатора JSON, используемые при извлечении из JavaScript веб-страницы конфигурации загрузки видео файлов.
		/// </summary>
		private static readonly JsonSerializerOptions _videoListDefinitionJsonSerializerOptions = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };

		/// <summary>
		/// Основной конструктор.
		/// </summary>
		/// <param name="courseUri">Полный адрес курса.</param>
		/// <param name="settings">Настройки движка загрузки данных.</param>
		/// <param name="cookies">Контейнер для хранения куки сайта курсов между запросами.</param>
		/// <param name="cancellationToken">Маркер отмены выполняемой операции.</param>
		public CourseDataExtractor(
			Uri courseUri,
			EngineSettings settings,
			CookieContainer cookies,
			CancellationToken cancellationToken)
		{
			_courseUri = courseUri ?? throw new ArgumentNullException(nameof(courseUri));
			_settings = settings ?? throw new ArgumentNullException(nameof(settings));
			_cookies = cookies ?? throw new ArgumentNullException(nameof(cookies));
			_cancellationToken = cancellationToken;
		}

		/// <summary>
		/// Извлекает данные о курсе.
		/// </summary>
		/// <returns>Объект с данными для загрузки файлов курса.</returns>
		public async Task<Course> ExtractAsync()
		{
			IHtmlDocument document = await GetDocumentAsync(_courseUri);
			if (document == null)
			{
				return null;
			}

			Task<Uri> materialsUriTask = GetMaterialsUri(document);

			string title = GetCourseTitle(document);
			List<Lesson> lessons = GetLessons(document);

			IEnumerable<Task> lessonTasks = null;
			if (lessons?.Count > 0)
			{
				lessonTasks = lessons.Select(lesson => SetLessonVideoAsync(lesson));
			}

			Uri materialsUri = await materialsUriTask;
			if (lessonTasks != null)
			{
				await Task.WhenAll(lessonTasks);
			}

			return new Course
			{
				Title = title,
				FileSafeTitle = GetSafeFileName(title),
				Lessons = lessons,
				Materials = materialsUri == null
					? null
					: new DownloadFile(materialsUri) { Title = Course.MaterialsTitle }
			};
		}

		/// <summary>
		/// Загружает HTML-документ по указанному адресу.
		/// </summary>
		/// <param name="uri">Адрес загружаемого документа.</param>
		/// <param name="referrer">Значение заголовка Referer для HTTP-запроса.</param>
		/// <returns></returns>
		private async Task<IHtmlDocument> GetDocumentAsync(Uri uri, Uri referrer = null)
		{
			string html = null;

			using HttpClientHandler httpClientHandler = CreateHttpClientHandler();
			httpClientHandler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

			using HttpClient httpClient = CreateHttpClient(httpClientHandler);
			using HttpRequestMessage httpRequest = new HttpRequestMessage()
			{
				RequestUri = uri,
				Method = HttpMethod.Get,
			};
			if (referrer != null)
			{
				httpRequest.Headers.Referrer = referrer;
			}
			httpRequest.Headers.UserAgent.Add(new ProductInfoHeaderValue("Client", "1"));
			httpRequest.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
			httpRequest.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));

			using HttpResponseMessage response = await httpClient.SendAsync(httpRequest, _cancellationToken);
			//response.EnsureSuccessStatusCode();
			if (response.IsSuccessStatusCode)
			{
				html = await response.Content.ReadAsStringAsync();
			}
			else
			{
				// TODO: Зафиксировать ошибку.
			}

			return html == null
				? null
				: await new HtmlParser().ParseDocumentAsync(html, _cancellationToken);
		}

		/// <summary>
		/// Извлекает из HTML-документа страницы курса ссылку на файл с дополнительными материалами курса.
		/// </summary>
		/// <param name="document">HTML-документ страницы курса.</param>
		/// <returns>Ссылку на файл с дополнительными материалами курса.</returns>
		/// <exception cref="PageParseException">Ошибка разбора документа.</exception>
		private async Task<Uri> GetMaterialsUri(IHtmlDocument document)
		{
			const string className = "materials-buttons-wrapper";
			IElement materialsButtonsWrapperTag = document
				.GetElementsByClassName(className)
				.FirstOrDefault();
			if (materialsButtonsWrapperTag == null)
			{
				throw new PageParseException("Изменилась структура web-страницы. Невозможно определить блок с ссылкой для скачивания материалов курса.", className);
			}

			string linkToMaterials = materialsButtonsWrapperTag
				.GetElementsByClassName("btn-filled-green btn-get-sertificate get-materials")
				.FirstOrDefault()?
				.GetAttribute("data-link");

			string materialsUrl = null;
			if (Uri.IsWellFormedUriString(linkToMaterials, UriKind.Absolute))
			{
				Uri requestUri = new Uri(_settings.BaseAddress, "Video/GetLinkToMaterials");

				using HttpClientHandler httpClientHandler = CreateHttpClientHandler();
				using HttpClient httpClient = CreateHttpClient(httpClientHandler);

				var content = new
				{
					linkToMaterials
				};

				using HttpResponseMessage response = await PostAsJsonAsync(httpClient, requestUri, content);
				//response.EnsureSuccessStatusCode();
				if (response.IsSuccessStatusCode)
				{
					string jsonString = await response.Content.ReadAsStringAsync();
					materialsUrl = JsonSerializer.Deserialize<string>(jsonString);
				}
				else
				{
					// TODO: Зафиксировать ошибку.
				}
			}

			return materialsUrl == null
				? null
				: new Uri(materialsUrl);
		}

		/// <summary>
		/// Извлекает из HTML-документа страницы курса название курса.
		/// </summary>
		/// <param name="document">HTML-документ страницы курса.</param>
		/// <returns>Название курса.</returns>
		private static string GetCourseTitle(IHtmlDocument document) => document
			.GetElementsByTagName("h1")
			.Where(item => item.GetAttribute("itemprop") == "name")
			.FirstOrDefault()?
			.TextContent;

		/// <summary>
		/// Извлекает из HTML-документа страницы курса данные об уроках курса.
		/// </summary>
		/// <param name="document">HTML-документ страницы курса.</param>
		/// <returns>Коллекция объектов, содержащих данные о уроках курса.</returns>
		private List<Lesson> GetLessons(IHtmlDocument document)
		{
			IEnumerable<Lesson> query =
				from videoLessonItem in document.GetElementsByClassName("video-lesson-item")
				let childrenTags = videoLessonItem.Children
				let linkTag = childrenTags
					.Where(tag => tag.TagName.Equals("a", StringComparison.OrdinalIgnoreCase))
					.FirstOrDefault()
				let idTag = childrenTags
					.Where(tag => tag.HasAttribute("data-lesson-id"))
					.FirstOrDefault()
				select new Lesson
				{
					Uri = new Uri(_courseUri, linkTag?.GetAttribute("href")),
					Number = Convert.ToInt32(linkTag?
						.GetElementsByClassName("lesson-number")
						.FirstOrDefault()?
						.TextContent, CultureInfo.InvariantCulture),
					Title = linkTag?
						.GetElementsByClassName("lsn-name-wrapper")
						.FirstOrDefault()?
						.TextContent,
					Id = idTag?.GetAttribute("data-lesson-id")
				};
			return query.ToList();
		}

		/// <summary>
		/// Получает ссылку для скачивания файл видео указанного урока курса.
		/// </summary>
		/// <param name="lesson">Данные урока курса.</param>
		private async Task SetLessonVideoAsync(Lesson lesson)
		{
			IHtmlDocument lessonDocument = await GetDocumentAsync(lesson.Uri, lesson.Uri);
			if (lessonDocument == null)
			{
				return;
			}

			Uri requestUri = new Uri(_settings.BaseAddress, "Video/GetVideoId");
			var content = new
			{
				lessonId = lesson.Id
			};

			using HttpClientHandler httpClientHandler = CreateHttpClientHandler();
			using HttpClient httpClient = CreateHttpClient(httpClientHandler);
			using HttpResponseMessage response = await PostAsJsonAsync(httpClient, requestUri, content);
			//response.EnsureSuccessStatusCode();

			VideoIdResponse videoIdResponse;
			if (response.IsSuccessStatusCode)
			{
				string responseContent = await response.Content.ReadAsStringAsync();
				videoIdResponse = JsonSerializer.Deserialize<VideoIdResponse>(responseContent);
			}
			else
			{
				// TODO: Зафиксировать ошибку.
				return;
			}

			if (!videoIdResponse.IsStatusSuccess)
			{
				// TODO: Зафиксировать ошибку.
				return;
			}

			const string appId = "122963";
			string frameUrl = $"https://player.vimeo.com/video/{videoIdResponse.Id}?app_id={appId}";
			IHtmlDocument frame = await GetDocumentAsync(new Uri(frameUrl), lesson.Uri);
			string javaScript = GetJavaScriptFromFrame(frame);

			Uri videoUri = GetVideoUri(javaScript);
			lesson.Video = new DownloadFile(videoUri)
			{
				Title = $"{lesson.Number}. {GetSafeFileName(lesson.Title)}"
			};
		}

		/// <summary>
		/// Извлекает из сценария вложенного содержимого страницы урока адрес для загрузки файла видео урока.
		/// </summary>
		/// <param name="javaScript">Сценарий вложенного содержимого страницы урока.</param>
		/// <returns>Адрес для загрузки файла видео урока.</returns>
		private static Uri GetVideoUri(string javaScript)
		{
			if (string.IsNullOrWhiteSpace(javaScript))
			{
				return null;
			}

			JavaScriptParser javaScriptParser = new JavaScriptParser();
			Program program = javaScriptParser.Parse(javaScript);

			Expression init =
				(from programBody in program.Body
				 where program.Body.Count == 1
				 let body = programBody as ExpressionStatement
				 let expression = body?.Expression as CallExpression
				 let callee = expression?.Callee as FunctionExpression
				 from variableDeclaration in callee.VariableDeclarations
				 from declaration in variableDeclaration.Declarations
				 where declaration.Id.Name.Equals("config", StringComparison.OrdinalIgnoreCase)
				 select declaration.Init)
				 .FirstOrDefault();

			if (init == null)
			{
				// TODO; Зафиксировать ошибку.
				return null;
			}

			Jint.Engine jintEngine = new Jint.Engine();
			JsValue initObject = jintEngine.EvaluateExpression(init) as JsValue;

			Jint.Native.Json.JsonSerializer jsonSerializer = new Jint.Native.Json.JsonSerializer(jintEngine);
			string configJson = jsonSerializer
				.Serialize(initObject, new JsValue(""), new JsValue(" "))
				.AsString();

			VideoListDefinition videoList = JsonSerializer.Deserialize<VideoListDefinition>(
				configJson,
				_videoListDefinitionJsonSerializerOptions);
			return videoList.Request.Files.Progressive
				.OrderByDescending(item => item.Quality)
				.FirstOrDefault()?
				.Url;
		}

		/// <summary>
		/// Выполняет POST-запрос с сериализованными в JSON данными.
		/// </summary>
		/// <param name="httpClient">Клиент для отправки HTTP-запросов.</param>
		/// <param name="requestUri">Адрес запроса.</param>
		/// <param name="content">Содержимое запроса, которое будет преобразовано в JSON.</param>
		/// <returns>Ответное сообщение запроса.</returns>
		private async Task<HttpResponseMessage> PostAsJsonAsync(HttpClient httpClient, Uri requestUri, object content)
		{
			string jsonContent = JsonSerializer.Serialize(content);
			using HttpContent httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

			// Здесь мы должны явно получить ответ, пока httpContent не вышел за пределы блока using.
			HttpResponseMessage responseMessage = await httpClient.PostAsync(requestUri, httpContent, _cancellationToken);
			return responseMessage;
		}

		/// <summary>
		/// Создаёт обработчик сообщений для <see cref="HttpClient"/> с требуемыми параметрами.
		/// </summary>
		/// <returns>Обработчик сообщений для <see cref="HttpClient"/>.</returns>
		private HttpClientHandler CreateHttpClientHandler() => new HttpClientHandler { CookieContainer = _cookies };

		/// <summary>
		/// Создаёт клиента для работы с HTTP-запросами с требуемыми параметрами.
		/// </summary>
		/// <param name="httpClientHandler">Обработчик сообщений для <see cref="HttpClient"/>.</param>
		/// <returns>Клиент для работы с HTTP-запросами.</returns>
		private HttpClient CreateHttpClient(HttpClientHandler httpClientHandler) => new HttpClient(httpClientHandler) { BaseAddress = _settings.BaseAddress };

		private static string GetJavaScriptFromFrame(IHtmlDocument document) => document
			.GetElementsByTagName("script")
			.Where(item => item.ParentElement.TagName.Equals("body", StringComparison.OrdinalIgnoreCase))
			.FirstOrDefault()?
			.TextContent;

		/// <summary>
		/// Удаляет из исходного имени файла все недопустимые символы и возвращает новое имя.
		/// </summary>
		/// <param name="fileName">Исходное имя файла.</param>
		/// <returns>Имя файла без недопустимых символов.</returns>
		private static string GetSafeFileName(string fileName)
		{
			const char replaceChar = '_';
			return new string(fileName
				.Trim()
				.Select(@char => _invalidFileNameChars.Contains(@char) ? replaceChar : @char)
				.ToArray());
		}
	}
}

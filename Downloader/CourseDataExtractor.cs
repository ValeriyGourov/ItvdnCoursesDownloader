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

namespace Downloader;

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
	/// Параметры сериализатора JSON, используемые при извлечении из JavaScript веб-страницы требуемых данных.
	/// </summary>
	private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
	};

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
		IHtmlDocument document = await GetDocumentAsync(_courseUri).ConfigureAwait(false);
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

		Uri materialsUri = await materialsUriTask.ConfigureAwait(false);
		if (lessonTasks != null)
		{
			await Task.WhenAll(lessonTasks).ConfigureAwait(false);
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
	/// <returns>Загруженный HTML-документ.</returns>
	private async Task<IHtmlDocument> GetDocumentAsync(Uri uri, Uri referrer = null)
	{
		string html = null;

		using HttpClientHandler httpClientHandler = CreateHttpClientHandler();
		httpClientHandler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

		using HttpClient httpClient = CreateHttpClient(httpClientHandler);
		using HttpRequestMessage httpRequest = new()
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

		using HttpResponseMessage response = await httpClient
			.SendAsync(httpRequest, _cancellationToken)
			.ConfigureAwait(false);
		//response.EnsureSuccessStatusCode();
		if (response.IsSuccessStatusCode)
		{
			html = await response.Content
				.ReadAsStringAsync(_cancellationToken)
				.ConfigureAwait(false);
		}
		else
		{
			// TODO: Зафиксировать ошибку.
		}

		return html == null
			? null
			: await new HtmlParser()
				.ParseDocumentAsync(html, _cancellationToken)
				.ConfigureAwait(false);
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
			Uri requestUri = new(_settings.BaseAddress, "Video/GetLinkToMaterials");

			using HttpClientHandler httpClientHandler = CreateHttpClientHandler();
			using HttpClient httpClient = CreateHttpClient(httpClientHandler);

			var content = new
			{
				linkToMaterials
			};

			using HttpResponseMessage response = await PostAsJsonAsync(httpClient, requestUri, content).ConfigureAwait(false);
			//response.EnsureSuccessStatusCode();
			if (response.IsSuccessStatusCode)
			{
				string jsonString = await response.Content
					.ReadAsStringAsync(_cancellationToken)
					.ConfigureAwait(false);
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
		.FirstOrDefault(item => item.GetAttribute("itemprop") == "name")?
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
				.FirstOrDefault(tag => tag.TagName.Equals("a", StringComparison.OrdinalIgnoreCase))
			let idTag = childrenTags
				.FirstOrDefault(tag => tag.HasAttribute("data-lesson-id"))
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
					.TextContent
			};
		return query.ToList();
	}

	/// <summary>
	/// Получает ссылку для скачивания файл видео указанного урока курса.
	/// </summary>
	/// <param name="lesson">Данные урока курса.</param>
	private async Task SetLessonVideoAsync(Lesson lesson)
	{
		IHtmlDocument lessonDocument = await GetDocumentAsync(lesson.Uri, lesson.Uri).ConfigureAwait(false);
		if (lessonDocument is null)
		{
			return;
		}

		LessonSettings lessonSettings = GetLessonSettingsFromDocument(lessonDocument);
		if (lessonSettings is null)
		{
			return;
		}

		Uri requestUri = new(_settings.BaseAddress, "Video/GetVideoId");
		var content = new
		{
			lessonUrl = lessonSettings.LessonUrl,
			courseUrl = lessonSettings.VideosetUrl
		};

		using HttpClientHandler httpClientHandler = CreateHttpClientHandler();
		using HttpClient httpClient = CreateHttpClient(httpClientHandler);
		using HttpResponseMessage response = await PostAsJsonAsync(httpClient, requestUri, content).ConfigureAwait(false);
		//response.EnsureSuccessStatusCode();

		VideoIdResponse videoIdResponse;
		if (response.IsSuccessStatusCode)
		{
			string responseContent = await response.Content
				.ReadAsStringAsync(_cancellationToken)
				.ConfigureAwait(false);
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
		IHtmlDocument frame = await GetDocumentAsync(new Uri(frameUrl), lesson.Uri).ConfigureAwait(false);
		string javaScript = GetVideoConfigScript(frame);

		Uri videoUri = GetVideoUri(javaScript);
		lesson.Video = new DownloadFile(videoUri)
		{
			Title = $"{lesson.Number}. {GetSafeFileName(lesson.Title)}"
		};
	}

	/// <summary>
	/// Извлекает настройки урока из страницы этого урока.
	/// </summary>
	/// <param name="document">Документ станицы.</param>
	/// <returns>Настройки урока, необходимые для загрузки файла видео.</returns>
	private LessonSettings GetLessonSettingsFromDocument(IHtmlDocument document)
	{
		string lessonSettingsScript = GetLessonSettingsScript(document);

		const string dataVariableName = "settings";
		Expression evaluateExpressionFunc(Program program) =>
			(from programBody in program.Body
			 where program.Body.Count == 1
			 let body = programBody as VariableDeclaration
			 from declaration in body.Declarations
			 where declaration.Id.Name.Equals(dataVariableName, StringComparison.OrdinalIgnoreCase)
			 select declaration.Init)
			 .FirstOrDefault();

		return GetJsonDataFromJavaScript<LessonSettings>(lessonSettingsScript, evaluateExpressionFunc);
	}

	/// <summary>
	/// Извлекает из сценария вложенного содержимого страницы урока адрес для загрузки файла видео урока.
	/// </summary>
	/// <param name="javaScript">Сценарий вложенного содержимого страницы урока.</param>
	/// <returns>Адрес для загрузки файла видео урока.</returns>
	private static Uri GetVideoUri(string javaScript)
	{
		const string dataVariableName = "config";
		Expression evaluateExpressionFunc(Program program) =>
			(from programBody in program.Body
			 where program.Body.Count == 1
			 let body = programBody as ExpressionStatement
			 let expression = body?.Expression as CallExpression
			 let callee = expression?.Callee as FunctionExpression
			 from variableDeclaration in callee.VariableDeclarations
			 from declaration in variableDeclaration.Declarations
			 where declaration.Id.Name.Equals(dataVariableName, StringComparison.OrdinalIgnoreCase)
			 select declaration.Init)
			 .FirstOrDefault();

		VideoListDefinition videoList = GetJsonDataFromJavaScript<VideoListDefinition>(javaScript, evaluateExpressionFunc);
		return videoList?.Request.Files.Progressive
			.OrderByDescending(item => item.Quality)
			.FirstOrDefault()?
			.Url;
	}

	/// <summary>
	/// Извлекает из текста JavaScript данные, представленные экземпляром типа <typeparamref name="T"/>.
	/// </summary>
	/// <typeparam name="T">Тип, к которому нужно преобразовать извлечённые данные.</typeparam>
	/// <param name="javaScript">Текст JavaScript.</param>
	/// <param name="evaluateExpressionFunc">Выражение для извлечения нужных данных из разобранного парсером <see cref="JavaScriptParser"/> текста JavaScript.</param>
	/// <returns>Данные, извлечённые из текста сценария.</returns>
	private static T GetJsonDataFromJavaScript<T>(
		string javaScript,
		Func<Program, Expression> evaluateExpressionFunc)
		where T : class
	{
		if (string.IsNullOrWhiteSpace(javaScript))
		{
			return null;
		}

		JavaScriptParser javaScriptParser = new();
		Program program = javaScriptParser.Parse(javaScript);

		Expression init = evaluateExpressionFunc(program);
		if (init == null)
		{
			// TODO; Зафиксировать ошибку.
			return null;
		}

		Jint.Engine jintEngine = new();
		JsValue initObject = jintEngine.EvaluateExpression(init) as JsValue;

		Jint.Native.Json.JsonSerializer jsonSerializer = new(jintEngine);
		string jsonString = jsonSerializer
			.Serialize(initObject, new JsValue(""), new JsValue(" "))
			.AsString();

		return JsonSerializer.Deserialize<T>(jsonString, _jsonSerializerOptions);
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
		HttpResponseMessage responseMessage = await httpClient
			.PostAsync(requestUri, httpContent, _cancellationToken)
			.ConfigureAwait(false);
		return responseMessage;
	}

	/// <summary>
	/// Создаёт обработчик сообщений для <see cref="HttpClient"/> с требуемыми параметрами.
	/// </summary>
	/// <returns>Обработчик сообщений для <see cref="HttpClient"/>.</returns>
	private HttpClientHandler CreateHttpClientHandler() => new() { CookieContainer = _cookies };

	/// <summary>
	/// Создаёт клиента для работы с HTTP-запросами с требуемыми параметрами.
	/// </summary>
	/// <param name="httpClientHandler">Обработчик сообщений для <see cref="HttpClient"/>.</param>
	/// <returns>Клиент для работы с HTTP-запросами.</returns>
	private HttpClient CreateHttpClient(HttpClientHandler httpClientHandler) => new(httpClientHandler) { BaseAddress = _settings.BaseAddress };

	/// <summary>
	/// Извлекает элемент script страницы, содержащий конфигурацию файлов видео.
	/// </summary>
	/// <param name="document">Документ станицы.</param>
	/// <returns>Извлечённый JavaScript.</returns>
	private static string GetVideoConfigScript(IHtmlDocument document) => GetScriptFromDocument(
		document,
		item => item.ParentElement.TagName.Equals("body", StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Извлекает элемент script страницы, содержащий настройки конкретного урока.
	/// </summary>
	/// <param name="document">Документ станицы.</param>
	/// <returns>Извлечённый JavaScript.</returns>
	private static string GetLessonSettingsScript(IHtmlDocument document) => GetScriptFromDocument(
		document,
		item => item.ParentElement.ClassName?.Equals("video-player-wrapper", StringComparison.OrdinalIgnoreCase) == true);

	/// <summary>
	/// Извлекает из HTML-документа текст сценария.
	/// </summary>
	/// <param name="document">Документ станицы.</param>
	/// <param name="predicate">Условие поиска требуемого элемента, содержащего сценарий.</param>
	/// <returns>Текст сценария. Если требуемый элемент не найден, будет возвращено значение <see langword="null"/>.</returns>
	private static string GetScriptFromDocument(IHtmlDocument document, Func<IElement, bool> predicate) => document
		.Scripts
		.FirstOrDefault(predicate)?
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
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Text;

using Downloader.Infrastructure;
using Downloader.Models;
using Downloader.Models.Responses;
using Downloader.Pages;

using Jint.Native;
using Jint.Parser;
using Jint.Parser.Ast;

using Microsoft.Edge.SeleniumTools;

using Newtonsoft.Json;

using OpenQA.Selenium;

namespace Downloader
{
	public sealed class Engine
	{
		private Uri _courseUri;

		private readonly EngineSettings _settings;
		private readonly CancellationToken _cancellationToken;
		private CookieContainer _cookies = new CookieContainer();
		//private readonly TaskFactory _taskFactory;
		private Uri _baseUri;
		private readonly char[] _invalidFileNameChars = Path.GetInvalidFileNameChars();
		private const string _applicationDataFolderName = "ITVDN Courses Downloader";
		private const string _cookiesFileName = "Cookies";
		private static readonly string _cookiesfileFullName = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			_applicationDataFolderName,
			_cookiesFileName);

		//public int MaxDownloadThread { get; set; } = 3;

		/// <summary>
		/// Основной конструктор.
		/// </summary>
		/// <param name="engineSettings">Настройки движка загрузки.</param>
		/// <param name="cancellationToken">Объект уведомления об отмене операции.</param>
		/// <exception cref="EngineSettingsValidationException">Некорректные настройки движка.</exception>
		public Engine(EngineSettings engineSettings, CancellationToken cancellationToken)
		{
			List<ValidationResult> validationResults = new List<ValidationResult>();

			if (!Validator.TryValidateObject(
				engineSettings,
				new ValidationContext(engineSettings),
				validationResults,
				true))
			{
				throw new EngineSettingsValidationException("Ошибки в настройках.", validationResults);
			}

			_settings = engineSettings;
			_cancellationToken = cancellationToken;
		}

		public async Task<Course> GetCourseAsync(Uri courseUri)
		{
			_courseUri = courseUri;
			_baseUri = new Uri(_courseUri.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped));

			if (!SetCookies())
			{
				return null;
			}

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
				//Parallel.ForEach(lessons,
				//    new ParallelOptions()
				//    {
				//        CancellationToken = cancellationToken,
				//        TaskScheduler = null
				//    },
				//    async lesson => await SetLessonVideoUri(lesson));

				//foreach (var lesson in lessons)
				//{
				//    await SetLessonVideoUriAsync(lesson);
				//}


				//await SetLessonVideoUriAsync(lessons[0]);



				//TaskFactory taskFactory = new TaskFactory(cancellationToken);
				//Task[] tasks = new Task[1];
				//tasks[0] = await taskFactory.StartNew(() => SetLessonVideoUriAsync(lessons[0]));
				//await Task.WhenAll(tasks);


				//Task[] tasks = new Task[lessons.Count];
				//for (int i = 0; i < lessons.Count; i++)
				//{
				//    tasks[i] = await _taskFactory.StartNew(() => SetLessonVideoAsync(lessons[i]));
				//}
				//await Task.WhenAll(tasks);


				//Task[] tasks = lessons.Select(lesson => SetLessonVideoAsync(lesson)).ToArray();
				//await Task.WhenAll(tasks);


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
				Lessons = lessons,
				Materials = materialsUri == null ? null : new DownloadFile(materialsUri) { Title = Course.MaterialsTitle }
			};
		}

		public async Task<bool> DownloadFilesAsync(Course course)
		{
			string safeCourseTitle = GetSafeFileName(course?.Title);
			string courseSavePath = Path.Combine(_settings.SavePath, safeCourseTitle);
			try
			{
				Directory.CreateDirectory(courseSavePath);
			}
			catch (Exception)
			{
				// TODO: Зафиксировать ошибку.
				return false;
			}

			course.ChangeSavePath(courseSavePath);

			Task[] tasks = course.CorrectFiles
				 .Select(file => DownloadFileAsync(file))
				 .ToArray();
			Task waitTask = Task.WhenAll(tasks);

			bool succsess = true;
			await waitTask.ContinueWith(task => succsess = !task.IsFaulted);

			return succsess;
		}

		private string GetSafeFileName(string fileName)
		{
			const char replaceChar = '_';
			return new string(fileName
				.Trim()
				.Select(@char => _invalidFileNameChars.Contains(@char) ? replaceChar : @char)
				.ToArray());
		}

		private async Task<IHtmlDocument> GetDocumentAsync(Uri uri, Uri referrer = null)
		{
			string html = null;

			using (HttpClientHandler httpClientHandler = CreateHttpClientHandler())
			{
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

				using (HttpResponseMessage response = await httpClient.SendAsync(httpRequest, _cancellationToken))
				{
					//response.EnsureSuccessStatusCode();
					if (response.IsSuccessStatusCode)
					{
						html = await response.Content.ReadAsStringAsync();
					}
					else
					{
						// TODO: Зафиксировать ошибку.
					}
				};
			}

			return html == null ? null : await new HtmlParser().ParseDocumentAsync(html, _cancellationToken);
		}

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

		private string GetCourseTitle(IHtmlDocument document) => document
			.GetElementsByTagName("h1")
			.Where(item => item.GetAttribute("itemprop") == "name")
			.FirstOrDefault()?
			.TextContent;

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
				Uri requestUri = new Uri(_baseUri, "Video/GetLinkToMaterials");

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
					materialsUrl = JsonConvert.DeserializeObject<string>(jsonString);
				}
				else
				{
					// TODO: Зафиксировать ошибку.
				}
			}

			return materialsUrl == null ? null : new Uri(materialsUrl);
		}

		private async Task SetLessonVideoAsync(Lesson lesson)
		{
			IHtmlDocument lessonDocument = await GetDocumentAsync(lesson.Uri, lesson.Uri);
			if (lessonDocument == null)
			{
				return;
			}

			VideoIdResponse videoIdResponse = null;
			Uri requestUri = new Uri(_baseUri, "Video/GetVideoId");

			using (HttpClientHandler httpClientHandler = CreateHttpClientHandler())
			using (HttpClient httpClient = CreateHttpClient(httpClientHandler))
			{
				var content = new
				{
					lessonId = lesson.Id
				};

				using HttpResponseMessage response = await PostAsJsonAsync(httpClient, requestUri, content);
				//response.EnsureSuccessStatusCode();
				if (response.IsSuccessStatusCode)
				{
					string responseContent = await response.Content.ReadAsStringAsync();
					videoIdResponse = JsonConvert.DeserializeObject<VideoIdResponse>(responseContent);
				}
				else
				{
					// TODO: Зафиксировать ошибку.
					return;
				}
			}

			if (videoIdResponse == null)
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

		private Uri GetVideoUri(string javaScript)
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

			var videoListDefinition = new
			{
				request = new
				{
					files = new
					{
						progressive = new[]
						{
							new
							{
								profile = 0,
								url = "",
								quality = ""
							}
						}
					}
				}
			};
			var videoList = JsonConvert.DeserializeAnonymousType(configJson, videoListDefinition);
			string uri = videoList.request.files.progressive
				.OrderByDescending(item => item.quality)
				.FirstOrDefault()?
				.url;
			return new Uri(uri);
		}

		private string GetJavaScriptFromFrame(IHtmlDocument document) => document
			.GetElementsByTagName("script")
			.Where(item => item.ParentElement.TagName.Equals("body", StringComparison.OrdinalIgnoreCase))
			.FirstOrDefault()?
			.TextContent;

		/// <summary>
		/// Выполняет авторизацию пользователя на сайте. Авторизация выполняется через Selenium WebDriver с открытием страницы авторизации в браузере.
		/// </summary>
		/// <param name="webDriver">Экземпляр Selenium WebDriver.</param>
		/// <returns></returns>
		private bool Authorize(IWebDriver webDriver)
		{
			bool authorized = true;

			LoginPage loginPage = new LoginPage(webDriver, _baseUri);
			try
			{
				loginPage.Authorize(_settings.Email, _settings.Password);
			}
			catch (Exception)
			{
				authorized = false;
				// TODO: Зафиксировать ошибку.
			}

			return authorized;
		}

		private Task DownloadFileAsync(DownloadFile downloadFile)
		{
			using WebClient webClient = new WebClient();

			_cancellationToken.Register(webClient.CancelAsync);
			webClient.Headers.Add(HttpRequestHeader.Cookie, _cookies.GetCookieHeader(_baseUri));

			webClient.DownloadFileCompleted += (sender, args) =>
			{
				downloadFile.Error = args.Error;
			};
			webClient.DownloadProgressChanged += (sender, args) =>
			{
				if (downloadFile.Size == 0)
				{
					downloadFile.Size = args.TotalBytesToReceive;
				}
				downloadFile.ProgressPercentage = args.ProgressPercentage;
			};
			try
			{
				return webClient.DownloadFileTaskAsync(downloadFile.Uri, downloadFile.TargetFileFullName);
			}
			catch (Exception exception)
			{
				downloadFile.Error = exception;
				return Task.FromResult<object>(null);
			}
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer01:Unnecessary async/await usage", Justification = "<Ожидание>")]
		private async Task<HttpResponseMessage> PostAsJsonAsync(HttpClient httpClient, Uri requestUri, object content)
		{
			//string jsonContent = System.Text.Json.JsonSerializer.Serialize(content);
			string jsonContent = JsonConvert.SerializeObject(content);
			using HttpContent httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

			return await httpClient.PostAsync(requestUri, httpContent, _cancellationToken);
		}

		private HttpClientHandler CreateHttpClientHandler() => new HttpClientHandler { CookieContainer = _cookies };

		private HttpClient CreateHttpClient(HttpClientHandler httpClientHandler) => new HttpClient(httpClientHandler) { BaseAddress = _baseUri };

		/// <summary>
		/// Устанавливает куки для последующих HTTP-запросов.
		/// </summary>
		/// <returns><see langword="true"/> - куки установлены успешно; <see langword="false"/> - в противном случае.</returns>
		private bool SetCookies()
		{
			if (_cookies.Count != 0)
			{
				return true;
			}
			else
			{
				CookieContainer cookiesFromDisk = ReadCookiesFromDisk();
				if (cookiesFromDisk?.Count > 0)
				{
					_cookies = cookiesFromDisk;
					return true;
				}

				using IWebDriver webDriver = CreateWebDriver();

				bool authorized = Authorize(webDriver);

				if (authorized)
				{
					webDriver.Manage().Window.Minimize();

					webDriver.Manage().Cookies.AllCookies
						.ToList()
						.ForEach(cookie => _cookies.Add(new System.Net.Cookie(
							cookie.Name,
							HttpUtility.UrlEncode(cookie.Value),
							cookie.Path,
							cookie.Domain)));

					WriteCookiesToDisk(_cookies);
				}

				webDriver.Quit();

				if (authorized)
				{
					return true;
				}
				else
				{
					// TODO: Зафиксировать ошибку.
					return false;
				}
			}
		}

		/// <summary>
		/// Создаёт новый экземпляр Selenium WebDriver с требуемыми настройками.
		/// </summary>
		/// <returns>Новый экземпляр Selenium WebDriver.</returns>
		private IWebDriver CreateWebDriver()
		{
			EdgeOptions options = new EdgeOptions
			{
				UseChromium = true
			};

			try
			{
				return new EdgeDriver(_settings.WebDriversPath, options);
			}
			catch
			//catch (DriverServiceNotFoundException exception)
			{
				// TODO: Зафиксировать ошибку.
				throw;
			}
		}

		/// <summary>
		/// Сохраняет куки на диск для использования в будущих запусках приложения.
		/// </summary>
		/// <param name="cookies">Контейнер с куки.</param>
		/// <exception cref="SerializationException">Ошибка сериализации куки.</exception>
		private static void WriteCookiesToDisk(CookieContainer cookies)
		{
			FileInfo cookiesFileInfo = new FileInfo(_cookiesfileFullName);
			if (!Directory.Exists(cookiesFileInfo.DirectoryName))
			{
				Directory.CreateDirectory(cookiesFileInfo.DirectoryName);
			}

			using FileStream fileStream = new FileStream(_cookiesfileFullName, FileMode.Create);
			BinaryFormatter formatter = new BinaryFormatter();

			try
			{
				formatter.Serialize(fileStream, cookies);
			}
			catch (SerializationException)
			{
				// TODO: Зафиксировать ошибку.
				//Console.WriteLine("Failed to serialize. Reason: " + exception.Message);
			}
			finally
			{
				fileStream.Close();
			}
		}

		/// <summary>
		/// Считывает с диска ранее сохранённые куки.
		/// </summary>
		/// <returns>Контейнер с куки.</returns>
		/// <exception cref="SerializationException">Ошибка десериализации куки.</exception>
		private static CookieContainer ReadCookiesFromDisk()
		{
			CookieContainer cookies = null;

			if (File.Exists(_cookiesfileFullName))
			{
				using FileStream fileStream = new FileStream(_cookiesfileFullName, FileMode.Open);
				BinaryFormatter formatter = new BinaryFormatter();

				try
				{
					cookies = formatter.Deserialize(fileStream) as CookieContainer;
				}
				catch (SerializationException)
				{
					// TODO: Зафиксировать ошибку.
					//Console.WriteLine("Failed to deserialize. Reason: " + exception.Message);
				}
				finally
				{
					fileStream.Close();
				}
			}

			return cookies;
		}
	}
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

using Downloader.Infrastructure;
using Downloader.Models;
using Downloader.Models.Responses;

using Jint.Native;
using Jint.Parser;
using Jint.Parser.Ast;

using Newtonsoft.Json;

namespace Downloader
{
	public sealed class Engine
	{
		private /*readonly*/ Uri _courseUri;
		private readonly CancellationToken _cancellationToken /*= new CancellationToken()*/;
		//private readonly HttpClientRequester _httpClientRequester = new HttpClientRequester();
		private CookieContainer _cookies = new CookieContainer();
		//private readonly TaskFactory _taskFactory;
		//private readonly object lesson;
		//private static readonly Uri baseUri = new Uri("https://itvdn.com");
		private Uri _baseUri;

		//private Uri videoFilesRequestUri;
		//private Uri authorizeRequestUri;
		//private readonly Uri videoFilesRequestUri = new Uri(baseUri, "Video/GetVideoFiles");
		//private readonly Uri authorizeRequestUri = new Uri(baseUri, "ru/Account/Login");
		private readonly char[] _invalidFileNameChars = Path.GetInvalidFileNameChars();


		public string Email { get; set; }
		public string Password { get; set; }
		//public int MaxDownloadThread { get; set; } = 3;

		public Engine(CancellationToken cancellationToken)
		{
			_cancellationToken = cancellationToken;
			//_taskFactory = new TaskFactory(cancellationToken, TaskCreationOptions.LongRunning, TaskContinuationOptions.None, new LimitedConcurrencyLevelTaskScheduler(2));
			//_taskFactory = new TaskFactory(cancellationToken);
		}

		public async Task<Course> GetCourseAsync(string courseUrl)
		{
			if (string.IsNullOrWhiteSpace(courseUrl))
			{
				throw new ArgumentNullException(nameof(courseUrl));
			}
			_courseUri = new Url(courseUrl);
			_baseUri = new Uri(_courseUri.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped));

			if (_cookies.Count == 0
				&& !await AuthorizeAsync())
			{
				// TODO: Зафиксировать ошибку.
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

		public async Task<bool> DownloadFilesAsync(Course course, string savePath)
		{
			//char replaceChar = '_';
			//string safeCourseTitle = course.Title;
			//foreach (char invalidChar in Path.GetInvalidFileNameChars())
			//{
			//    safeCourseTitle = safeCourseTitle.Replace(invalidChar, replaceChar);
			//}

			string safeCourseTitle = GetSafeFileName(course?.Title);
			string courseSavePath = Path.Combine(savePath, safeCourseTitle);
			//DirectoryInfo directoryInfo = new DirectoryInfo(courseSavePath);
			//directoryInfo.Create
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




			//int lessonsCount = course.Lessons.Count;
			//Action[] actions = new Action[lessonsCount];
			//for (int i = 0; i < lessonsCount; i++)
			//{
			//    // Note that we create the Action here, but do not start it.
			//    Lesson lesson = course.Lessons[i];
			//    string safeLessonTitle = GetSafeFileName(lesson.Title, invalidFileNameChars);
			//    string lessonFileName = $"{lesson.Number}. {safeLessonTitle}";
			//    actions[i] = () => DownloadFileAsync(lesson.VideoUri, courseSavePath, lessonFileName);
			//}
			//ParallelOptions parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 2 };
			//Parallel.Invoke(parallelOptions, actions);





			//await DownloadFile(course.MaterialsUri, courseSavePath);
			//foreach (Lesson lesson in course.Lessons)
			//{
			//    string safeLessonTitle = GetSafeFileName(lesson.Title, invalidFileNameChars);
			//    string lessonFileName = $"{lesson.Number}. {safeLessonTitle}";
			//    DownloadFile(lesson.VideoUri, courseSavePath, lessonFileName);
			//}





			//var files = course.GetDownloadFiles().CorrectFiles;
			//ParallelLoopResult parallelLoopResult;
			//await Task.Run(() =>
			//{
			//    parallelLoopResult = Parallel.ForEach(files, file => DownloadFile(file));
			//});
			//int failures = files.Count(file => file.Status != DownloadFileStatus.Completed);
			//return failures == 0;



			Task[] tasks = course.CorrectFiles
				 .Select(file => DownloadFileAsync(file))
				 .ToArray();
			Task waitTask = Task.WhenAll(tasks);

			bool succsess = true;
			await waitTask.ContinueWith(task => succsess = !task.IsFaulted);

			return succsess;





			//var (correctFiles, incorrectFiles) = course.GetDownloadFiles();
			//int filesCount = correctFiles.Count;
			//Task[] tasks = new Task[filesCount];
			//for (int i = 0; i < filesCount; i++)
			//{
			//    tasks[i] = await _taskFactory.StartNew(() => DownloadFileAsync(correctFiles[i]),
			//        _cancellationToken,
			//        TaskCreationOptions.LongRunning,
			//        TaskScheduler.Default);
			//}
			////await Task.WhenAll(tasks);

			//int failures = 0;
			////failures = tasks.Count(task => task.Exception != null);
			//_taskFactory.ContinueWhenAll(tasks,
			//    completedTasks =>
			//    {
			//        //failures = completedTasks.Where(task => !task.Result).Count();
			//        failures = completedTasks.Count(task => task.Exception != null);
			//        //failures = completedTasks.Where(task => task.Exception != null).Count();
			//    })
			//    .Wait();

			//if (failures > 0)
			//{
			//    return false;
			//}






			//Parallel.ForEach(course.Lessons,
			//    new ParallelOptions()
			//    {
			//        CancellationToken = cancellationToken,
			//        MaxDegreeOfParallelism = MaxDownloadThread
			//    },
			//    lesson =>
			//    {
			//        string safeLessonTitle = GetSafeFileName(lesson.Title, invalidFileNameChars);
			//        string lessonFileName = $"{lesson.Number}. {safeLessonTitle}";
			//        DownloadFile(lesson.VideoUri, courseSavePath, lessonFileName);
			//    });

			//return true;
		}

		private string GetSafeFileName(string fileName)
		{
			const char replaceChar = '_';
			return new string(fileName
				.Trim()
				.Select(@char => _invalidFileNameChars.Contains(@char) ? replaceChar : @char)
				.ToArray());


			//const char replaceChar = '_';

			//string safeFileName = fileName.Trim();
			//foreach (char invalidChar in _invalidFileNameChars)
			//{
			//    safeFileName = safeFileName.Replace(invalidChar, replaceChar);
			//}

			//return safeFileName;
		}

		private async Task<IHtmlDocument> GetDocumentAsync(Uri uri, Uri referrer = null)
		{
			string html = null;

			using (HttpClientHandler httpClientHandler = new HttpClientHandler()
			{
				CookieContainer = _cookies,
				AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
			})
			using (HttpClient httpClient = new HttpClient(httpClientHandler) { BaseAddress = _baseUri })
			using (HttpRequestMessage httpRequest = new HttpRequestMessage()
			{
				RequestUri = uri,
				Method = HttpMethod.Get,
			})
			{
				if (referrer != null)
				{
					httpRequest.Headers.Referrer = referrer;
					//httpClient.DefaultRequestHeaders.Referrer = referrer;
					//httpClient.DefaultRequestHeaders.Add("Referer", "https://itvdn.com/ru/video/csharp-for-professional-renewed");
				}
				httpRequest.Headers.UserAgent.Add(new ProductInfoHeaderValue("Client", "1"));
				//httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
				//httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
				//httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
				//httpRequest.Headers.AcceptCharset.Add(new StringWithQualityHeaderValue("utf-8"));
				httpRequest.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
				httpRequest.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
				//httpRequest.Headers.CacheControl = new CacheControlHeaderValue() { NoCache = true };
				//httpRequest.Headers.UserAgent.Add(new ProductInfoHeaderValue("RestSharp", "106.5.4.0"));
				//httpRequest.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip, deflate"));
				//httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue() { NoCache = true };
				//httpClient.DefaultRequestHeaders.Add("cache-control", "no-cache");

				using (HttpResponseMessage response = await httpClient.SendAsync(httpRequest, _cancellationToken))
				{
					//response.EnsureSuccessStatusCode();
					if (response.IsSuccessStatusCode)
					{
						html = await response.Content.ReadAsStringAsync();

						//var IsFormData = response.Content.IsFormData();
						//var IsHttpRequestMessageContent = response.Content.IsHttpRequestMessageContent();
						//var IsHttpResponseMessageContent = response.Content.IsHttpResponseMessageContent();
						//var IsMimeMultipartContent = response.Content.IsMimeMultipartContent();
						////void LoadIntoBufferAsync = await response.Content.LoadIntoBufferAsync();
						////var ReadAsAsync = await response.Content.ReadAsAsync<string>();
						//var ReadAsByteArrayAsync = await response.Content.ReadAsByteArrayAsync();
						////var ReadAsFormDataAsync = await response.Content.ReadAsFormDataAsync();
						//var ReadAsHttpRequestMessageAsync = await response.Content.ReadAsHttpRequestMessageAsync();
						//var ReadAsHttpResponseMessageAsync = await response.Content.ReadAsHttpResponseMessageAsync();
						//var ReadAsMultipartAsync = await response.Content.ReadAsMultipartAsync();
						//var ReadAsStreamAsync = await response.Content.ReadAsStreamAsync();
					}
					else
					{
						// TODO: Зафиксировать ошибку.
					}
				};
			}

			return html == null ? null : await new HtmlParser().ParseDocumentAsync(html, _cancellationToken);

			//string html = null;

			//using (HttpClientHandler httpClientHandler = new HttpClientHandler() { CookieContainer = cookies })
			//using (HttpClient httpClient = new HttpClient(httpClientHandler) { BaseAddress = baseUri })
			//{
			//    if (referrer != null)
			//    {
			//        //httpClient.DefaultRequestHeaders.Referrer = referrer;
			//        httpClient.DefaultRequestHeaders.Add("Referer", "https://itvdn.com/ru/video/csharp-for-professional-renewed");
			//    }
			//    //httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue() { NoCache = true };
			//    //httpClient.DefaultRequestHeaders.Add("cache-control", "no-cache");
			//    HttpResponseMessage response = await httpClient.GetAsync(uri, cancellationToken);

			//    //response.EnsureSuccessStatusCode();
			//    if (response.IsSuccessStatusCode)
			//    {
			//        html = await response.Content.ReadAsStringAsync();
			//    }
			//    else
			//    {
			//        // TODO: Зафиксировать ошибку.
			//    }
			//}

			//return html == null ? null : await new HtmlParser().ParseAsync(html, cancellationToken);
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

			//return document
			//    .GetElementsByClassName("video-lesson-item")
			//    .SelectMany(videoLessonItem => videoLessonItem.Children)
			//    .Where(lesson => lesson.TagName.Equals("a", StringComparison.OrdinalIgnoreCase))
			//    .Select(link => new Lesson
			//    {
			//        Uri = new Uri(courseUri, link.GetAttribute("href")),
			//        Number = Convert.ToInt32(link.GetElementsByClassName("lesson-number").FirstOrDefault()?.TextContent),
			//        Title = link.GetElementsByClassName("lsn-name-wrapper").FirstOrDefault()?.TextContent
			//    })
			//    .ToList();
		}

		//private List<Lesson> GetLessons(IHtmlDocument document) => document
		//    .GetElementsByClassName("video-lesson-item")
		//    .SelectMany(videoLessonItem => videoLessonItem.Children)
		//    .Where(lesson => lesson.TagName.Equals("a", StringComparison.OrdinalIgnoreCase))
		//    .Select(link => new Lesson
		//    {
		//        Uri = new Uri(courseUri, link.GetAttribute("href")),
		//        Number = Convert.ToInt32(link.GetElementsByClassName("lesson-number").FirstOrDefault()?.TextContent),
		//        Title = link.GetElementsByClassName("lsn-name-wrapper").FirstOrDefault()?.TextContent
		//    })
		//    .ToList();

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
			//string linkToMaterials = document
			//    .GetElementsByClassName("btn-filled-orange btn-get-materials get-materials")
			//    .FirstOrDefault()?
			//    .GetAttribute("data-link");

			string materialsUrl = null;
			if (Uri.IsWellFormedUriString(linkToMaterials, UriKind.Absolute))
			{
				Uri requestUri = new Uri(_baseUri, "Video/GetLinkToMaterials");

				using HttpClientHandler httpClientHandler = new HttpClientHandler() { CookieContainer = _cookies };
				using HttpClient httpClient = new HttpClient(httpClientHandler) { BaseAddress = _baseUri };

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
			//Console.WriteLine($"SetLessonVideoUriAsync: {lesson.Title}");

			IHtmlDocument lessonDocument = await GetDocumentAsync(lesson.Uri, lesson.Uri);
			if (lessonDocument == null)
			{
				return;
			}

			VideoIdResponse videoIdResponse = null;
			Uri requestUri = new Uri(_baseUri, "Video/GetVideoId");

			using (HttpClientHandler httpClientHandler = new HttpClientHandler() { CookieContainer = _cookies })
			using (HttpClient httpClient = new HttpClient(httpClientHandler) { BaseAddress = _baseUri })
			{
				//httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
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

		private async Task<bool> AuthorizeAsync()
		{
			Dictionary<string, string> content = new Dictionary<string, string>
			{
				{ "Email", Email },
				{ "Password", Password }
			};

			Uri requestUri = new Uri(_baseUri, "ru/Account/Login");

			using (HttpClientHandler httpClientHandler = new HttpClientHandler { CookieContainer = _cookies })
			using (HttpClient httpClient = new HttpClient(httpClientHandler) { BaseAddress = _baseUri })
			using (FormUrlEncodedContent httpContent = new FormUrlEncodedContent(content))
			using (HttpResponseMessage response = await httpClient.PostAsync(requestUri, httpContent, _cancellationToken))
			{
				//response.EnsureSuccessStatusCode();
				if (!response.IsSuccessStatusCode)
				{
					// TODO: Зафиксировать ошибку.
					return false;
				}
			}

			return true;
		}

		private Task DownloadFileAsync(DownloadFile downloadFile)
		{
			//Console.WriteLine($"DownloadFile: {downloadFile.Uri}");

			//if (string.IsNullOrWhiteSpace(savePath))
			//{
			//    // TODO: Зафиксировать ошибку.
			//    return false;
			//}

			//DirectoryInfo directoryInfo = new DirectoryInfo(savePath);
			//if (!directoryInfo.Exists)
			//{
			//    // TODO: Зафиксировать ошибку.
			//    return false;
			//}



			using WebClient webClient = new WebClient();

			_cancellationToken.Register(webClient.CancelAsync);
			webClient.Headers.Add(HttpRequestHeader.Cookie, _cookies.GetCookieHeader(_baseUri));

			webClient.DownloadFileCompleted += (sender, args) =>
			{
				downloadFile.Error = args.Error;
				//if (downloadFile.Error == null)
				//{
				//    downloadFile.Status = DownloadFileStatus.Completed;
				//}
			};
			webClient.DownloadProgressChanged += (sender, args) =>
			{
				if (downloadFile.Size == 0)
				{
					downloadFile.Size = args.TotalBytesToReceive;
				}
				//downloadFile.Status = DownloadFileStatus.InProgress;
				downloadFile.ProgressPercentage = args.ProgressPercentage;
			};
			try
			{
				return webClient.DownloadFileTaskAsync(downloadFile.Uri, downloadFile.TargetFileFullName);
			}
			catch (Exception exception)
			{
				downloadFile.Error = exception;
				//downloadFile.Status = DownloadFileStatus.Error;
				//return false;
				return Task.FromResult<object>(null);
			}

			//return true;


			//FileWebRequest fileWebRequest = WebRequest.CreateDefault(fileUri) as FileWebRequest;
			//FileWebRequest.
			//fileWebRequest.AuthenticationLevel=System.Net.Security.AuthenticationLevel.MutualAuthRequested
			//fileWebRequest.get
		}

		[System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer01:Unnecessary async/await usage", Justification = "<Ожидание>")]
		private Task<HttpResponseMessage> PostAsJsonAsync(HttpClient httpClient, Uri requestUri, object content)
		{
			//string jsonContent = System.Text.Json.JsonSerializer.Serialize(content);
			string jsonContent = JsonConvert.SerializeObject(content);
			using HttpContent httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

			return httpClient.PostAsync(requestUri, httpContent, _cancellationToken);
		}
		//[System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer01:Unnecessary async/await usage", Justification = "<Ожидание>")]
		//private async Task<HttpResponseMessage> PostAsJsonAsync(HttpClient httpClient, Uri requestUri, object content)
		//{
		//	//string jsonContent = System.Text.Json.JsonSerializer.Serialize(content);
		//	string jsonContent = JsonConvert.SerializeObject(content);
		//	using HttpContent httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

		//	return await httpClient.PostAsync(requestUri, httpContent, _cancellationToken);
		//}

		//private /*async*/ Task/*<bool>*//*bool*/ DownloadFileAsync(Uri fileUri, string savePath, string fileName)
		//{
		//    Console.WriteLine($"DownloadFile: {fileUri}");

		//    //if (string.IsNullOrWhiteSpace(savePath))
		//    //{
		//    //    // TODO: Зафиксировать ошибку.
		//    //    return false;
		//    //}

		//    //DirectoryInfo directoryInfo = new DirectoryInfo(savePath);
		//    //if (!directoryInfo.Exists)
		//    //{
		//    //    // TODO: Зафиксировать ошибку.
		//    //    return false;
		//    //}


		//    FileInfo fileInfo = new FileInfo(fileUri.AbsolutePath);
		//    string fullFileName = Path.Combine(savePath, $"{fileName}{fileInfo.Extension}");

		//    using (WebClient webClient = new WebClient())
		//    {
		//        //try
		//        //{
		//        //webClient.DownloadFileAsync(fileUri, fullFileName);
		//        return webClient.DownloadFileTaskAsync(fileUri, fullFileName);
		//        //    }
		//        //    catch (Exception)
		//        //    {
		//        //        // TODO: Зафиксировать ошибку.
		//        //        return false;
		//        //    }
		//    }

		//    //return true;


		//    //FileWebRequest fileWebRequest = WebRequest.CreateDefault(fileUri) as FileWebRequest;
		//    //FileWebRequest.
		//    //fileWebRequest.AuthenticationLevel=System.Net.Security.AuthenticationLevel.MutualAuthRequested
		//    //fileWebRequest.get
		//}
	}
}

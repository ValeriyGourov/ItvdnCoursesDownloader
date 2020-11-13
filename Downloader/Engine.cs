using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

using Downloader.Infrastructure;
using Downloader.Models;
using Downloader.Pages;

using Microsoft.Edge.SeleniumTools;

using OpenQA.Selenium;

namespace Downloader
{
	/// <summary>
	/// Механизм получения данных курсов.
	/// </summary>
	public sealed class Engine
	{
		/// <summary>
		/// Настройки движка загрузки данных.
		/// </summary>
		private readonly EngineSettings _settings;

		/// <summary>
		/// Маркер отмены выполняемой операции.
		/// </summary>>
		private readonly CancellationToken _cancellationToken;

		/// <summary>
		/// Контейнер для хранения куки сайта курсов между запросами.
		/// </summary>>
		private CookieContainer _cookies = new CookieContainer();

		/// <summary>
		/// Имя папки для сохранения служебных файлов приложения.
		/// </summary>
		private const string _applicationDataFolderName = "ITVDN Courses Downloader";

		/// <summary>
		/// Имя файла для сохранения кук сайта.
		/// </summary>
		private const string _cookiesFileName = "Cookies";

		/// <summary>
		/// Полное имя файла для сохранения кук сайта.
		/// </summary>
		private static readonly string _cookiesfileFullName = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			_applicationDataFolderName,
			_cookiesFileName);

		/// <summary>
		/// Основной конструктор.
		/// </summary>
		/// <param name="settings">Настройки движка загрузки.</param>
		/// <param name="cancellationToken">Объект уведомления об отмене операции.</param>
		/// <exception cref="EngineSettingsValidationException">Некорректные настройки движка.</exception>
		public Engine(EngineSettings settings, CancellationToken cancellationToken)
		{
			List<ValidationResult> validationResults = new List<ValidationResult>();

			if (!Validator.TryValidateObject(
				settings,
				new ValidationContext(settings),
				validationResults,
				true))
			{
				throw new EngineSettingsValidationException("Ошибки в настройках.", validationResults);
			}

			_settings = settings;
			_cancellationToken = cancellationToken;
		}

		/// <summary>
		/// Возвращает описание данных загружаемого курса.
		/// </summary>
		/// <param name="courseUri">Адрес курса.</param>
		/// <returns>Объект, представляющий все данные, необходимые для загрузки курса.</returns>
		/// <exception cref="ArgumentException">Адрес курса не совпадает с базовым адресом сайта.</exception>
		public Task<Course> GetCourseAsync(Uri courseUri)
		{
			const int uriAreEqual = 0;
			int compareResult = Uri.Compare(
				courseUri,
				_settings.BaseAddress,
				UriComponents.SchemeAndServer,
				UriFormat.UriEscaped,
				StringComparison.OrdinalIgnoreCase);
			if (compareResult != uriAreEqual)
			{
				throw new ArgumentException($"Сайт в адресе курса '{courseUri}' не совпадает с базовым адресом сайта '{_settings.BaseAddress}'.", nameof(courseUri));
			}

			if (!SetCookies())
			{
				return null;
			}

			CourseDataExtractor courseDataExtractor = new CourseDataExtractor(
				courseUri,
				_settings,
				_cookies,
				_cancellationToken);
			return courseDataExtractor.ExtractAsync();
		}

		/// <summary>
		/// Загружает все файлы курса: видео и дополнительные материалы (при наличии).
		/// </summary>
		/// <param name="course">Данные курса для загрузки.</param>
		/// <returns><see langword="true"/> - все файлы загружены успешно; <see langword="false"/> - в противном случае.</returns>
		/// <exception cref="ArgumentNullException">Не указаны данные курса.</exception>
		public async Task<bool> DownloadFilesAsync(Course course)
		{
			if (course is null)
			{
				throw new ArgumentNullException(nameof(course));
			}

			string courseSavePath = Path.Combine(_settings.SavePath, course.FileSafeTitle);
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
			await waitTask
				.ContinueWith(task => succsess = !task.IsFaulted)
				.ConfigureAwait(false);

			return succsess;
		}

		/// <summary>
		/// Выполняет авторизацию пользователя на сайте. Авторизация выполняется через Selenium WebDriver с открытием страницы авторизации в браузере.
		/// </summary>
		/// <param name="webDriver">Экземпляр Selenium WebDriver.</param>
		/// <returns><see langword="true"/> - авторизация выполнена успешно; <see langword="false"/> - в противном случае.</returns>
		private bool Authorize(IWebDriver webDriver)
		{
			bool authorized = true;

			LoginPage loginPage = new LoginPage(webDriver, _settings.BaseAddress);
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

		/// <summary>
		/// Непосредственно загружает один файл курса.
		/// </summary>
		/// <param name="downloadFile">Описание загружаемого файла.</param>
		private Task DownloadFileAsync(DownloadFile downloadFile)
		{
			using WebClient webClient = new WebClient();

			_cancellationToken.Register(webClient.CancelAsync);
			webClient.Headers.Add(HttpRequestHeader.Cookie, _cookies.GetCookieHeader(_settings.BaseAddress));

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
					CookieCollection cookies = cookiesFromDisk.GetCookies(_settings.BaseAddress);
					if (cookies.All(cookie => !cookie.Expired))
					{
						_cookies = cookiesFromDisk;
						return true;
					}
				}

				using IWebDriver webDriver = CreateWebDriver();

				bool authorized = Authorize(webDriver);

				if (authorized)
				{
					webDriver.Manage().Window.Minimize();

					foreach (OpenQA.Selenium.Cookie seleniumCookie in webDriver.Manage().Cookies.AllCookies)
					{
						System.Net.Cookie newCookie = new(
							seleniumCookie.Name,
							HttpUtility.UrlEncode(seleniumCookie.Value),
							seleniumCookie.Path,
							seleniumCookie.Domain)
						{
							Expires = seleniumCookie.Expiry ?? default,
							HttpOnly = seleniumCookie.IsHttpOnly,
							Secure = seleniumCookie.Secure
						};
						_cookies.Add(newCookie);
					}

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

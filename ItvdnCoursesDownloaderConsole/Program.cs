using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Downloader;
using Downloader.Models;

using Microsoft.Extensions.Configuration;

namespace ItvdnCoursesDownloaderConsole
{
	internal class Program
	{
		private static Engine _engine;
		private static Course _course;
		private static readonly object _consoleWriterLock = new object();

		private static async Task Main(string[] args)
		{
			IConfiguration configuration = new ConfigurationBuilder()
				.AddJsonFile("appsettings.json", true, true)
				.AddUserSecrets<Program>()
				.AddCommandLine(args)
				.Build();

			AppSettings appSettings = await GetSettingsAsync(configuration);
			if (appSettings is null)
			{
				return;
			}

			using CancellationTokenSource cancellation = new CancellationTokenSource();

			_engine = new Engine(appSettings.Engine, cancellation.Token);

			string courseAddress = appSettings.CourseAddress;
			do
			{
				if (courseAddress is null)
				{
					Console.Clear();
					Console.Write("Адрес курса: ");
					courseAddress = Console.ReadLine();
				}

				Console.Clear();
				ShowInfo(appSettings.Engine);

				if (Uri.TryCreate(courseAddress, UriKind.Absolute, out Uri courseUri))
				{
					Console.WriteLine("Загрузка информации о курсе...");

					try
					{
						bool result = await DownloadCourseAsync(courseUri);
					}
					catch (Exception exception)
					{
						await ShowErrorMessageAsync($"Ошибка: {GetErrorDescription(exception)}");
					}

				}
				else
				{
					await ShowErrorMessageAsync($"Некорректный адрес курса: {courseAddress}");
				}

				courseAddress = null;
			} while (OneMoreCourseRequest());
		}

		/// <summary>
		/// Получает настройки приложения из всех предусмотренных источников.
		/// </summary>
		/// <param name="configuration">Набор свойств конфигурации приложения.</param>
		/// <returns>Настройки приложения.</returns>
		private static async Task<AppSettings> GetSettingsAsync(IConfiguration configuration)
		{
			AppSettings appSettings = configuration.Get<AppSettings>();

			List<ValidationResult> validationResults = new List<ValidationResult>();

			Validator.TryValidateObject(
				appSettings,
				new ValidationContext(appSettings),
				validationResults,
				true);
			Validator.TryValidateObject(
				appSettings.Engine,
				new ValidationContext(appSettings.Engine),
				validationResults,
				true);

			if (validationResults.Count > 0)
			{
				await ShowErrorMessageAsync("Ошибки в параметрах приложения:");

				StringBuilder errorMessageBuilder = new StringBuilder();
				validationResults.ForEach(async error =>
				{
					errorMessageBuilder.Clear();
					errorMessageBuilder.Append(error.ErrorMessage);

					if (error.MemberNames.Any())
					{
						errorMessageBuilder
							.Append("\r\n\t")
							.Append("Параметры: ")
							.AppendJoin(',', error.MemberNames);
					}

					await ShowErrorMessageAsync(errorMessageBuilder.ToString());
				});

				return null;
			}

			return appSettings;
		}

		private static void ShowInfo(EngineSettings engineSettings)
		{
			Console.WriteLine("Адрес электронной почты: " + engineSettings.Email);
			Console.WriteLine("Путь сохранения файлов: " + engineSettings.SavePath);
			Console.WriteLine(new string('-', Console.WindowWidth));
		}

		private static async Task ShowErrorMessageAsync(string errorMessage)
		{
			Console.BackgroundColor = ConsoleColor.Black;
			Console.ForegroundColor = ConsoleColor.Red;

			await Console.Error.WriteLineAsync(errorMessage);

			Console.ResetColor();
		}

		private static bool OneMoreCourseRequest()
		{
			Console.WriteLine("Загрузить ещё один курс?");
			Console.WriteLine("1. Да");
			Console.WriteLine("2. Нет");

			while (true)
			{
				switch (Console.ReadKey(true).KeyChar)
				{
					case '1':
						return true;
					case '2':
						return false;
					default:
						Console.Write("\a\b \b");
						break;
				}
			}
		}

		private static async Task<bool> DownloadCourseAsync(Uri uri)
		{
			try
			{
				_course = await _engine.GetCourseAsync(uri);
			}
			catch (Exception exception)
			{
				await ShowErrorMessageAsync(exception.Message);
			}

			if (_course == null)
			{
				Console.WriteLine("Данные не получены.");
				return false;
			}

			if (string.IsNullOrWhiteSpace(_course.Title))
			{
				Console.WriteLine("Не удалось извлечь данные курса.");
			}
			else
			{
				Console.Clear();
				Console.BackgroundColor = ConsoleColor.Blue;
				Console.WriteLine(_course.Title);
				Console.ResetColor();

				if (_course.IncorrectFiles.Count > 0)
				{
					Console.WriteLine("Не удалось получить ссылки для следующих файлов:");
					foreach (string fileTitle in _course.IncorrectFiles)
					{
						Console.WriteLine($" - {fileTitle}");
					}
				}
				else
				{
					Console.WriteLine("Файлы курса:");
					Console.CursorVisible = false;

					// TODO: Убрать заглушку.
					Console.WriteLine("Заглушка - загрузка файлов");
					//await DownloadFiles(_course.CorrectFiles);
				}
			}

			return true;
		}

		private static async Task DownloadFilesAsync(List<DownloadFile> downloadFiles)
		{
			foreach (DownloadFile file in downloadFiles)
			{
				int cursorTop = Console.CursorTop;
				WriteFileInfo(file, cursorTop);

				file.SizeChanged += (sender, e) =>
				{
					WriteFileInfo(file, cursorTop);
				};

				file.ProgressPercentageChanged += (sender, e) =>
				{
					WriteFileInfo(file, cursorTop);
				};

				file.StatusChanged += (sender, e) =>
				{
					WriteFileInfo(file, cursorTop);
				};
			}

			int footerCursorTop = Console.CursorTop;

			bool result = await _engine.DownloadFilesAsync(_course);

			Console.CursorVisible = true;
			Console.CursorTop = footerCursorTop;
			if (result)
			{
				Console.WriteLine("Все файлы загружены.");
			}
			else
			{
				Console.WriteLine("Загружены не все файлы.");

				foreach (DownloadFile file in downloadFiles.Where(file => file.Status == DownloadFileStatus.Error))
				{
					Console.Write($"{file.Title}: ");
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine(GetErrorDescription(file.Error));
					Console.ResetColor();
				}
			}
		}

		private static void WriteFileInfo(DownloadFile file, int cursorTop)
		{
			string fileInfo = $"{file.ProgressPercentage,4:D}% из {file.FormattedSize}: {file.Title}";
			ConsoleColor color = file.Status switch
			{
				DownloadFileStatus.InProgress => ConsoleColor.Yellow,
				DownloadFileStatus.Error => ConsoleColor.Red,
				DownloadFileStatus.Completed => ConsoleColor.Green,
				_ => Console.ForegroundColor,
			};
			//switch (file.Status)
			//{
			//	case DownloadFileStatus.InProgress:
			//		color = ConsoleColor.Yellow;
			//		break;

			//	case DownloadFileStatus.Error:
			//		color = ConsoleColor.Red;
			//		break;

			//	case DownloadFileStatus.Completed:
			//		color = ConsoleColor.Green;
			//		break;

			//	default:
			//		color = Console.ForegroundColor;
			//		break;
			//}

			lock (_consoleWriterLock)
			{
				Console.CursorTop = cursorTop;
				Console.ForegroundColor = color;
				Console.WriteLine(fileInfo);
				Console.ResetColor();
			}
		}

		private static string GetErrorDescription(Exception exception)
		{
			Exception currentException = exception;
			List<string> errorMessages = new List<string>();
			do
			{
				errorMessages.Add(currentException.Message);
			} while ((currentException = currentException.InnerException) != null);

			return string.Join(" -> ", errorMessages);
		}
	}
}

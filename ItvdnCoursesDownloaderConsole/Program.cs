using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Downloader;
using Downloader.Models;

namespace ItvdnCoursesDownloaderConsole
{
    internal class Program
    {
        private static Parameters _parameters;
        private static Engine _engine;
        private static Course _course;
        private static readonly object _consoleWriterLock = new object();

        private static async Task Main(string[] args)
        {
            _parameters = new Parameters(args);
            CancellationTokenSource cancellation = new CancellationTokenSource();

            _engine = new Engine(cancellation.Token)
            {
                Email = _parameters.Email,
                Password = _parameters.Password,
                //MaxDownloadThread = 3
            };

            do
            {
                Console.Clear();
                Console.Write("Адрес курса: ");
                string courseUri = Console.ReadLine();

                Console.WriteLine("Загрузка информации о курсе...");

                try
                {
                    bool result = await DownloadCourse(courseUri);
                }
                catch (Exception exception)
                {
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.ForegroundColor = ConsoleColor.Red;

                    Console.Error.WriteLine($"Ошибка: {GetErrorDescription(exception)}");
                    Console.ResetColor();
                }
            } while (OneMoreCourseRequest());
        }

        private static bool OneMoreCourseRequest()
        {
            Console.WriteLine("Загрузить ещё один курс?");
            Console.WriteLine("1. Да");
            Console.WriteLine("2. Нет");

            while (true)
            {
                switch (Console.ReadKey().KeyChar)
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

        private static async Task<bool> DownloadCourse(string uri)
        {
            _course = await _engine.GetCourseAsync(uri);

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

                    await DownloadFiles(_course.CorrectFiles);
                }
            }

            return true;
        }

        private static async Task DownloadFiles(List<DownloadFile> downloadFiles)
        {
            foreach (DownloadFile file in downloadFiles)
            {
                int cursorTop = Console.CursorTop;
                WriteFileInfo(file, cursorTop);

                file.SizeChanged += (sender, e) =>
                {
                    WriteFileInfo(file, cursorTop);
                    //DownloadFile downloadFile = sender as DownloadFile;
                    //WriteFileInfo(downloadFile, cursorTop);
                };

                file.ProgressPercentageChanged += (sender, e) =>
                {
                    WriteFileInfo(file, cursorTop);
                    //DownloadFile downloadFile = sender as DownloadFile;
                    //WriteFileInfo(downloadFile, cursorTop);
                };

                file.StatusChanged += (sender, e) =>
                {
                    WriteFileInfo(file, cursorTop);
                    //DownloadFile downloadFile = sender as DownloadFile;
                    //WriteFileInfo(downloadFile, cursorTop);
                };
            }

            int footerCursorTop = Console.CursorTop;

            bool result = await _engine.DownloadFilesAsync(_course, _parameters.SavePath);

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
                    //Exception exception = file.Error;
                    //var errorMessages = new List<string>();
                    //do
                    //{
                    //    errorMessages.Add(exception.Message);
                    //} while ((exception = exception.InnerException) != null);

                    //Console.Write($"{file.Title}: ");
                    //Console.ForegroundColor = ConsoleColor.Red;
                    //Console.WriteLine(string.Join(" -> ", errorMessages));
                    //Console.ResetColor();
                }
            }
        }

        private static void WriteFileInfo(DownloadFile file, int cursorTop)
        {
            string fileInfo = $"{file.ProgressPercentage,4:D}% из {file.FormattedSize}: {file.Title}";

            ConsoleColor color;
            switch (file.Status)
            {
                case DownloadFileStatus.InProgress:
                    color = ConsoleColor.Yellow;
                    break;
                case DownloadFileStatus.Error:
                    color = ConsoleColor.Red;
                    break;
                case DownloadFileStatus.Completed:
                    color = ConsoleColor.Green;
                    break;
                default:
                    color = Console.ForegroundColor;
                    break;
            }

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
            var errorMessages = new List<string>();
            do
            {
                errorMessages.Add(currentException.Message);
            } while ((currentException = currentException.InnerException) != null);

            return string.Join(" -> ", errorMessages);
        }
    }
}

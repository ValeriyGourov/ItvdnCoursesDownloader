﻿using System;
using System.Collections.Generic;
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
                bool result = await DownloadCourse(courseUri);
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

                var (correctFiles, incorrectFiles) = _course.GetDownloadFiles();
                if (incorrectFiles.Count > 0)
                {
                    Console.WriteLine("Не удалось получить ссылки для следующих файлов:");
                    foreach (string fileTitle in incorrectFiles)
                    {
                        Console.WriteLine($" - {fileTitle}");
                    }
                }
                else
                {
                    Console.WriteLine("Файлы курса:");
                    Console.CursorVisible = false;

                    await DownloadFiles(correctFiles);
                }
            }

            return true;
        }

        private static async Task DownloadFiles(List<DownloadFile> downloadFiles)
        {
            //Dictionary<DownloadFile, int> filesCursorsTops = new Dictionary<DownloadFile, int>();
            foreach (DownloadFile file in downloadFiles)
            {
                int cursorTop = Console.CursorTop;
                //filesCursorsTops.Add(file, cursorTop);
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
            //Dictionary<DownloadFile, int> filesCursorsTops = course.GetDownloadFiles()
            //    .ToDictionary(key => key, _ => default(int));
            //foreach (KeyValuePair<DownloadFile, int> item in filesCursorsTops)
            //{
            //    DownloadFile file = item.Key;
            //    int cursorTop = Console.CursorTop;
            //    filesCursorsTops[file] = cursorTop;
            //    WriteFileInfo(file, cursorTop);

            //    file.SizeChanged += (sender, e) =>
            //    {
            //        DownloadFile downloadFile = sender as DownloadFile;
            //        //Console.WriteLine($"{downloadFile.Title}: {downloadFile.Size} байт");
            //        WriteFileInfo(file, cursorTop);
            //    };
            //}

            //var downloadFiles = course.GetDownloadFiles();
            //downloadFiles
            //    .ForEach(file => file.SizeChanged += (sender, e) =>
            //    {
            //        DownloadFile downloadFile = sender as DownloadFile;
            //        Console.WriteLine($"{downloadFile.Title}: {downloadFile.Size} байт");
            //    });
            //.ForEach(file => file.SizeChanged += (sender, e) => Console.WriteLine($"{file.Title}: {file.Size} байт"));
            //.ForEach(file => file.SizeChanged += (sender, size) => Console.WriteLine($"{file.Title}: {file.Size} байт"));

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

                //foreach (DownloadFile file in downloadFiles.Where(file => file.Error != null))
                //{
                //    Console.WriteLine($"{file.Title}:\n\t{file.Error.ToString()}");
                //}
            }
        }

        //private static void File_ProgressPercentageChanged(object sender, EventArgs e)
        //{
        //    throw new NotImplementedException();
        //}

        private static void WriteFileInfo(DownloadFile file, int cursorTop)
        {
            string fileInfo = $"{file.ProgressPercentage,4:D}% из {file.Size} байт: {file.Title}";

            ConsoleColor color;
            switch (file.Status)
            {
                //case DownloadFileStatus.Wait:
                //    break;
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
            //if (file.Error != null)
            //{
            //    color = ConsoleColor.Red;
            //}
            //else if (file.ProgressPercentage > 0)
            //{
            //    color = ConsoleColor.Yellow;
            //}
            //else if (file.ProgressPercentage == 100)
            //{
            //    color = ConsoleColor.Green;
            //}
            //else
            //{
            //    color = Console.ForegroundColor;
            //}

            lock (_consoleWriterLock)
            {
                Console.CursorTop = cursorTop;
                Console.ForegroundColor = color;
                Console.WriteLine(fileInfo);
                Console.ResetColor();
            }
        }
    }
}

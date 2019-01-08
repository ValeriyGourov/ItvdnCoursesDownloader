using System;
using System.Collections.Generic;
using System.Linq;

namespace Downloader.Models
{
    public sealed class Course
    {
        private List<DownloadFile> _correctFiles;
        private List<string> _incorrectFiles;

        public string Title { get; internal set; }
        public DownloadFile Materials { get; internal set; }
        public List<Lesson> Lessons { get; internal set; }
        public static string MaterialsTitle => "Материалы курса";

        public List<DownloadFile> CorrectFiles
        {
            get
            {
                if (_correctFiles == null)
                {
                    SetDownloadFiles();
                }
                return _correctFiles;
            }
        }

        public List<string> IncorrectFiles
        {
            get
            {
                if (_incorrectFiles == null)
                {
                    SetDownloadFiles();
                }
                return _incorrectFiles;
            }
        }

        internal Course()
        {
        }

        private void SetDownloadFiles()
        {
            Dictionary<string, DownloadFile> downloadFiles = Lessons
                .ToDictionary(key => $"({key.Number}) {key.Title}", element => element.Video);
            if (Materials != null)
            {
                // Файла материалов курса может и не быть.
                downloadFiles.Add(MaterialsTitle, Materials);
            }

            _correctFiles = new List<DownloadFile>();
            _incorrectFiles = new List<string>();

            foreach (var item in downloadFiles)
            {
                DownloadFile file = item.Value;
                if (file != null)
                {
                    _correctFiles.Add(file);
                }
                else
                {
                    _incorrectFiles.Add(item.Key);
                }
            }
        }

        internal void ChangeSavePath(string savePath)
        {
            if (string.IsNullOrWhiteSpace(savePath))
            {
                throw new ArgumentNullException(nameof(savePath));
            }

            CorrectFiles.ForEach(file => file.SavePath = savePath);
        }
    }
}

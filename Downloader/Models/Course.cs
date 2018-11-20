using System;
using System.Collections.Generic;
using System.Linq;

namespace Downloader.Models
{
    public sealed class Course
    {
        public string Title { get; internal set; }
        public DownloadFile Materials { get; internal set; }
        //public Uri MaterialsUri { get; internal set; }
        public List<Lesson> Lessons { get; internal set; }
        public static string MaterialsTitle => "Материалы курса";

        internal Course()
        {
        }

        public (List<DownloadFile> CorrectFiles, List<string> IncorrectFiles) GetDownloadFiles()
        {
            Dictionary<string, DownloadFile> downloadFiles = Lessons
                .ToDictionary(key => key.Title, element => element.Video);
            //var downloadFiles = Lessons
            //    .Select(lesson => lesson.Video)
            //    .ToDictionary(key => key.Title, element => element);
            downloadFiles.Add(MaterialsTitle, Materials);
            //downloadFiles.Add(Materials);

            var correctFiles = new List<DownloadFile>();
            var incorrectFiles = new List<string>();

            foreach (var item in downloadFiles)
            {
                DownloadFile file = item.Value;
                if (file != null)
                {
                    correctFiles.Add(file);
                }
                else
                {
                    incorrectFiles.Add(item.Key);
                }
            }


            //if (Materials != null)
            //{
            //    AddCorrectFile(Materials, correctFiles);
            //}
            //else
            //{
            //    AddIncorrectFile(MaterialsTitle, incorrectFiles);
            //}

            //foreach (Lesson lesson in Lessons)
            //{
            //    if (lesson.Video != null)
            //    {
            //        AddCorrectFile(lesson.Video, correctFiles);
            //    }
            //    else
            //    {
            //        AddIncorrectFile(lesson.Title, incorrectFiles);
            //    }
            //}

            return (correctFiles, incorrectFiles);
        }

        //private void AddCorrectFile(DownloadFile downloadFile, List<DownloadFile> correctFiles) => correctFiles.Add(downloadFile);

        //private void AddIncorrectFile(string title, List<string> incorrectFiles) => incorrectFiles.Add(title);

        //public List<DownloadFile> GetDownloadFiles()
        //{
        //    var downloadFiles = Lessons
        //        .Select(lesson => lesson.Video)
        //        .ToList();
        //    downloadFiles.Insert(0, Materials);
        //    //downloadFiles.Add(Materials);

        //    return downloadFiles;
        //}

        internal void ChangeSavePath(string savePath)
        {
            if (string.IsNullOrWhiteSpace(savePath))
            {
                throw new ArgumentNullException(nameof(savePath));
            }

            GetDownloadFiles().CorrectFiles.ForEach(file => file.SavePath = savePath);
        }
    }
}

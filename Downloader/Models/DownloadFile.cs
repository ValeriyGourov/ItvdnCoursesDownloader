using System;
using System.IO;

namespace Downloader.Models
{
    public sealed class DownloadFile
    {
        //private readonly string _savePath;
        private string _title;
        private long _size;
        private int _progressPercentage;
        private DownloadFileStatus _status = DownloadFileStatus.Wait;
        private Exception _error;

        public Uri Uri { get; }

        public string Title
        {
            get => _title;
            set => _title = string.IsNullOrWhiteSpace(value) ? "Укажите имя файла" : value;
        }

        public string Extension { get; }
        public string TargetFileFullName => Path.Combine(SavePath, $"{Title}{Extension}");
        public string SavePath { get; set; }

        public int ProgressPercentage
        {
            get => _progressPercentage;
            internal set
            {
                bool progressPercentageChanged = _progressPercentage != value;

                _progressPercentage = value;

                if (ProgressPercentage == 100)
                {
                    Status = DownloadFileStatus.Completed;
                }
                else if (Status != DownloadFileStatus.InProgress)
                {
                    Status = DownloadFileStatus.InProgress;
                }
                else if (progressPercentageChanged)
                {
                    ProgressPercentageChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public long Size
        {
            get => _size;
            internal set
            {
                _size = value;
                SizeChanged?.Invoke(this, EventArgs.Empty);
                //SizeChanged?.Invoke(this, _size);
                //SizeChanged?.Invoke(this, new DownloadFileSizeChangedEventArgs() { Size = _size });
            }
        }

        public Exception Error
        {
            get => _error;
            internal set
            {
                _error = value;
                if (_error != null)
                {
                    Status = DownloadFileStatus.Error;
                }
            }
        }

        public DownloadFileStatus Status
        {
            get => _status;
            /*internal*/
            private set
            {
                _status = value;
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler ProgressPercentageChanged;
        public event EventHandler SizeChanged;
        public event EventHandler StatusChanged;

        internal DownloadFile(Uri uri/*, string savePath*/)
        {
            Uri = uri;
            //this._savePath = savePath;

            FileInfo fileInfo = new FileInfo(uri.AbsolutePath);
            Extension = fileInfo.Extension;

            //Status = DownloadFileStatus.Wait;
        }
    }
}

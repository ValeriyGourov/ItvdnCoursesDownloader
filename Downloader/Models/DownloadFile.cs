using System;
using System.IO;

using Downloader.Utilities;

namespace Downloader.Models;

public sealed class DownloadFile
{
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
			FormattedSize = FileSizeFormatHelper.FormatByteSize(_size);
			SizeChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	/// <summary>
	/// Форматированное представление размера файла.
	/// </summary>
	public string FormattedSize { get; private set; }

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
		private set
		{
			_status = value;
			StatusChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	public event EventHandler ProgressPercentageChanged;
	public event EventHandler SizeChanged;
	public event EventHandler StatusChanged;

	internal DownloadFile(Uri uri)
	{
		Uri = uri;

		FileInfo fileInfo = new(uri.AbsolutePath);
		Extension = fileInfo.Extension;
	}
}

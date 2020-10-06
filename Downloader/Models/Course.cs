using System;
using System.Collections.Generic;
using System.Linq;

namespace Downloader.Models
{
	/// <summary>
	/// Извлечённые данные для загрузки отдельного курса.
	/// </summary>
	public sealed class Course
	{
		private List<DownloadFile> _correctFiles;
		private List<string> _incorrectFiles;

		/// <summary>
		/// Название курса.
		/// </summary>
		public string Title { get; internal set; }

		/// <summary>
		/// Название курса, не содержащее недопустимых для файлов символов.
		/// </summary>
		public string FileSafeTitle { get; internal set; }

		/// <summary>
		/// Данные для загрузки дополнительных материалов курса.
		/// </summary>
		public DownloadFile Materials { get; internal set; }

		/// <summary>
		/// Данные для загрузки уроков курса.
		/// </summary>
		public List<Lesson> Lessons { get; internal set; }

		/// <summary>
		/// Название для сохраняемого файла с дополнительными материалами курса.
		/// </summary>
		public static string MaterialsTitle => "Материалы курса";

		/// <summary>
		/// Список файлов курса, данные которых удалось корректно определить.
		/// </summary>
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

		/// <summary>
		/// Список определений файлов курса, данные которых не удалось корректно определить.
		/// </summary>
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

		/// <summary>
		/// Заполняет списки файлов для загрузки и данные которых получить не удалось.
		/// </summary>
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

			foreach (KeyValuePair<string, DownloadFile> item in downloadFiles)
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

		/// <summary>
		/// Изменяет для загружаемых файлов путь сохранения на указанный.
		/// </summary>
		/// <param name="savePath">Новый путь сохранения файлов.</param>
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

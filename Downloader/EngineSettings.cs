using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;

namespace Downloader
{
	/// <summary>
	/// Настройки движка загрузки данных.
	/// </summary>
	public sealed class EngineSettings : IValidatableObject
	{
		/// <summary>
		/// Адрес сайта. Значение по умолчанию: <see href="https://itvdn.com"/>.
		/// </summary>
		public Uri BaseAddress { get; set; } = new Uri("https://itvdn.com");

		/// <summary>
		/// Логин (адрес электронной почты) для авторизации на сайте.
		/// </summary>
		[Required, EmailAddress]
		public string Email { get; set; }

		/// <summary>
		/// Пароль для авторизации на сайте.
		/// </summary>
		[Required]
		public string Password { get; set; }

		/// <summary>
		/// Путь в локальной файловой системе для сохранения загруженных курсов.
		/// </summary>
		[Required]
		public string SavePath { get; set; }

		/// <summary>
		/// Путь к папке с файлами веб-драйверов для Selenium WebDriver.
		/// </summary>
		[Required]
		public string WebDriversPath { get; set; }

		/// <inheritdoc/>
		public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
		{
			List<ValidationResult> errors = new();

			if (!Path.IsPathFullyQualified(SavePath))
			{
				errors.Add(new ValidationResult(
					"Не указан полный путь к папке загрузки файлов.",
					new[] { nameof(SavePath) }));
			}
			else if (!Directory.Exists(SavePath))
			{
				errors.Add(new ValidationResult(
					$"Папка '{SavePath}', указанная для загрузки файлов, не существует.",
					new[] { nameof(SavePath) }));
			}

			if (!Directory.Exists(WebDriversPath))
			{
				errors.Add(new ValidationResult(
					$"Указанная папка веб-драйверов '{WebDriversPath}' не существует.",
					new[] { nameof(WebDriversPath) }));
			}

			return errors;
		}
	}
}

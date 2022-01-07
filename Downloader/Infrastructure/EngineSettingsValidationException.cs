using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Downloader.Infrastructure;

/// <summary>
/// Исключение, возникающее при проверке настроек движка.
/// </summary>
[Serializable]
public sealed class EngineSettingsValidationException : Exception
{
	/// <summary>
	/// Перечень результатов проверки некорректных настроек.
	/// </summary>
	public IEnumerable<ValidationResult> ValidationResults { get; } = new List<ValidationResult>();

	public EngineSettingsValidationException()
	{
	}

	public EngineSettingsValidationException(
		string message,
		List<ValidationResult> validationResults)
		: this(message, null, validationResults)
	{
	}

	public EngineSettingsValidationException(
		string message,
		Exception innerException,
		List<ValidationResult> validationResults)
		: base(message, innerException)
	{
		ValidationResults = validationResults;
	}
}

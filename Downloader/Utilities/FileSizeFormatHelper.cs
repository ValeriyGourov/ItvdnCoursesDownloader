using System.Text;

namespace Downloader.Utilities;

/// <summary>
/// Вспомогательный класс для форматирования размера файла.
/// </summary>
public static class FileSizeFormatHelper
{
	/// <summary>
	/// Преобразовывает числовое значение в строковое, которое представляет число, выраженное как значение размера в байтах, килобайтах, мегабайтах и т.д., в зависимости от размера.
	/// </summary>
	/// <param name="fileSize">Числовое значение размера файла для конвертации.</param>
	/// <returns>Форматированное представление размера файла.</returns>
	public static string FormatByteSize(long fileSize)
	{
		StringBuilder buffer = new(11);
		NativeMethods.StrFormatByteSize(fileSize, buffer, buffer.Capacity);
		return buffer.ToString();
	}
}

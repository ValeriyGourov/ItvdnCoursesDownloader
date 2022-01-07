using System.Runtime.InteropServices;
using System.Text;

namespace Downloader.Utilities;

/// <summary>
/// Служебный класс для использования P/Invokes.
/// </summary>
internal static class NativeMethods
{
	/// <summary>
	/// Преобразовывает числовое значение в строковое, которое представляет число, выраженное как значение размера в байтах, килобайтах, мегабайтах и т.д., в зависимости от размера.
	/// </summary>
	/// <param name="fileSize">Конвертируемое числовое значение.</param>
	/// <param name="buffer">Буфер, принимающий конвертированную строку.</param>
	/// <param name="bufferSize">Размер буфера в символах.</param>
	/// <returns>Конвертированная строка или null, если конвертация завершилась неудачей.</returns>
	[DllImport("Shlwapi.dll", CharSet = CharSet.Unicode)]
	internal static extern long StrFormatByteSize(long fileSize, StringBuilder buffer, int bufferSize);
}
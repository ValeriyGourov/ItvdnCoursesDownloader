using System;

namespace Downloader.Infrastructure;

[Serializable]
public sealed class PageParseException : ApplicationException
{
	public PageParseException(string message, string className = null) : base(message)
	{
		Data["ClassName"] = className;
	}
}

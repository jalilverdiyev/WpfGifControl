using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Windows;

namespace WpfGifControl.Helpers;

public static class ResourcesHelper
{
	private const string PackUriHeader = "pack://application:,,,/{0};component{1}";

	public static Stream? GetResourceStream(string resource)
		=> Application.GetResourceStream(new Uri(resource))?.Stream;

	public static Stream? GetResourceStream(Uri uri)
		=> Application.GetResourceStream(uri)?.Stream;

	public static bool TryCreatePackUri(string strUri,[NotNullWhen(true)] out Uri? result)
	{
		var execAssemblyName = Assembly.GetEntryAssembly()?.GetName()?.Name;
		strUri = !strUri.StartsWith('/') ? '/' + strUri : strUri;
		var packUriStr = string.Format(PackUriHeader, execAssemblyName, strUri);
		return Uri.TryCreate(packUriStr, UriKind.RelativeOrAbsolute, out result);
	}
}

namespace JSAI.WinApp;

public static class StringExtensions
{
	public static string OrDefault(this string? value, string fallback)
	{
		return string.IsNullOrWhiteSpace(value) ? fallback : value;
	}
}

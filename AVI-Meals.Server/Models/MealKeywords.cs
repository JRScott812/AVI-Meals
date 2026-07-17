using System.Text.RegularExpressions;

namespace AVI_Meals.Server.Models;

/// <summary>
/// Derives search/theme keywords from meal text.
/// </summary>
public static partial class MealKeywords
{
	private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
	{
		"and", "the", "with", "for", "your", "our", "fresh", "includes", "include", "served", "service",
		"buffet", "box", "boxed", "person", "meal", "meals", "assorted", "choice", "selection", "style",
		"house", "made", "day", "available", "option", "options", "add", "ice", "water", "tea", "coffee"
	};

	public static IReadOnlyList<string> Extract(string name, string description)
	{
		return [.. WordRegex()
			.Matches($"{name} {description}")
			.Select(match => match.Value.Trim().ToLowerInvariant())
			.Where(word => word.Length > 3 && !StopWords.Contains(word))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Take(8)];
	}

	[GeneratedRegex("[A-Za-z][A-Za-z'`-]+", RegexOptions.CultureInvariant)]
	private static partial Regex WordRegex();
}

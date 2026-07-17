using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

using AVI_Meals.Server.Models;

namespace AVI_Meals.Server.Services;

/// <summary>
/// Parses CaterTrax category and product HTML into meal items.
/// </summary>
internal static partial class CaterTraxMenuScraper
{
	public sealed record CategoryLink(string Name, string Url);

	public static string? ExtractFirstAbsoluteUrl(string html, string hostHint)
	{
		foreach (Match match in HrefRegex().Matches(html))
		{
			string href = WebUtility.HtmlDecode(match.Groups["href"].Value).Trim();
			if (!Uri.TryCreate(href, UriKind.Absolute, out Uri? uri))
			{
				continue;
			}

			if (OutboundUrlGuard.IsAllowedWithHostHint(uri, hostHint))
			{
				return uri.ToString();
			}
		}

		return null;
	}

	public static List<CategoryLink> ExtractCategories(string html, string baseUrl)
	{
		MatchCollection matches = CategoryRegex().Matches(html);
		List<CategoryLink> categories = new(matches.Count);

		foreach (Match match in matches)
		{
			string href = match.Groups["href"].Value;
			string name = NormalizeTextValue(match.Groups["name"].Value);
			if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(name))
			{
				continue;
			}

			string? absoluteUrl = TryMakeAllowedAbsoluteUrl(baseUrl, href);
			if (absoluteUrl is null)
			{
				continue;
			}

			categories.Add(new CategoryLink(name, absoluteUrl));
		}

		return categories;
	}

	public static IEnumerable<MealItem> ExtractMeals(string categoryName, string html, string baseUrl)
	{
		foreach (Match match in ProductRegex().Matches(html))
		{
			string name = NormalizeTextValue(match.Groups["name"].Value);
			string description = NormalizeTextValue(StripHtml(match.Groups["description"].Value));
			string priceText = NormalizeTextValue(match.Groups["price"].Value);
			string productHref = match.Groups["href"].Value;

			if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(priceText))
			{
				continue;
			}

			if (!TryParsePrice(priceText, out decimal price))
			{
				continue;
			}

			string? productUrl = TryMakeAllowedAbsoluteUrl(baseUrl, productHref);
			if (productUrl is null)
			{
				continue;
			}

			yield return new MealItem(
				categoryName,
				name,
				description,
				price,
				priceText,
				productUrl,
				ExtractKeywords(name, description));
		}
	}

	private static IReadOnlyList<string> ExtractKeywords(string name, string description)
	{
		HashSet<string> stopWords = new(StringComparer.OrdinalIgnoreCase)
		{
			"and", "the", "with", "for", "your", "our", "fresh", "includes", "include", "served", "service",
			"buffet", "box", "boxed", "person", "meal", "meals", "assorted", "choice", "selection", "style",
			"house", "made", "day", "available", "option", "options", "add", "ice", "water", "tea", "coffee"
		};

		return [.. WordRegex()
			.Matches($"{name} {description}")
			.Select(match => match.Value.Trim().ToLowerInvariant())
			.Where(word => word.Length > 3 && !stopWords.Contains(word))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Take(8)];
	}

	private static bool TryParsePrice(string priceText, out decimal price)
	{
		return decimal.TryParse(
			priceText.Replace("$", string.Empty, StringComparison.Ordinal),
			NumberStyles.Number,
			CultureInfo.InvariantCulture,
			out price);
	}

	private static string? TryMakeAllowedAbsoluteUrl(string baseUrl, string href)
	{
		if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri))
		{
			return null;
		}

		Uri absoluteUri = new(baseUri, WebUtility.HtmlDecode(href));
		return OutboundUrlGuard.IsAllowed(absoluteUri) ? absoluteUri.ToString() : null;
	}

	private static string StripHtml(string value)
	{
		string withoutBreaks = value.Replace("<br>", " ", StringComparison.OrdinalIgnoreCase)
			.Replace("<br/>", " ", StringComparison.OrdinalIgnoreCase)
			.Replace("<br />", " ", StringComparison.OrdinalIgnoreCase)
			.Replace("</p>", " ", StringComparison.OrdinalIgnoreCase)
			.Replace("<p>", " ", StringComparison.OrdinalIgnoreCase);

		return HtmlTagRegex().Replace(withoutBreaks, " ");
	}

	private static string NormalizeTextValue(string value) =>
		WhitespaceRegex().Replace(WebUtility.HtmlDecode(value), " ").Trim();

	[GeneratedRegex("href\\s*=\\s*['\"](?<href>[^'\"]+)['\"]", RegexOptions.IgnoreCase)]
	private static partial Regex HrefRegex();

	[GeneratedRegex("<a class='category-tile' href='(?<href>[^']+)'.*?<div class='tile-title'>(?<name>.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex CategoryRegex();

	[GeneratedRegex("<div class='product-slot'>\\s*<a class='product-tile' href=\"(?<href>[^\"]+)\">.*?<div class='tile-title'>(?<name>.*?)</div>.*?<div class='tile-description'>(?<description>.*?)</div>.*?<div class='cost'>(?<price>.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex ProductRegex();

	[GeneratedRegex("[A-Za-z][A-Za-z'`-]+", RegexOptions.CultureInvariant)]
	private static partial Regex WordRegex();

	[GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
	private static partial Regex HtmlTagRegex();

	[GeneratedRegex("\\s+")]
	private static partial Regex WhitespaceRegex();
}

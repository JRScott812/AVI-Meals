using System.Net;
using System.Net.Sockets;

namespace AVI_Meals.Server.Services;

/// <summary>
/// Validates outbound URLs against an HTTPS host allowlist and blocks private addresses.
/// </summary>
public static class OutboundUrlGuard
{
	private static readonly string[] AllowedHostSuffixes =
	[
		"connecttaylor.atriumcampus.com",
		"aviserves.com",
		"catertrax.com",
		"dish.avifoodsystems.com"
	];

	/// <summary>
	/// Returns true when the URI is HTTPS and its host is on the dining-source allowlist.
	/// </summary>
	public static bool IsAllowed(Uri? uri)
	{
		return uri is not null
			&& uri.IsAbsoluteUri
			&& string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrWhiteSpace(uri.Host) && (!IPAddress.TryParse(uri.Host, out IPAddress? address) || !IsPrivateOrLocal(address)) && AllowedHostSuffixes.Any(suffix => HostMatchesSuffix(uri.Host, suffix));
	}

	/// <summary>
	/// Returns true when the URI is allowed and its host matches the expected dining-source hint.
	/// </summary>
	public static bool IsAllowedWithHostHint(Uri? uri, string hostHint) => IsAllowed(uri) && HostMatchesSuffix(uri!.Host, hostHint);

	/// <summary>
	/// Throws when the URI is not an allowed outbound dining-source URL.
	/// </summary>
	public static void EnsureAllowed(Uri? uri)
	{
		if (!IsAllowed(uri))
		{
			throw new InvalidOperationException($"Outbound request blocked for untrusted URL: {uri}");
		}
	}

	private static bool HostMatchesSuffix(string host, string suffix)
	{
		return host.Equals(suffix, StringComparison.OrdinalIgnoreCase)
			|| host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsPrivateOrLocal(IPAddress address)
	{
		if (IPAddress.IsLoopback(address)
			|| address.Equals(IPAddress.Any)
			|| address.Equals(IPAddress.IPv6Any)
			|| address.IsIPv6LinkLocal
			|| address.IsIPv6SiteLocal
			|| address.IsIPv6UniqueLocal)
		{
			return true;
		}

		if (address.AddressFamily != AddressFamily.InterNetwork)
		{
			return false;
		}

		byte[] bytes = address.GetAddressBytes();
		return bytes[0] == 10
			|| (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
			|| (bytes[0] == 192 && bytes[1] == 168)
			|| (bytes[0] == 169 && bytes[1] == 254);
	}
}
